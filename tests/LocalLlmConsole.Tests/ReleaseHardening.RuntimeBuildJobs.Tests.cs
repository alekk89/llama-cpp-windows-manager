using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
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
        var notifications = 0;

        var result = await workflow.DownloadAsync(new RuntimeSourceDownloadWorkflowRequest(
            preset,
            settings,
            BoundedLogFile.MegabytesToBytes(1),
            () =>
            {
                notifications++;
                return Task.CompletedTask;
            },
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
        Assert.True(notifications >= 3);
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
        var notifications = 0;

        var result = await workflow.CheckUpdateAsync(new RuntimeSourceUpdateCheckWorkflowRequest(
            preset,
            new RuntimeSourceVersion("000000000000", Path.Combine(root, "runtime")),
            BoundedLogFile.MegabytesToBytes(1),
            () =>
            {
                notifications++;
                return Task.CompletedTask;
            },
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
        Assert.True(notifications >= 3);
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
        var jobRefreshes = 0;
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
                jobRefreshes++;
                return Task.CompletedTask;
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
        Assert.True(jobRefreshes >= 6);
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


    [Fact]
    public void RuntimeBuildJobServiceCreatesDeterministicBuildPlan()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "master", false);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source"), "abcdef1234567890", DateTimeOffset.UtcNow);

        var plan = RuntimeBuildJobService.CreatePlan(preset, update: false, source, settings, new DateTimeOffset(2026, 5, 26, 12, 34, 56, TimeSpan.Zero), "marker");

        Assert.Equal("build", plan.Action);
        Assert.Equal(source.SourceDir, plan.SourceDir);
        Assert.Equal(Path.Combine(settings.CacheRoot, "runtime-builds", "official-cpu-20260526-123456"), plan.BuildDir);
        Assert.Equal(Path.Combine(settings.RuntimeRoot, "official-cpu-20260526-123456"), plan.InstallDir);
        Assert.Equal("marker", plan.ProcessMarker);
        Assert.Contains("abcdef1", plan.QueuedMessage, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeBuildJobServiceParsesPayloadAndExposesJobControls()
    {
        var root = CreateTempRoot();
        var preset = new RuntimeBuildPreset("official-cuda", "Official CUDA", "https://example.com/llama.cpp.git", "master", true);
        var sourceDir = Path.Combine(root, "runtime-source");
        var payloadJson = RuntimeBuildJobService.Payload(preset, "build", Path.Combine(root, "runtime"), "Building", "marker", "Ubuntu-24.04", sourceDir);
        var now = DateTimeOffset.UtcNow;
        var running = new JobRecord("job-1", "runtime-build", JobStatus.Running, payloadJson, Path.Combine(root, "logs", "job-1.log"), now, now);
        var failed = running with { Id = "job-2", Status = JobStatus.Failed };
        var completed = running with { Id = "job-3", Status = JobStatus.Completed };
        var completedDownload = running with { Id = "job-4", Kind = "runtime-source-download", Status = JobStatus.Completed };
        Directory.CreateDirectory(Path.GetDirectoryName(running.LogPath)!);
        File.WriteAllText(running.LogPath, "[2026-05-26T12:00:00Z] Running: Building\n[ 42%] Building CXX object llama.cpp\n");

        var payload = RuntimeBuildJobService.ParsePayload(payloadJson);
        var vm = new JobsViewModel();
        vm.ReplaceJobs([running, failed, completed]);

        Assert.NotNull(payload);
        Assert.Equal("official-cuda", payload.Preset.Id);
        Assert.Equal("Official CUDA", payload.Preset.Label);
        Assert.True(payload.Preset.Cuda);
        Assert.Equal("build", payload.Action);
        Assert.Equal("marker", payload.ProcessMarker);
        Assert.Equal("Ubuntu-24.04", payload.WslDistro);
        Assert.Equal(sourceDir, payload.SourceDir);
        Assert.Equal(RuntimeMode.Wsl, payload.Mode);
        Assert.True(RuntimeBuildJobService.CanCancel(running));
        Assert.False(RuntimeBuildJobService.CanRetry(running));
        Assert.False(RuntimeBuildJobService.CanClear(running));
        Assert.Contains("Building CXX object", vm.RuntimeRows[0].C5, StringComparison.Ordinal);
        Assert.False(RuntimeBuildJobService.CanCancel(failed));
        Assert.True(RuntimeBuildJobService.CanRetry(failed));
        Assert.True(RuntimeBuildJobService.CanClear(failed));
        Assert.False(RuntimeBuildJobService.CanCancel(completed));
        Assert.False(RuntimeBuildJobService.CanRetry(completed));
        Assert.True(RuntimeBuildJobService.CanClear(completed));
        Assert.False(RuntimeBuildJobService.CanCancel(completedDownload));
        Assert.False(RuntimeBuildJobService.CanRetry(completedDownload));
        Assert.True(RuntimeBuildJobService.CanClear(completedDownload));
        Assert.Equal(3, vm.RuntimeRows.Count);
        Assert.True(vm.RuntimeRows[0].B2);
        Assert.False(vm.RuntimeRows[0].B3);
        Assert.False(vm.RuntimeRows[0].B4);
        Assert.False(vm.RuntimeRows[1].B2);
        Assert.True(vm.RuntimeRows[1].B3);
        Assert.True(vm.RuntimeRows[1].B4);
        Assert.Equal("Clear", vm.RuntimeRows[2].C9);
        Assert.True(vm.RuntimeRows[2].B4);
    }


    [Fact]
    public async Task RuntimeBuildJobControlServiceCancelsRetriesAndClearsRuntimeJobs()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""));
        var markers = new RuntimeBuildMarkerService(runner);
        var cancellations = new RuntimeBuildCancellationRegistry();
        var controls = new RuntimeBuildJobControlService(store, jobs, markers, cancellations, root);
        var preset = new RuntimeBuildPreset("official-cuda", "Official CUDA", "https://example.com/llama.cpp.git", "master", true);
        var sourceDir = Path.Combine(root, "runtime-source");
        Directory.CreateDirectory(sourceDir);
        var payloadJson = RuntimeBuildJobService.Payload(preset, "build", Path.Combine(root, "runtime"), "Building", "marker", "Ubuntu-24.04", sourceDir);
        var job = await jobs.CreateAsync("runtime-build", payloadJson, TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Running, payloadJson, TestContext.Current.CancellationToken);
        var cancellation = controls.RegisterCancellation(job.Id);

        var cancel = await controls.CancelAsync(
            job with { Status = JobStatus.Running },
            "Ubuntu-default",
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken);
        var cancellationRequested = cancellation.IsCancellationRequested;
        var cancelledJob = Assert.Single(await store.ListJobsAsync());
        var retry = controls.PlanRetry(cancelledJob);
        var clear = await controls.ClearAsync(cancelledJob, TestContext.Current.CancellationToken);
        controls.UnregisterCancellation(job.Id, cancellation);
        var logExistsAfterClear = File.Exists(cancelledJob.LogPath);

        Assert.True(cancel.Success);
        Assert.True(cancellationRequested);
        Assert.Contains("Cancel requested", cancel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(JobStatus.Cancelled, cancelledJob.Status);
        Assert.Contains("Cancel requested by user", RuntimeBuildJobService.ParsePayload(cancelledJob.PayloadJson)?.Message, StringComparison.Ordinal);
        Assert.Contains(runner.Commands, command => command.Contains("Ubuntu-24.04"));
        Assert.True(retry.CanRetry);
        Assert.Equal(preset.Id, retry.Preset?.Id);
        Assert.False(retry.Update);
        Assert.Equal(sourceDir, retry.Source?.SourceDir);
        Assert.True(clear.Success);
        Assert.Contains("Cleared runtime job", clear.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(await store.ListJobsAsync());
        Assert.False(logExistsAfterClear);
        Assert.Equal(0, cancellations.ActiveCount);
    }


    [Fact]
    public async Task RuntimeBuildJobApplicationServiceCoordinatesCancelRetryAndClear()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""));
        var markers = new RuntimeBuildMarkerService(runner);
        var cancellations = new RuntimeBuildCancellationRegistry();
        var controls = new RuntimeBuildJobControlService(store, jobs, markers, cancellations, root);
        var application = new RuntimeBuildJobApplicationService(controls);
        var preset = new RuntimeBuildPreset("app-job-cuda", "App Job CUDA", "https://example.com/llama.cpp.git", "master", true);
        var sourceDir = Path.Combine(root, "runtime-source");
        Directory.CreateDirectory(sourceDir);
        var payloadJson = RuntimeBuildJobService.Payload(preset, "build", Path.Combine(root, "runtime"), "Building", "marker", "Ubuntu-24.04", sourceDir);
        var job = await jobs.CreateAsync("runtime-build", payloadJson, TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Running, payloadJson, TestContext.Current.CancellationToken);
        var cancellation = controls.RegisterCancellation(job.Id);
        var confirmations = new List<RuntimeBuildJobClearConfirmation>();
        var retries = new List<RuntimeBuildJobRetryPlan>();
        var busyMessages = new List<string>();
        var statuses = new List<string>();
        var refreshes = 0;
        var allowClear = false;
        RuntimeBuildJobApplicationActions Actions() => new(
            confirmation =>
            {
                confirmations.Add(confirmation);
                return allowClear;
            },
            async (message, action) =>
            {
                busyMessages.Add(message);
                await action();
            },
            () =>
            {
                refreshes++;
                return Task.CompletedTask;
            },
            retry =>
            {
                retries.Add(retry);
                return Task.CompletedTask;
            },
            statuses.Add);

        var invalidClear = await application.ClearAsync(job with { Status = JobStatus.Running }, Actions());
        var cancelled = await application.CancelAsync(job with { Status = JobStatus.Running }, settings, BoundedLogFile.MegabytesToBytes(1), Actions());
        var cancellationRequested = cancellation.IsCancellationRequested;
        var cancelledJob = Assert.Single(await store.ListJobsAsync());
        var retried = await application.RetryAsync(cancelledJob, Actions());
        var clearCancelled = await application.ClearAsync(cancelledJob, Actions());
        allowClear = true;
        var cleared = await application.ClearAsync(cancelledJob, Actions());
        controls.UnregisterCancellation(job.Id, cancellation);

        Assert.Equal(RuntimeBuildJobApplicationOutcome.Blocked, invalidClear);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Applied, cancelled);
        Assert.True(cancellationRequested);
        Assert.Contains("Cancel requested", statuses[1], StringComparison.Ordinal);
        Assert.Equal(JobStatus.Cancelled, cancelledJob.Status);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Applied, retried);
        var retry = Assert.Single(retries);
        Assert.True(retry.CanRetry);
        Assert.Equal(preset.Id, retry.Preset?.Id);
        Assert.Equal(sourceDir, retry.Source?.SourceDir);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Cancelled, clearCancelled);
        Assert.Equal(2, confirmations.Count);
        Assert.Equal("Clear runtime job", confirmations[0].Title);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Applied, cleared);
        Assert.Equal(["Only completed, failed, cancelled, or interrupted runtime jobs can be cleared.", "Cancel requested for App Job CUDA.", $"Cleared runtime job {job.Id}."], statuses);
        Assert.Equal(["Clearing runtime job..."], busyMessages);
        Assert.Equal(2, refreshes);
        Assert.Empty(await store.ListJobsAsync());
        Assert.Equal(0, cancellations.ActiveCount);
    }


    [Fact]
    public void RuntimeBuildRetryPreservesDownloadedSourceContext()
    {
        var source = ReadMainWindowSources();
        var applicationSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeBuildApplicationService.cs"));
        var jobApplicationSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeBuildJobApplicationService.cs"));
        var workflowSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeBuildWorkflowService.cs"));
        var controlsSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeBuildJobControlService.cs"));

        Assert.Contains("var buildJobApplication = RuntimeServices.RuntimeBuildJobApplication;", source, StringComparison.Ordinal);
        Assert.Contains("buildJobApplication.RetryAsync(job, RuntimeBuildJobApplicationActions())", source, StringComparison.Ordinal);
        Assert.Contains("retry => BuildManagedRuntimeAsync(retry.Preset!, retry.Update, retry.Source)", source, StringComparison.Ordinal);
        Assert.Contains("buildApplication.BuildSourceAsync(source, settings, MaxLogBytes(), RuntimeBuildApplicationActions())", source, StringComparison.Ordinal);
        Assert.Contains("var buildApplication = RuntimeServices.RuntimeBuildApplication;", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeBuildApplicationActions()", source, StringComparison.Ordinal);
        Assert.Contains("var retry = _controls.PlanRetry(job)", jobApplicationSource, StringComparison.Ordinal);
        Assert.Contains("actions.RetryBuildAsync(retry)", jobApplicationSource, StringComparison.Ordinal);
        Assert.Contains("ResolveSourcePreset(source, settings.RuntimeRoot)", applicationSource, StringComparison.Ordinal);
        Assert.Contains("var payloadSourceDir = request.Source?.SourceDir ?? \"\";", applicationSource, StringComparison.Ordinal);
        Assert.Contains("RuntimeBuildJobService.Payload(", applicationSource, StringComparison.Ordinal);
        Assert.Contains("_jobControls.RegisterCancellation(job.Id)", applicationSource, StringComparison.Ordinal);
        Assert.Contains("_jobControls.UnregisterCancellation(job.Id, buildCancellation)", applicationSource, StringComparison.Ordinal);
        Assert.Contains("var sourceDir = request.Source?.SourceDir ?? \"\";", workflowSource, StringComparison.Ordinal);
        Assert.Contains("request.WslDistro", workflowSource, StringComparison.Ordinal);
        Assert.Contains("RuntimeSourceFromBuildPayload(payload)", controlsSource, StringComparison.Ordinal);
        Assert.Contains("payload.SourceDir", controlsSource, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildPresets(_settings.RuntimeRoot).FirstOrDefault", source, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeBuildWorkflowServiceCompletesBuildAndPreservesSourceContext()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "master", false, Mode: RuntimeMode.Native);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source"), "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: false, source, settings, new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero), "marker");
        var job = await jobs.CreateAsync("runtime-build", RuntimeBuildJobService.Payload(preset, plan.Action, plan.InstallDir, plan.QueuedMessage, plan.ProcessMarker, settings.WslDistro, source.SourceDir), TestContext.Current.CancellationToken);
        var executed = false;
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(plan.InstallDir, "llama-server.exe"), $$"""{"folder":"{{plan.InstallDir.Replace("\\", "\\\\")}}"}""", DateTimeOffset.UtcNow);
        var workflow = new RuntimeBuildWorkflowService(
            jobs,
            request =>
            {
                executed = true;
                Assert.Equal(source, request.Source);
                Assert.Equal(plan, request.Plan);
                return Task.FromResult(new RuntimeBuildExecutionResult(runtime, "", "Official CPU installed as official-cpu-20260528-100000."));
            },
            (_, _) => throw new InvalidOperationException("Update check should not run for a source build."));
        var notifications = 0;

        var result = await workflow.RunAsync(new RuntimeBuildWorkflowRequest(
            preset,
            settings,
            plan,
            source,
            job,
            Update: false,
            settings.WslDistro,
            BoundedLogFile.MegabytesToBytes(1),
            () =>
            {
                notifications++;
                return Task.CompletedTask;
            },
            TestContext.Current.CancellationToken));
        var stored = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimeBuildJobService.ParsePayload(stored.PayloadJson);
        var log = await File.ReadAllTextAsync(stored.LogPath, TestContext.Current.CancellationToken);

        Assert.True(executed);
        Assert.Equal(RuntimeBuildWorkflowResultKind.Completed, result.Kind);
        Assert.Equal(JobStatus.Completed, stored.Status);
        Assert.NotNull(payload);
        Assert.Equal("build", payload.Action);
        Assert.Equal(source.SourceDir, payload.SourceDir);
        Assert.Equal(settings.WslDistro, payload.WslDistro);
        Assert.Contains("Building downloaded source", log, StringComparison.Ordinal);
        Assert.Contains("installed as official-cpu", log, StringComparison.Ordinal);
        Assert.True(notifications >= 2);
    }


    [Fact]
    public async Task RuntimeBuildWorkflowServiceCompletesNoUpdateWithoutExecutingBuild()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "master", false, Mode: RuntimeMode.Native);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: true, source: null, settings, new DateTimeOffset(2026, 5, 28, 10, 0, 0, TimeSpan.Zero), "marker");
        var job = await jobs.CreateAsync("runtime-build", RuntimeBuildJobService.Payload(preset, plan.Action, plan.InstallDir, plan.QueuedMessage, plan.ProcessMarker, settings.WslDistro), TestContext.Current.CancellationToken);
        var workflow = new RuntimeBuildWorkflowService(
            jobs,
            _ => throw new InvalidOperationException("Build should not execute when remote commit matches."),
            (_, _) => Task.FromResult(new RuntimeSourceUpdateCheck(IsInstalled: true, HasUpdate: false, LocalCommit: "abcdef123456", RemoteCommit: "abcdef123456")));

        var result = await workflow.RunAsync(new RuntimeBuildWorkflowRequest(
            preset,
            settings,
            plan,
            Source: null,
            job,
            Update: true,
            settings.WslDistro,
            BoundedLogFile.MegabytesToBytes(1),
            null,
            TestContext.Current.CancellationToken));
        var stored = Assert.Single(await store.ListJobsAsync());
        var payload = RuntimeBuildJobService.ParsePayload(stored.PayloadJson);
        var log = await File.ReadAllTextAsync(stored.LogPath, TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeBuildWorkflowResultKind.NoUpdate, result.Kind);
        Assert.Equal(JobStatus.Completed, stored.Status);
        Assert.NotNull(payload);
        Assert.Equal("update", payload.Action);
        Assert.Contains("No new build was created", payload.Message, StringComparison.Ordinal);
        Assert.Contains("Checking remote repository", log, StringComparison.Ordinal);
        Assert.Contains("No new build was created", log, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeBuildApplicationServiceCoordinatesBuildAndNoUpdateResults()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""));
        var cancellations = new RuntimeBuildCancellationRegistry();
        var controls = new RuntimeBuildJobControlService(
            store,
            jobs,
            new RuntimeBuildMarkerService(runner),
            cancellations,
            root);
        var prerequisites = new RuntimeBuildPrerequisiteService(new RuntimeToolPrerequisiteService(
            _ => throw new InvalidOperationException("WSL readiness is not expected for native build application tests."),
            () => WindowsBuildTools(),
            runner,
            () => "wsl.exe"));
        var preset = new RuntimeBuildPreset("app-build-cpu", "App Build CPU", "https://example.com/llama.cpp.git", "master", false, Mode: RuntimeMode.Native);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source"), "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        var completedWorkflow = new RuntimeBuildWorkflowService(
            jobs,
            request => Task.FromResult(new RuntimeBuildExecutionResult(
                new RuntimeRecord("runtime-app-build", "App Build Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(request.Plan.InstallDir, "llama-server.exe"), "{}", DateTimeOffset.UtcNow),
                "",
                "App Build CPU installed.")),
            (_, _) => throw new InvalidOperationException("Update check should not run for source builds."));
        var catalogData = new RuntimeCatalogDataService();
        var completedService = new RuntimeBuildApplicationService(jobs, prerequisites, completedWorkflow, controls, catalogData);
        var noUpdateWorkflow = new RuntimeBuildWorkflowService(
            jobs,
            _ => throw new InvalidOperationException("Build should not run when runtime is already current."),
            (_, _) => Task.FromResult(new RuntimeSourceUpdateCheck(IsInstalled: true, HasUpdate: false, LocalCommit: "abcdef123456", RemoteCommit: "abcdef123456")));
        var noUpdateService = new RuntimeBuildApplicationService(jobs, prerequisites, noUpdateWorkflow, controls, catalogData);
        var busyMessages = new List<string>();
        var statuses = new List<string>();
        var infoMessages = new List<string>();
        var jobRefreshes = 0;
        var runtimeRefreshes = 0;
        var overviewRefreshes = 0;
        RuntimeBuildApplicationActions Actions() => new(
            async (message, action) =>
            {
                busyMessages.Add(message);
                await action();
            },
            () =>
            {
                jobRefreshes++;
                return Task.CompletedTask;
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
            statuses.Add,
            (title, message) => infoMessages.Add($"{title}: {message}"));

        var completed = await completedService.BuildSourceAsync(
            source,
            settings,
            BoundedLogFile.MegabytesToBytes(1),
            Actions());
        var noUpdate = await noUpdateService.BuildAsync(
            new RuntimeBuildApplicationRequest(
                preset,
                settings,
                true,
                null,
                BoundedLogFile.MegabytesToBytes(1),
                new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero)),
            Actions());
        var storedJobs = (await store.ListJobsAsync()).OrderBy(job => job.CreatedAt).ToList();
        var completedPayload = RuntimeBuildJobService.ParsePayload(storedJobs[0].PayloadJson);
        var noUpdatePayload = RuntimeBuildJobService.ParsePayload(storedJobs[1].PayloadJson);

        Assert.Equal(RuntimeBuildApplicationOutcome.Completed, completed);
        Assert.Equal(RuntimeBuildApplicationOutcome.NoUpdate, noUpdate);
        Assert.Equal(["Building App Build CPU...", "Updating App Build CPU..."], busyMessages);
        Assert.Equal(["App Build CPU installed."], statuses);
        Assert.Contains(infoMessages, message => message.Contains("Runtime update", StringComparison.Ordinal)
            && message.Contains("No new build was created", StringComparison.Ordinal));
        Assert.Equal(2, storedJobs.Count);
        Assert.All(storedJobs, job => Assert.Equal(JobStatus.Completed, job.Status));
        Assert.NotNull(completedPayload);
        Assert.NotNull(noUpdatePayload);
        Assert.Equal("build", completedPayload.Action);
        Assert.Equal(source.SourceDir, completedPayload.SourceDir);
        Assert.Equal(settings.WslDistro, completedPayload.WslDistro);
        Assert.Equal("update", noUpdatePayload.Action);
        Assert.Equal("", noUpdatePayload.SourceDir);
        Assert.True(jobRefreshes >= 6);
        Assert.Equal(1, runtimeRefreshes);
        Assert.Equal(2, overviewRefreshes);
        Assert.Equal(0, cancellations.ActiveCount);
    }


    [Fact]
    public void RuntimeBuildToolServiceBuildsHiddenPowerShellCommand()
    {
        var preset = new RuntimeBuildPreset("custom-cuda", "Custom CUDA", "https://example.com/repo.git", "feature/runtime", true, Custom: true);

        var psi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            preset,
            RuntimeMode.Wsl,
            "Ubuntu-24.04",
            "marker-1",
            @"C:\Windows\System32\wsl.exe",
            "",
            "",
            noUpdate: true);
        var args = psi.ArgumentList.ToArray();

        Assert.Equal("powershell.exe", psi.FileName);
        Assert.False(psi.UseShellExecute);
        Assert.True(psi.RedirectStandardOutput);
        Assert.Contains("-RepoUrl", args);
        Assert.Contains("https://example.com/repo.git", args);
        Assert.Contains("-Branch", args);
        Assert.Contains("feature/runtime", args);
        Assert.Contains("-WslDistro", args);
        Assert.Contains("Ubuntu-24.04", args);
        Assert.Contains("-ProcessMarker", args);
        Assert.Contains("marker-1", args);
        Assert.Contains("-Cuda", args);
        Assert.Contains("-NoUpdate", args);
        Assert.Contains("-Clean", args);

        var vulkanPreset = new RuntimeBuildPreset("official-vulkan", "Official Vulkan", "https://example.com/repo.git", "master", false, Backend: "vulkan");
        var vulkanPsi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            vulkanPreset,
            RuntimeMode.Wsl,
            "Ubuntu-24.04",
            "marker-2",
            @"C:\Windows\System32\wsl.exe",
            "",
            "",
            noUpdate: false);
        var vulkanArgs = vulkanPsi.ArgumentList.ToArray();

        Assert.Contains("-Vulkan", vulkanArgs);
        Assert.DoesNotContain("-Cuda", vulkanArgs);

        var syclPreset = new RuntimeBuildPreset("official-sycl", "Official SYCL", "https://example.com/repo.git", "master", false, Backend: "sycl");
        var syclPsi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            syclPreset,
            RuntimeMode.Wsl,
            "Ubuntu-24.04",
            "marker-3",
            @"C:\Windows\System32\wsl.exe",
            "",
            "",
            noUpdate: false);
        var syclArgs = syclPsi.ArgumentList.ToArray();

        Assert.Contains("-Sycl", syclArgs);
        Assert.DoesNotContain("-Cuda", syclArgs);
        Assert.DoesNotContain("-Vulkan", syclArgs);

        var nativePreset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU Windows", "https://example.com/repo.git", "master", false, Mode: RuntimeMode.Native);
        var nativePsi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            "powershell.exe",
            @"D:\tools\Build-LlamaCppRuntime.ps1",
            @"D:\cache\source",
            @"D:\cache\build",
            @"D:\runtimes\install",
            nativePreset,
            RuntimeMode.Native,
            "",
            "",
            "",
            @"C:\Program Files\Git\cmd\git.exe",
            @"C:\Program Files\CMake\bin\cmake.exe",
            noUpdate: false);
        var nativeArgs = nativePsi.ArgumentList.ToArray();

        Assert.Contains("-Runtime", nativeArgs);
        Assert.Contains("native", nativeArgs);
        Assert.Contains("-GitExe", nativeArgs);
        Assert.Contains(@"C:\Program Files\Git\cmd\git.exe", nativeArgs);
        Assert.Contains("-CMakeExe", nativeArgs);
        Assert.DoesNotContain("-WslDistro", nativeArgs);
        Assert.DoesNotContain("-WslExe", nativeArgs);
    }


    [Fact]
    public async Task RuntimeBuildExecutionServiceRunsNativeBuildRegistersRuntimeAndDeletesSource()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var runtimes = new RuntimeRegistryService(store);
        var settings = AppSettings.CreateDefault(root) with { DeleteRuntimeSourceAfterSuccessfulBuild = true };
        var preset = new RuntimeBuildPreset("official-windows-cpu", "Official CPU Windows", "https://example.com/repo.git", "master", false, Mode: RuntimeMode.Native);
        var sourceDir = Path.Combine(settings.RuntimeRoot, "runtime-sources", preset.Id);
        Directory.CreateDirectory(sourceDir);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, sourceDir, "abcdef123456", DateTimeOffset.UtcNow, Mode: RuntimeMode.Native);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: false, source, settings, new DateTimeOffset(2026, 5, 28, 10, 11, 12, TimeSpan.Zero), "native-marker");
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            var installDir = args[Array.IndexOf(args, "-InstallDir") + 1];
            Directory.CreateDirectory(installDir);
            File.WriteAllText(Path.Combine(installDir, "llama-server.exe"), "");
            return new ProcessRunResult(0, "native build output", "native build warning");
        });
        var markers = new RuntimeBuildMarkerService(runner);
        var service = new RuntimeBuildExecutionService(root, runner, runtimes, markers);
        var logPath = Path.Combine(root, "logs", "runtime-build.log");

        var result = await service.ExecuteAsync(new RuntimeBuildExecutionRequest(preset, settings, plan, source, logPath, false, TestContext.Current.CancellationToken));
        var registered = Assert.Single(await store.ListRuntimesAsync());
        var metadata = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(plan.InstallDir, "local-llm-runtime.json"), TestContext.Current.CancellationToken))!.AsObject();
        var log = await File.ReadAllTextAsync(logPath, TestContext.Current.CancellationToken);
        var buildArgs = runner.Commands.Single(command => command.Contains("-InstallDir", StringComparer.Ordinal));

        Assert.Equal(registered.Id, result.Runtime.Id);
        Assert.Equal(RuntimeMode.Native, registered.Mode);
        Assert.False(Directory.Exists(sourceDir));
        Assert.Equal("official-windows-cpu", metadata["managedPresetId"]?.ToString());
        Assert.Equal("native build output", log.Split(Environment.NewLine, StringSplitOptions.None)[0]);
        Assert.Contains("Deleted downloaded source", log, StringComparison.Ordinal);
        Assert.Contains("-NoUpdate", buildArgs);
        Assert.Equal(0, markers.ActiveMarkerCount);
        Assert.Contains("Downloaded source deleted", result.StatusMessage, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeBuildExecutionServiceCleansWslMarkerWhenBuildFails()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with { WslDistro = "Ubuntu-24.04" };
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU WSL", "https://example.com/repo.git", "master", false);
        var plan = RuntimeBuildJobService.CreatePlan(preset, update: true, source: null, settings, new DateTimeOffset(2026, 5, 28, 10, 11, 12, TimeSpan.Zero), "wsl-marker");
        var runner = new ScriptedProcessRunner(psi =>
        {
            var args = psi.ArgumentList.ToArray();
            if (args.Contains("-d", StringComparer.Ordinal) && args.Contains("Ubuntu-24.04", StringComparer.Ordinal))
                return new ProcessRunResult(0, "cleanup", "");
            return new ProcessRunResult(1, "", "build failed");
        });
        var markers = new RuntimeBuildMarkerService(runner);
        var service = new RuntimeBuildExecutionService(root, runner, new RuntimeRegistryService(store), markers);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ExecuteAsync(new RuntimeBuildExecutionRequest(preset, settings, plan, null, Path.Combine(root, "logs", "runtime-build.log"), true, TestContext.Current.CancellationToken)));
        var cleanupCommand = runner.Commands.Single(command => command.Contains("-d", StringComparer.Ordinal) && command.Contains("Ubuntu-24.04", StringComparer.Ordinal));

        Assert.Contains("build failed", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, markers.ActiveMarkerCount);
        Assert.Contains("wsl-marker", string.Join(" ", cleanupCommand), StringComparison.Ordinal);
        Assert.Empty(await store.ListRuntimesAsync());
    }


}
