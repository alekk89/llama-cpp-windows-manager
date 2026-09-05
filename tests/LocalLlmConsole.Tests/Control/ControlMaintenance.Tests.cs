using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlMaintenanceTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task CacheDryRunPreservesFilesAndExecutionClearsOnlyCache()
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        Directory.CreateDirectory(fixture.Settings.CacheRoot);
        var cached = Path.Combine(fixture.Settings.CacheRoot, "download.tmp");
        var sentinel = Path.Combine(fixture.Root, "keep.txt");
        await File.WriteAllTextAsync(cached, "cache", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(sentinel, "keep", TestContext.Current.CancellationToken);

        var plan = await fixture.ExecuteAsync("cache.clear", dryRun: true);
        Assert.True(plan["wouldClear"]!.GetValue<bool>());
        Assert.True(File.Exists(cached));
        var result = await fixture.ExecuteAsync("cache.clear");
        Assert.True(result["cleared"]!.GetValue<bool>());
        Assert.False(File.Exists(cached));
        Assert.Equal("keep", await File.ReadAllTextAsync(sentinel, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CacheClearRefusesAnActiveJobAndPreservesItsFiles()
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        Directory.CreateDirectory(fixture.Settings.CacheRoot);
        var cached = Path.Combine(fixture.Settings.CacheRoot, "active.tmp");
        File.WriteAllText(cached, "active");
        var job = await fixture.Jobs.CreateAsync("runtime-build", "{}", TestContext.Current.CancellationToken);
        await fixture.Jobs.UpdateAsync(job, JobStatus.Running, "{}", TestContext.Current.CancellationToken);

        var result = await fixture.ExecuteAsync("cache.clear");
        Assert.False(result["wouldClear"]!.GetValue<bool>());
        Assert.Equal("active", File.ReadAllText(cached));
    }

    [Fact]
    public async Task LogDeletionRejectsTraversalAndProtectsActiveSessions()
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        var logs = Path.Combine(fixture.Root, "logs");
        Directory.CreateDirectory(logs);
        var active = Path.Combine(logs, "active.log");
        var inactive = Path.Combine(logs, "old.log");
        File.WriteAllText(active, "running");
        File.WriteAllText(inactive, "old");
        fixture.Sessions.Add(RuntimeSession(fixture.Root, fixture.Settings, LoadedModelSessionStatus.Running, true) with { LogPath = active });

        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExecuteAsync("logs.delete", new JsonObject { ["file"] = "../keep.txt" }));
        var blocked = await fixture.ExecuteAsync("logs.delete", new JsonObject { ["file"] = "active.log" });
        Assert.False(blocked["CanDelete"]!.GetValue<bool>());
        await fixture.ExecuteAsync("logs.delete-all", dryRun: true);
        Assert.True(File.Exists(inactive));
        await fixture.ExecuteAsync("logs.delete-all");
        Assert.False(File.Exists(inactive));
        Assert.Equal("running", File.ReadAllText(active));
    }

    [Fact]
    public async Task DownloadHistoryDeletionPreservesCompletedModels()
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        var destination = Path.Combine(fixture.Settings.ModelsRoot, "download", "model.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        File.WriteAllText(destination, "keep model");
        File.WriteAllText(destination + ".partial", "stale");
        var payload = new DownloadJobPayload(new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "", 10, 0), destination);
        var job = await fixture.Jobs.CreateAsync("huggingface-download", JsonSerializer.Serialize(payload), TestContext.Current.CancellationToken);
        await fixture.Jobs.UpdateAsync(job, JobStatus.Completed, job.PayloadJson, TestContext.Current.CancellationToken);
        var body = new JsonObject { ["job"] = job.Id };

        await fixture.ExecuteAsync("downloads.delete", body, dryRun: true);
        Assert.Single(await fixture.Store.ListJobsAsync());
        Assert.True(File.Exists(destination + ".partial"));
        await fixture.ExecuteAsync("downloads.delete", body);
        Assert.Empty(await fixture.Store.ListJobsAsync());
        Assert.False(File.Exists(destination + ".partial"));
        Assert.Equal("keep model", File.ReadAllText(destination));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => fixture.ExecuteAsync("downloads.delete", body));
    }

    [Theory]
    [InlineData("windows.setup", "Cpu")]
    [InlineData("wsl.setup", "InstallWsl")]
    public async Task SetupRequiresConfirmationAndDryRunNeverLaunches(string operation, string action)
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        var body = new JsonObject { ["action"] = action };
        await fixture.ExecuteAsync(operation, body, dryRun: true);
        await fixture.ExecuteAsync(operation, body, confirm: false);
        Assert.Empty(fixture.Launched);
        var result = await fixture.ExecuteAsync(operation, body, confirm: true);
        Assert.Single(fixture.Launched);
        Assert.Equal("Started", result["outcome"]!.GetValue<string>());
    }

    [Fact]
    public async Task UpdateInstallRequiresConfirmationAndPropagatesCheckFailures()
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        var plan = await fixture.ExecuteAsync("updates.install", dryRun: true);
        Assert.True(plan["wouldInstall"]!.GetValue<bool>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExecuteAsync("updates.install", confirm: false));
        Assert.Empty(fixture.Scheduled);
        await fixture.ExecuteAsync("updates.install", confirm: true);
        Assert.Equal("v999.0.0", Assert.Single(fixture.Scheduled).LatestVersion);

        fixture.FailUpdateCheck = true;
        await Assert.ThrowsAsync<HttpRequestException>(() => fixture.ExecuteAsync("updates.install", confirm: true));
        Assert.Single(fixture.Scheduled);
    }

    [Fact]
    public async Task CancelledMaintenanceDoesNotClearFilesOrScheduleAnUpdate()
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        Directory.CreateDirectory(fixture.Settings.CacheRoot);
        var file = Path.Combine(fixture.Settings.CacheRoot, "keep.tmp");
        File.WriteAllText(file, "keep");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ExecuteAsync("cache.clear", new JsonObject(), false, true, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => fixture.Service.ExecuteAsync("updates.install", new JsonObject(), false, true, cancellation.Token));
        Assert.True(File.Exists(file));
        Assert.Empty(fixture.Scheduled);
    }

    [Theory]
    [InlineData("logs.delete", "file")]
    [InlineData("lifetime.delete", "model")]
    [InlineData("downloads.delete", "job")]
    [InlineData("windows.setup", "action")]
    [InlineData("wsl.select", "distro")]
    public async Task MissingParametersFailBeforeSideEffects(string operation, string parameter)
    {
        await using var fixture = await Fixture.CreateAsync(CreateTempRoot());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.ExecuteAsync(operation));
        Assert.Contains(parameter, error.Message, StringComparison.Ordinal);
        Assert.Empty(fixture.Launched);
        Assert.Empty(fixture.Scheduled);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        internal string Root { get; }
        internal AppSettings Settings { get; private set; }
        internal StateStore Store { get; }
        internal JobEngine Jobs { get; }
        internal List<LoadedModelSessionSnapshot> Sessions { get; } = [];
        internal List<ProcessStartInfo> Launched { get; } = [];
        internal List<AppUpdateInfo> Scheduled { get; } = [];
        internal bool FailUpdateCheck { get; set; }
        internal ControlNonRuntimeOperationApplicationService Service { get; }
        private readonly HuggingFaceService _downloads;
        private readonly HttpClient _http;
        private readonly AppUpdateService _updates;

        private Fixture(string root)
        {
            Root = root;
            Settings = AppSettings.CreateDefault(root);
            Store = new StateStore(Path.Combine(root, "state.db"));
            Jobs = new JobEngine(Store, Path.Combine(root, "logs"));
            _downloads = new HuggingFaceService(Store, Jobs, new ModelCatalogService(Store), new CapturingHttpHandler(_ => throw new InvalidOperationException("No download expected.")));
            _http = new HttpClient(new CapturingHttpHandler(_ => new HttpResponseMessage(FailUpdateCheck ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            {
                Content = new StringContent("""
                    {"tag_name":"v999.0.0","name":"Test release","assets":[
                      {"name":"LlamaCppWindowsManager.exe","size":100,"browser_download_url":"https://github.com/alekk89/llama-cpp-windows-manager/releases/download/v999.0.0/LlamaCppWindowsManager.exe"},
                      {"name":"LlamaCppWindowsManager.exe.sha256","size":64,"browser_download_url":"https://github.com/alekk89/llama-cpp-windows-manager/releases/download/v999.0.0/LlamaCppWindowsManager.exe.sha256"}
                    ]}
                    """)
            }));
            _updates = new AppUpdateService(_http, _ => throw new InvalidOperationException("Updates must only be scheduled."), currentVersion: () => "v1.0.0", allowUnsignedUpdates: true);
            var launcher = new VisibleCommandLaunchService(Launched.Add, () => "fake-wsl.exe");
            var windows = new WindowsToolSetupWorkflowService(launcher, () => WindowsBuildTools());
            var wsl = new WslToolSetupWorkflowService(launcher, () => "fake-wsl.exe");
            var logWorkflow = new LogPageWorkflowService(root, Store);
            Service = new ControlNonRuntimeOperationApplicationService(new ControlNonRuntimeOperationDependencies(
                new CacheClearWorkflowService(root, Store), _downloads,
                new LogPageApplicationService(logWorkflow), logWorkflow, new LifetimeMetricsApplicationService(Store),
                new DownloadHistoryApplicationService(new DownloadHistoryWorkflowService(Store, _downloads)),
                windows, new WindowsToolSetupApplicationService(windows), new WslEnvironmentService(),
                new WslPageWorkflowService(_ => Task.FromResult(ReadyWslReport()), new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", "")), () => "fake-wsl.exe"),
                wsl, new WslToolSetupApplicationService(wsl), new AppUpdateWorkflowService(_updates, root)),
                new ControlNonRuntimeOperationActions(() => Settings, () => Sessions,
                    (settings, _) => Task.FromResult(Settings = settings), _ => { }, (_, work) => work(), _ => { }, Scheduled.Add));
        }

        internal static async Task<Fixture> CreateAsync(string root)
        {
            var fixture = new Fixture(root);
            await fixture.Store.InitializeAsync();
            return fixture;
        }

        internal async Task<JsonNode> ExecuteAsync(string operation, JsonObject? body = null, bool dryRun = false, bool confirm = true)
            => JsonSerializer.SerializeToNode(await Service.ExecuteAsync(operation, body ?? new JsonObject(), dryRun, confirm, TestContext.Current.CancellationToken))!;

        public async ValueTask DisposeAsync()
        {
            _updates.Dispose();
            _http.Dispose();
            _downloads.Dispose();
            await Store.DisposeAsync();
        }
    }
}
