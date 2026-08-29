using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeSourceJobsTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task RuntimeBuildJobServiceBuildsPayloadRedactsUrlsAndStampsMetadata()
    {
        var root = CreateTempRoot();
        var installDir = Path.Combine(root, "runtime");
        Directory.CreateDirectory(installDir);
        await File.WriteAllTextAsync(Path.Combine(installDir, "local-llm-runtime.json"), """{"commit":"abc"}""", TestContext.Current.CancellationToken);
        var preset = new RuntimeBuildPreset("custom-cuda", "Custom CUDA", "https://fixture-user:fixture-pass@example.invalid/repo.git", "main", true, Custom: true);

        var sourceDir = Path.Combine(root, "source");
        var payload = System.Text.Json.Nodes.JsonNode.Parse(RuntimeBuildJobService.Payload(preset, "build", installDir, "Queued.", "marker", "Ubuntu", sourceDir))!.AsObject();
        await RuntimeBuildJobService.StampManagedMetadataAsync(installDir, preset, update: true);
        var logPath = Path.Combine(root, "runtime-build.log");
        await RuntimeBuildJobService.AppendJobLogAsync(logPath, JobStatus.Running, "build started", BoundedLogFile.MegabytesToBytes(1));
        await RuntimeBuildJobService.AppendRecoveryLogAsync(logPath, "recovered source", BoundedLogFile.MegabytesToBytes(1));
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(installDir, "local-llm-runtime.json"), TestContext.Current.CancellationToken))!.AsObject();
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);

        Assert.Equal("custom-cuda", payload["preset"]?.ToString());
        Assert.Equal("Custom CUDA", payload["label"]?.ToString());
        Assert.Equal(preset.RepoUrl, payload["repoUrl"]?.ToString());
        Assert.Equal("build", payload["action"]?.ToString());
        Assert.Equal("Ubuntu", payload["wslDistro"]?.ToString());
        Assert.Equal("marker", payload["processMarker"]?.ToString());
        Assert.Equal(sourceDir, payload["sourceDir"]?.ToString());
        Assert.Equal("wsl", payload["mode"]?.ToString());
        Assert.Equal("https://redacted:redacted@example.invalid/repo.git", RuntimeBuildJobService.RedactCommandArgument(preset.RepoUrl));
        Assert.Equal("abc", metadata["commit"]?.ToString());
        Assert.Equal("custom-cuda", metadata["managedPresetId"]?.ToString());
        Assert.Equal("wsl", metadata["managedMode"]?.ToString());
        Assert.Equal("update", metadata["managedAction"]?.ToString());
        Assert.False(string.IsNullOrWhiteSpace(metadata["managedInstalledAt"]?.ToString()));
        Assert.NotNull(metadata["installedFiles"]);
        Assert.Equal("verified", metadata["lastVerificationStatus"]?.ToString());
        Assert.Contains("Running: build started", log, StringComparison.Ordinal);
        Assert.Contains("Recovery: recovered source", log, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeSourceRepositoryServiceDownloadsMetadataAndRedactsGitLog()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var logPath = Path.Combine(root, "logs", "runtime-source.log");
        var preset = new RuntimeBuildPreset(
            "custom-private-cuda",
            "Custom Private CUDA",
            "https://user:secret@example.invalid/repo.git",
            "main",
            true,
            Custom: true,
            Mode: RuntimeMode.Native);
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            if (args.Contains("clone", StringComparer.Ordinal))
            {
                Directory.CreateDirectory(args[^1]);
                Directory.CreateDirectory(Path.Combine(args[^1], ".git"));
                return new ProcessRunResult(0, "cloned", "");
            }

            if (args.Contains("rev-parse", StringComparer.Ordinal) && args.Contains("--short=12", StringComparer.Ordinal))
                return new ProcessRunResult(0, "abcdef123456\r\n", "");

            return new ProcessRunResult(1, "", "unexpected command");
        });
        var service = new RuntimeSourceRepositoryService(runner);

        var result = await service.DownloadAsync(new RuntimeSourceDownloadRequest(preset, settings, logPath, 1024 * 1024, TestContext.Current.CancellationToken));
        var metadata = RuntimeBuildCatalogService.ReadSource(RuntimeBuildCatalogService.SourceMetadataPath(result.Source.SourceDir));
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);
        var cloneArgs = runner.Commands.Single(command => command.Contains("clone", StringComparer.Ordinal));

        Assert.Equal("abcdef123456", result.Source.Commit);
        Assert.Equal(result.Source, metadata);
        Assert.Contains("--branch", cloneArgs);
        Assert.Contains("main", cloneArgs);
        Assert.Contains("https://redacted:redacted@example.invalid/repo.git", log, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", log, StringComparison.Ordinal);
        Assert.Contains("downloaded at abcdef1", result.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task RuntimeSourceWorkflowServiceOwnsDownloadJobLifecycle()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset(
            "custom-source-cpu",
            "Custom Source CPU",
            "https://example.invalid/repo.git",
            "main",
            false,
            Custom: true,
            Mode: RuntimeMode.Native);
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            if (args.Contains("clone", StringComparer.Ordinal))
            {
                Directory.CreateDirectory(args[^1]);
                Directory.CreateDirectory(Path.Combine(args[^1], ".git"));
                return new ProcessRunResult(0, "cloned", "");
            }

            if (args.Contains("rev-parse", StringComparer.Ordinal) && args.Contains("--short=12", StringComparer.Ordinal))
                return new ProcessRunResult(0, "abcdef123456\r\n", "");

            return new ProcessRunResult(1, "", "unexpected command");
        });
        var workflow = new RuntimeSourceWorkflowService(
            new RuntimeSourceRepositoryService(runner),
            new JobEngine(store, Path.Combine(root, "logs")));

        var result = await workflow.DownloadAsync(new RuntimeSourceDownloadWorkflowRequest(
            preset,
            settings,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken));
        var job = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimeBuildJobService.ParsePayload(job.PayloadJson);
        var log = await File.ReadAllTextAsync(job.LogPath, TestContext.Current.CancellationToken);

        Assert.Equal(result.Job.Id, job.Id);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.NotNull(payload);
        Assert.Equal("download", payload.Action);
        Assert.Equal(result.Source.SourceDir, payload.InstallDir);
        Assert.Equal("abcdef123456", result.Source.Commit);
        Assert.Contains("Downloading repository source", log, StringComparison.Ordinal);
        Assert.Contains("downloaded at abcdef1", log, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task RuntimeSourceWorkflowServiceOwnsUpdateCheckJobLifecycle()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var preset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU", "https://example.invalid/repo.git", "master", false, Mode: RuntimeMode.Native);
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            return args.Contains("ls-remote", StringComparer.Ordinal)
                ? new ProcessRunResult(0, "1234567890abcdef\trefs/heads/master\r\n", "")
                : new ProcessRunResult(1, "", "unexpected command");
        });
        var workflow = new RuntimeSourceWorkflowService(
            new RuntimeSourceRepositoryService(runner),
            new JobEngine(store, Path.Combine(root, "logs")));

        var result = await workflow.CheckUpdateAsync(new RuntimeSourceUpdateCheckWorkflowRequest(
            preset,
            new RuntimeSourceVersion("000000000000", Path.Combine(root, "runtime")),
            BoundedLogFile.MegabytesToBytes(1),
            new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero),
            TestContext.Current.CancellationToken));
        var job = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimeBuildJobService.ParsePayload(job.PayloadJson);
        var log = await File.ReadAllTextAsync(job.LogPath, TestContext.Current.CancellationToken);

        Assert.Equal(result.Job.Id, job.Id);
        Assert.Equal(JobStatus.Completed, job.Status);
        Assert.True(result.State.HasUpdate);
        Assert.Equal("1234567890abcdef", result.State.RemoteCommit);
        Assert.NotNull(payload);
        Assert.Equal("check", payload.Action);
        Assert.Contains("Update available", payload.Message, StringComparison.Ordinal);
        Assert.Contains("Checking remote repository", log, StringComparison.Ordinal);
        Assert.Contains("Update available", log, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeSourceApplicationServiceCoordinatesDownloadAndUpdateCheck()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset(
            "custom-app-source-cpu",
            "Custom App Source CPU",
            "https://example.invalid/repo.git",
            "main",
            false,
            Custom: true,
            Mode: RuntimeMode.Native);
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            if (args.Contains("clone", StringComparer.Ordinal))
            {
                Directory.CreateDirectory(args[^1]);
                Directory.CreateDirectory(Path.Combine(args[^1], ".git"));
                return new ProcessRunResult(0, "cloned", "");
            }

            if (args.Contains("rev-parse", StringComparer.Ordinal) && args.Contains("--short=12", StringComparer.Ordinal))
                return new ProcessRunResult(0, "abcdef123456\r\n", "");

            if (args.Contains("ls-remote", StringComparer.Ordinal))
                return new ProcessRunResult(0, "fedcba9876543210\trefs/heads/main\r\n", "");

            return new ProcessRunResult(1, "", "unexpected command");
        });
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var service = new RuntimeSourceApplicationService(
            store,
            new RuntimeCatalogDataService(),
            new RuntimeSourceWorkflowService(new RuntimeSourceRepositoryService(runner), jobs));
        var sessionState = new RuntimeCatalogSessionState();
        var busyMessages = new List<string>();
        var infoMessages = new List<string>();
        var statuses = new List<string>();
        var runtimeRefreshes = 0;
        var overviewRefreshes = 0;
        var gridRefreshes = 0;
        var yields = 0;
        RuntimeSourceApplicationActions Actions() => new(
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
                yields++;
                return Task.CompletedTask;
            },
            () => gridRefreshes++,
            statuses.Add,
            (title, message) => infoMessages.Add($"{title}: {message}"));

        var row = new RuntimeBuildPresetRow { CanCheck = true, CanDownload = false };
        var initialCheck = await service.CheckUpdateAsync(preset, row, settings, sessionState, BoundedLogFile.MegabytesToBytes(1), Actions());
        var downloaded = await service.DownloadAsync(preset, settings, sessionState, BoundedLogFile.MegabytesToBytes(1), Actions());
        var blockedDownload = await service.DownloadAsync(preset, settings, sessionState, BoundedLogFile.MegabytesToBytes(1), Actions());
        var checkedUpdate = await service.CheckUpdateAsync(preset, row, settings, sessionState, BoundedLogFile.MegabytesToBytes(1), Actions());
        var missingPreset = new RuntimeBuildPreset("missing-app-source", "Missing App Source", "https://example.invalid/missing.git", "main", false, Mode: RuntimeMode.Native);
        var missingSourceDir = RuntimeBuildCatalogService.SourceDir(settings.RuntimeRoot, missingPreset);
        Directory.CreateDirectory(missingSourceDir);
        await File.WriteAllTextAsync(
            RuntimeBuildCatalogService.SourceMetadataPath(missingSourceDir),
            System.Text.Json.JsonSerializer.Serialize(new RuntimeSourceEntry(
                missingPreset.Id,
                missingPreset.Label,
                missingPreset.RepoUrl,
                missingPreset.Branch,
                missingPreset.Cuda,
                "missing-source",
                "unknown",
                DateTimeOffset.UtcNow,
                Mode: RuntimeMode.Native)),
            TestContext.Current.CancellationToken);
        var unknownRow = new RuntimeBuildPresetRow { CanCheck = true, CanDownload = true };
        var unknown = await service.CheckUpdateAsync(
            missingPreset,
            unknownRow,
            settings,
            sessionState,
            BoundedLogFile.MegabytesToBytes(1),
            Actions());
        var jobsList = await store.ListJobsAsync();

        Assert.Equal(RuntimeSourceApplicationOutcome.Applied, initialCheck);
        Assert.Equal(RuntimeSourceApplicationOutcome.Applied, downloaded);
        Assert.Equal(RuntimeSourceApplicationOutcome.Blocked, blockedDownload);
        Assert.Equal(RuntimeSourceApplicationOutcome.Applied, checkedUpdate);
        Assert.Equal(RuntimeSourceApplicationOutcome.UnknownLocalVersion, unknown);
        Assert.True(sessionState.RuntimeUpdateStates.TryGetValue(preset.Id, out var state));
        Assert.True(state.HasUpdate);
        Assert.Equal("abcdef123456", state.LocalCommit);
        Assert.Equal("fedcba9876543210", state.RemoteCommit);
        Assert.Equal("Update available", row.LocalStatus);
        Assert.Contains("Update available", row.LatestLocal, StringComparison.Ordinal);
        Assert.Equal("Downloaded", row.DownloadAction);
        Assert.False(row.CanDownload);
        Assert.Equal("Version unknown", unknownRow.LocalStatus);
        Assert.False(unknownRow.CanDownload);
        Assert.Contains("Local runtime version is unknown", Assert.Single(statuses), StringComparison.Ordinal);
        Assert.Contains(infoMessages, message => message.Contains("Download disabled", StringComparison.Ordinal));
        Assert.Contains(infoMessages, message => message.Contains("Runtime update check", StringComparison.Ordinal));
        Assert.Equal(["Checking Custom App Source CPU for updates...", "Downloading Custom App Source CPU...", "Checking Custom App Source CPU for updates..."], busyMessages);
        Assert.Equal(3, jobsList.Count);
        Assert.All(jobsList, job => Assert.Equal(JobStatus.Completed, job.Status));
        Assert.Equal(4, runtimeRefreshes);
        Assert.Equal(1, overviewRefreshes);
        Assert.True(gridRefreshes >= 4);
        Assert.Equal(3, yields);
    }


    [Fact]
    public async Task RuntimeSourceRepositoryServiceRepairsDirtyExistingCheckoutBeforePull()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU", "https://example.invalid/repo.git", "master", false, Mode: RuntimeMode.Native);
        var sourceDir = RuntimeBuildCatalogService.SourceDir(settings.RuntimeRoot, preset);
        Directory.CreateDirectory(Path.Combine(sourceDir, ".git"));
        var statusCalls = 0;
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            if (args.Contains("rev-parse", StringComparer.Ordinal) && args.Contains("--is-inside-work-tree", StringComparer.Ordinal))
                return new ProcessRunResult(0, "true\r\n", "");
            if (args.Contains("status", StringComparer.Ordinal))
                return new ProcessRunResult(0, statusCalls++ == 0 ? " M CMakeLists.txt\r\n" : "", "");
            if (args.Contains("checkout", StringComparer.Ordinal) || args.Contains("fetch", StringComparer.Ordinal) || args.Contains("pull", StringComparer.Ordinal))
                return new ProcessRunResult(0, "", "");
            return new ProcessRunResult(1, "", "unexpected command");
        });
        var service = new RuntimeSourceRepositoryService(runner);

        await service.CloneOrUpdateAsync(new RuntimeSourceRepositoryRequest(
            preset,
            settings.RuntimeRoot,
            sourceDir,
            Path.Combine(root, "runtime-source.log"),
            1024 * 1024,
            TestContext.Current.CancellationToken));

        var commandText = runner.Commands.Select(command => string.Join(" ", command)).ToArray();
        Assert.Contains(commandText, command => command.Contains("checkout --force HEAD", StringComparison.Ordinal));
        Assert.Contains(commandText, command => command.Contains("fetch --all --tags", StringComparison.Ordinal));
        Assert.Contains(commandText, command => command.Contains("checkout master", StringComparison.Ordinal));
        Assert.Contains(commandText, command => command.Contains("pull --ff-only", StringComparison.Ordinal));
    }


    [Fact]
    public async Task RuntimeSourceRepositoryServiceChecksRemoteCommitAndRejectsUnsafeSourceDir()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU", "https://example.invalid/repo.git", "master", false, Mode: RuntimeMode.Native);
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            return args.Contains("ls-remote", StringComparer.Ordinal)
                ? new ProcessRunResult(0, "1234567890abcdef\trefs/heads/master\r\n", "")
                : new ProcessRunResult(1, "", "unexpected command");
        });
        var service = new RuntimeSourceRepositoryService(runner);
        var runtime = new RuntimeRecord(
            "runtime",
            "Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(settings.RuntimeRoot, "runtime", "llama-server.exe"),
            $$"""{"commit":"000000000000","folder":"{{Path.Combine(settings.RuntimeRoot, "runtime").Replace("\\", "\\\\")}}"}""",
            DateTimeOffset.UtcNow);

        var update = await service.CheckUpdateAsync(preset, runtime, TestContext.Current.CancellationToken);
        var unsafeRequest = new RuntimeSourceRepositoryRequest(
            preset,
            settings.RuntimeRoot,
            Path.Combine(root, "outside-source"),
            Path.Combine(root, "runtime-source.log"),
            1024 * 1024,
            TestContext.Current.CancellationToken);

        Assert.True(update.IsInstalled);
        Assert.True(update.HasUpdate);
        Assert.Equal("1234567890abcdef", update.RemoteCommit);
        Assert.Contains("outside", (await Assert.ThrowsAsync<InvalidOperationException>(() => service.CloneOrUpdateAsync(unsafeRequest))).Message, StringComparison.OrdinalIgnoreCase);
    }


}
