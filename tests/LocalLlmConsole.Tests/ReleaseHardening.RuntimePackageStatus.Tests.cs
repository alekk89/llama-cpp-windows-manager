using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void RuntimePackageStatusServiceBuildsInventoryRowsAndHonorsStateIdentity()
    {
        var root = CreateTempRoot();
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");
        var runtimeFolder = Path.Combine(root, "official-prebuilt-windows-cuda-b9354");
        var runtimeExecutable = CreateRuntimeExecutable(runtimeFolder);
        var runtime = new RuntimeRecord(
            "runtime-1",
            "Official llama.cpp CUDA Windows",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            runtimeExecutable,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = runtimeFolder,
                runtimeMetadata = new
                {
                    managedPackageId = preset.Id,
                    managedPresetId = preset.Id,
                    releaseTag = "b9354"
                }
            }),
            DateTimeOffset.UtcNow);
        var stale = new RuntimePackageUpdateState(true, "old", "b9355", "", "stale", DateTimeOffset.UtcNow, "package:old");
        var current = new RuntimePackageUpdateState(true, "b9354", "b9355", "", "current", DateTimeOffset.UtcNow, "package:b9354");
        var service = new RuntimePackageStatusService();

        var staleInventory = service.BuildInventory(preset, [runtime], new Dictionary<string, RuntimePackageUpdateState> { [preset.Id] = stale });
        var currentInventory = service.BuildInventory(preset, [runtime], new Dictionary<string, RuntimePackageUpdateState> { [preset.Id] = current });
        var row = service.CreateRow(preset, currentInventory);

        Assert.Null(staleInventory.CheckedState);
        Assert.Same(current, currentInventory.CheckedState);
        Assert.Equal("package:b9354", currentInventory.LocalIdentity);
        Assert.Equal("Update", row.InstallAction);
        Assert.True(row.CanInstall);
        Assert.Equal("current", row.Assets);
    }


    [Fact]
    public void RuntimePackageStatusServiceEvaluatesAvailableAndUnavailableChecks()
    {
        var root = CreateTempRoot();
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cuda");
        var runtimeFolder = Path.Combine(root, "official-prebuilt-windows-cuda-b9354");
        var runtimeExecutable = CreateRuntimeExecutable(runtimeFolder);
        var runtime = new RuntimeRecord(
            "runtime-1",
            "Official llama.cpp CUDA Windows",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            runtimeExecutable,
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = runtimeFolder,
                runtimeMetadata = new
                {
                    managedPackageId = preset.Id,
                    managedPresetId = preset.Id,
                    releaseTag = "b9354",
                    assets = new[] { new { name = "llama-b9354-bin-win-cuda-12.4-x64.zip" } }
                }
            }),
            DateTimeOffset.UtcNow);
        var service = new RuntimePackageStatusService();
        var inventory = service.BuildInventory(preset, [runtime], new Dictionary<string, RuntimePackageUpdateState>());
        var release = new RuntimePackageRelease(
            "b9354",
            "9777256c3130",
            "https://example.com/release",
            DateTimeOffset.UtcNow,
            [new RuntimePackageAsset("llama-b9354-bin-win-cuda-13.1-x64.zip", "https://example.com/asset.zip", 1024)]);
        var selection = new RuntimePackageSelection(preset, release.TagName, release.HtmlUrl, release.PublishedAt, release.Assets[0], []);

        var available = service.EvaluateAvailableRelease(inventory, release, selection, DateTimeOffset.UtcNow);
        var unavailableInventory = service.BuildInventory(preset, [], new Dictionary<string, RuntimePackageUpdateState>());
        var unavailable = service.EvaluateUnavailableRelease(unavailableInventory, release, "not published", DateTimeOffset.UtcNow);

        Assert.True(available.State.HasUpdate);
        Assert.Equal("Update available", available.LocalStatus);
        Assert.Contains("Package variant available", available.Message, StringComparison.Ordinal);
        Assert.Equal("Update", available.InstallAction);
        Assert.False(unavailable.State.IsAvailable);
        Assert.Equal("Not published", unavailable.LocalStatus);
        Assert.False(unavailable.CanInstall);
    }

    [Fact]
    public void RuntimePackageStatusMarksRegisteredMissingExecutablesForRepair()
    {
        var root = CreateTempRoot();
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");
        var runtimeFolder = Path.Combine(root, "official-prebuilt-windows-cpu-b10107");
        var missingRuntime = new RuntimeRecord(
            "runtime-cpu",
            "Official llama.cpp CPU Windows",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = runtimeFolder,
                runtimeMetadata = new
                {
                    managedPackageId = preset.Id,
                    managedPresetId = preset.Id,
                    releaseTag = "b10107"
                }
            }),
            DateTimeOffset.UtcNow);
        var service = new RuntimePackageStatusService();

        var inventory = service.BuildInventory(preset, [missingRuntime], new Dictionary<string, RuntimePackageUpdateState>());
        var row = service.CreateRow(preset, inventory);

        Assert.Empty(inventory.Installed);
        Assert.Equal([missingRuntime], inventory.Unavailable);
        Assert.Equal("Repair required", row.LocalStatus);
        Assert.Equal("Repair", row.InstallAction);
        Assert.True(row.CanInstall);
        Assert.True(row.CanDelete);
        Assert.Contains("missing", row.LatestRelease, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task RuntimePackageUpdateCheckServiceMapsAvailableAndUnavailableReleaseChecks()
    {
        var checkedAt = new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero);
        const string releaseJson = """
        {
          "tag_name": "b9354",
          "target_commitish": "9777256c3130",
          "html_url": "https://example.com/release",
          "published_at": "2026-05-28T10:00:00Z",
          "assets": [
            {
              "name": "llama-b9354-bin-win-cpu-x64.zip",
              "browser_download_url": "https://example.com/win-cpu.zip",
              "size": 1024
            }
          ]
        }
        """;
        using var handler = new CapturingHttpHandler(request =>
            request.RequestUri?.ToString() == RuntimePackageSourceCatalog.LatestReleaseApiUrl
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(releaseJson) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var status = new RuntimePackageStatusService();
        var service = new RuntimePackageUpdateCheckService(http, status);
        var availablePreset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");
        var unavailablePreset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-cuda");

        var available = await service.CheckAsync(new RuntimePackageUpdateCheckRequest(
            availablePreset,
            status.BuildInventory(availablePreset, [], new Dictionary<string, RuntimePackageUpdateState>()),
            "latest",
            checkedAt,
            TestContext.Current.CancellationToken));
        var unavailable = await service.CheckAsync(new RuntimePackageUpdateCheckRequest(
            unavailablePreset,
            status.BuildInventory(unavailablePreset, [], new Dictionary<string, RuntimePackageUpdateState>()),
            "latest",
            checkedAt,
            TestContext.Current.CancellationToken));

        Assert.False(available.AssetUnavailable);
        Assert.Equal("b9354", available.Result.State.LatestTag);
        Assert.True(available.Result.State.IsAvailable);
        Assert.Contains("Latest available release is b9354", available.Result.Message, StringComparison.Ordinal);
        Assert.True(unavailable.AssetUnavailable);
        Assert.False(unavailable.Result.State.IsAvailable);
        Assert.Contains("CUDA WSL/Linux", unavailable.Result.Message, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimePackageCheckWorkflowServiceOwnsCheckJobLifecycle()
    {
        var root = CreateTempRoot();
        const string releaseJson = """
        {
          "tag_name": "b9354",
          "target_commitish": "9777256c3130",
          "html_url": "https://example.com/release",
          "published_at": "2026-05-28T10:00:00Z",
          "assets": [
            {
              "name": "llama-b9354-bin-win-cpu-x64.zip",
              "browser_download_url": "https://example.com/win-cpu.zip",
              "size": 1024
            }
          ]
        }
        """;
        using var handler = new CapturingHttpHandler(request =>
            request.RequestUri?.ToString() == RuntimePackageSourceCatalog.LatestReleaseApiUrl
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(releaseJson) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var status = new RuntimePackageStatusService();
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");
        var jobs = new RuntimePackageJobService(new JobEngine(store, Path.Combine(root, "logs")));
        var checks = new RuntimePackageUpdateCheckService(http, status);
        var workflow = new RuntimePackageCheckWorkflowService(jobs, checks);
        var notifications = 0;

        var outcome = await workflow.CheckAsync(new RuntimePackageCheckWorkflowRequest(
            preset,
            status.BuildInventory(preset, [], new Dictionary<string, RuntimePackageUpdateState>()),
            "latest",
            BoundedLogFile.MegabytesToBytes(1),
            () =>
            {
                notifications++;
                return Task.CompletedTask;
            },
            new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken));
        var job = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimePackageJobService.ParsePayload(job.PayloadJson);
        var log = await File.ReadAllTextAsync(job.LogPath, TestContext.Current.CancellationToken);

        Assert.Equal(outcome.Job.Id, job.Id);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(payload);
        Assert.Equal("check", payload.Action);
        Assert.Equal(outcome.CheckResult.Message, payload.Message);
        Assert.Contains("Checking official llama.cpp release assets", log, StringComparison.Ordinal);
        Assert.Contains("Latest available release is b9354", log, StringComparison.Ordinal);
        Assert.True(notifications >= 3);
    }

    [Fact]
    public async Task RuntimePackageApplicationServiceCoordinatesInstallCheckAndDelete()
    {
        var root = CreateTempRoot();
        const string releaseJson = """
        {
          "tag_name": "b9355",
          "target_commitish": "abcdef999999",
          "html_url": "https://example.com/release",
          "published_at": "2026-05-28T10:00:00Z",
          "assets": [
            {
              "name": "llama-b9355-bin-win-cpu-x64.zip",
              "browser_download_url": "https://example.com/win-cpu.zip",
              "size": 1024
            }
          ]
        }
        """;
        using var handler = new CapturingHttpHandler(request =>
            request.RequestUri?.ToString() == RuntimePackageSourceCatalog.LatestReleaseApiUrl
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(releaseJson) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var preset = RuntimePackageSourceCatalog.PresetRows().Single(candidate => candidate.Id == "official-prebuilt-windows-cpu");
        var runtimeFolder = Path.Combine(settings.RuntimeRoot, "official-prebuilt-windows-cpu-b9354");
        Directory.CreateDirectory(runtimeFolder);
        await File.WriteAllTextAsync(Path.Combine(runtimeFolder, "llama-server.exe"), "fake server", TestContext.Current.CancellationToken);
        var runtime = new RuntimeRecord(
            "runtime-package",
            "Official llama.cpp CPU Windows",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            System.Text.Json.JsonSerializer.Serialize(new
            {
                folder = runtimeFolder,
                runtimeMetadata = new
                {
                    managedPackageId = preset.Id,
                    managedPresetId = preset.Id,
                    releaseTag = "b9354"
                }
            }),
            DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);

        var sessions = CreateLoadedModelSessionManager();
        var launchProfiles = new ModelLaunchProfileService(store, sessions);
        var status = new RuntimePackageStatusService();
        var jobs = new RuntimePackageJobService(new JobEngine(store, Path.Combine(root, "logs")));
        var service = new RuntimePackageApplicationService(
            store,
            status,
            new RuntimePackageCheckWorkflowService(jobs, new RuntimePackageUpdateCheckService(http, status)),
            new RuntimePackageInstallWorkflowService(
                new RuntimePackageInstallService(http, new RuntimeRegistryService(store)),
                jobs,
                new RuntimePackageWslFileService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", "")), () => "wsl.exe")),
            new RuntimeDeletionPlanner(store, launchProfiles, sessions),
            new RuntimeDeletionExecutorService(store),
            new RuntimeBuildPrerequisiteService(new RuntimeToolPrerequisiteService(
                _ => throw new InvalidOperationException("WSL readiness is not expected for native packages."),
                () => throw new InvalidOperationException("Windows build tools are not expected for package installs."),
                new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", "")))));
        var sessionState = new RuntimeCatalogSessionState();
        var row = status.CreateRow(
            preset,
            status.BuildInventory(preset, await store.ListRuntimesAsync(), sessionState.RuntimePackageUpdateStates));
        var statuses = new List<string>();
        var busyMessages = new List<string>();
        var infoMessages = new List<string>();
        var confirmations = new List<RuntimePackageDeleteConfirmation>();
        var packageGridRefreshes = 0;
        var runtimeRefreshes = 0;
        var overviewRefreshes = 0;
        var jobRefreshes = 0;
        var yields = 0;
        var confirmDelete = false;
        RuntimePackageApplicationActions Actions() => new(
            async (message, action) =>
            {
                busyMessages.Add(message);
                await action();
            },
            () =>
            {
                runtimeRefreshes++;
                return Task.CompletedTask;
            },
            () =>
            {
                overviewRefreshes++;
                return Task.CompletedTask;
            },
            () =>
            {
                jobRefreshes++;
                return Task.CompletedTask;
            },
            () =>
            {
                yields++;
                return Task.CompletedTask;
            },
            () => packageGridRefreshes++,
            statuses.Add,
            (title, message) => infoMessages.Add($"{title}: {message}"),
            confirmation =>
            {
                confirmations.Add(confirmation);
                return confirmDelete;
            });

        var blockedInstall = await service.InstallAsync(preset, settings, sessionState, BoundedLogFile.MegabytesToBytes(1), Actions());
        var check = await service.CheckUpdateAsync(preset, row, settings, sessionState, BoundedLogFile.MegabytesToBytes(1), Actions());
        Assert.True(sessionState.RuntimePackageUpdateStates.TryGetValue(preset.Id, out var packageUpdate));
        var cancelledDelete = await service.DeleteBuildsAsync(preset, settings, sessionState, Actions());
        var afterCancelledDelete = await store.ListRuntimesAsync();
        confirmDelete = true;
        var appliedDelete = await service.DeleteBuildsAsync(preset, settings, sessionState, Actions());
        var afterAppliedDelete = await store.ListRuntimesAsync();

        Assert.Equal(RuntimePackageApplicationOutcome.Blocked, blockedInstall);
        Assert.Contains("already installed", statuses[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(RuntimePackageApplicationOutcome.Applied, check);
        Assert.Equal("b9355", packageUpdate.LatestTag);
        Assert.Equal("Update", row.InstallAction);
        Assert.True(row.CanInstall);
        Assert.Contains(infoMessages, message => message.Contains("Runtime download check", StringComparison.Ordinal));
        Assert.Equal(RuntimePackageApplicationOutcome.Cancelled, cancelledDelete);
        Assert.Contains(afterCancelledDelete, candidate => candidate.Id == runtime.Id);
        Assert.Equal(RuntimePackageApplicationOutcome.Applied, appliedDelete);
        Assert.DoesNotContain(afterAppliedDelete, candidate => candidate.Id == runtime.Id);
        Assert.Empty(sessionState.RuntimePackageUpdateStates);
        Assert.Equal(["Checking Official llama.cpp CPU Windows release...", "Deleting runtime downloads..."], busyMessages);
        Assert.Equal(2, confirmations.Count);
        Assert.Contains("Installed runtimes: 1", confirmations[0].Message, StringComparison.Ordinal);
        Assert.Equal(2, packageGridRefreshes);
        Assert.Equal(3, runtimeRefreshes);
        Assert.Equal(1, overviewRefreshes);
        Assert.True(jobRefreshes >= 2);
        Assert.Equal(1, yields);
    }


}
