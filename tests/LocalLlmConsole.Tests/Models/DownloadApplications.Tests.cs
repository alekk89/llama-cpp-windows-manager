using LocalLlmConsole.Models;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


public sealed class DownloadApplicationsTests : ManagerRegressionTestBase
{


    [Fact]
    public async Task DownloadHistoryWorkflowServiceDeletesHistoryAndSafePartialFiles()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var catalog = new ModelCatalogService(store);
        var huggingFace = new HuggingFaceService(store, jobs, catalog);
        var service = new DownloadHistoryWorkflowService(store, huggingFace);
        var settings = AppSettings.CreateDefault(root);
        var modelDir = Path.Combine(settings.ModelsRoot, "repo-model");
        Directory.CreateDirectory(modelDir);
        var destination = Path.Combine(modelDir, "model.gguf");
        await File.WriteAllTextAsync(destination + ".partial", "partial", TestContext.Current.CancellationToken);
        var file = new HuggingFaceFile("owner/repo", "model.gguf", "model.gguf", "Q4", 1024, 1);
        var payload = new DownloadJobPayload(file, destination, DownloadedBytes: 5, TotalBytes: 10);
        var now = DateTimeOffset.UtcNow;
        var job = new JobRecord(
            "download-1",
            "huggingface-download",
            JobStatus.Paused,
            System.Text.Json.JsonSerializer.Serialize(payload),
            "",
            now,
            now);
        await store.UpsertJobAsync(job);

        var plan = service.BuildDeletePlan(job);
        var result = await service.DeleteAsync(job, settings, TestContext.Current.CancellationToken);

        Assert.Contains("model.gguf", plan.DisplayName, StringComparison.Ordinal);
        Assert.Contains("Completed model files are kept.", plan.ConfirmationMessage, StringComparison.Ordinal);
        Assert.True(result.Deleted);
        Assert.False(result.StopStillInProgress);
        Assert.False(File.Exists(destination + ".partial"));
        Assert.False(Directory.Exists(modelDir));
        Assert.Empty(await store.ListJobsAsync());

        var outsideRoot = Path.Combine(root, "..", $"{Path.GetFileName(root)}-outside-download");
        Directory.CreateDirectory(outsideRoot);
        var outsideDestination = Path.GetFullPath(Path.Combine(outsideRoot, "outside.gguf"));
        await File.WriteAllTextAsync(outsideDestination + ".partial", "must stay", TestContext.Current.CancellationToken);
        var outsideJob = job with
        {
            Id = "download-2",
            Status = JobStatus.Completed,
            PayloadJson = System.Text.Json.JsonSerializer.Serialize(payload with { Destination = outsideDestination })
        };
        await store.UpsertJobAsync(outsideJob);

        var outsideResult = await service.DeleteAsync(outsideJob, settings, TestContext.Current.CancellationToken);

        Assert.True(outsideResult.Deleted);
        Assert.True(File.Exists(outsideDestination + ".partial"));
        Assert.Empty(await store.ListJobsAsync());
    }


    [Fact]
    public async Task DownloadHistoryWorkflowServiceOwnsDownloadCommands()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var downloads = new FakeDownloadOperations();
        var service = new DownloadHistoryWorkflowService(store, downloads);
        var settings = AppSettings.CreateDefault(root);
        var now = DateTimeOffset.UtcNow;
        JobRecord Job(string id, JobStatus status) => new(id, "huggingface-download", status, "{}", "", now, now);

        var runningPlan = service.BuildResumePlan(Job("running", JobStatus.Running));
        var queuedPlan = service.BuildResumePlan(Job("queued", JobStatus.Queued));
        var completedPlan = service.BuildResumePlan(Job("completed", JobStatus.Completed));
        var paused = Job("paused", JobStatus.Paused);
        var resume = await service.ResumeAsync(paused, settings);
        var pause = await service.PauseAsync(paused);
        var stop = await service.StopAsync(paused);

        Assert.False(runningPlan.CanResume);
        Assert.Equal("That download is already active.", runningPlan.StatusMessage);
        Assert.False(queuedPlan.CanResume);
        Assert.False(completedPlan.CanResume);
        Assert.Equal("That download already completed.", completedPlan.StatusMessage);
        Assert.True(resume.Applied);
        Assert.True(resume.StartMonitor);
        Assert.Equal("Download started: paused", resume.StatusMessage);
        Assert.Equal([paused.Id], downloads.ResumedJobIds);
        Assert.True(pause.Applied);
        Assert.False(pause.StartMonitor);
        Assert.Equal("Pause requested: paused", pause.StatusMessage);
        Assert.Equal([paused.Id], downloads.PausedJobIds);
        Assert.True(stop.Applied);
        Assert.Equal("Stop requested: paused", stop.StatusMessage);
        Assert.Equal([paused.Id], downloads.StoppedJobIds);
    }

    [Fact]
    public async Task DownloadHistoryApplicationServiceCoordinatesDownloadCommands()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var downloads = new FakeDownloadOperations();
        var workflow = new DownloadHistoryWorkflowService(store, downloads);
        var service = new DownloadHistoryApplicationService(workflow);
        var settings = AppSettings.CreateDefault(root);
        var now = DateTimeOffset.UtcNow;
        JobRecord Job(string id, JobStatus status) => new(id, "huggingface-download", status, "{}", "", now, now);
        var paused = Job("paused", JobStatus.Paused);
        var completed = Job("completed", JobStatus.Completed);
        var deleteJob = Job("delete", JobStatus.Failed);
        await store.UpsertJobAsync(paused);
        await store.UpsertJobAsync(completed);
        await store.UpsertJobAsync(deleteJob);
        var statuses = new List<string>();
        var busyMessages = new List<string>();
        var monitorIds = new List<string>();
        var showCalls = new List<string>();
        var historyRefreshes = 0;
        var timerRefreshes = 0;
        var timerCompletes = 0;
        DownloadHistoryCommandApplicationActions CommandActions() => new(
            async (message, action) =>
            {
                busyMessages.Add(message);
                await action();
            },
            () =>
            {
                historyRefreshes++;
                return Task.CompletedTask;
            },
            statuses.Add,
            monitorIds.Add);

        var hostVisible = false;
        var shown = await service.ShowAsync(
            paused.Id,
            new DownloadHistoryShowActions(
                () => hostVisible,
                () =>
                {
                    hostVisible = true;
                    showCalls.Add("show-host");
                },
                () => showCalls.Add("configure"),
                () =>
                {
                    showCalls.Add("refresh");
                    return Task.CompletedTask;
                },
                jobId => showCalls.Add($"select:{jobId}"),
                () => showCalls.Add("timer"),
                statuses.Add));
        var skippedTimer = await service.RefreshTimerAsync(new DownloadHistoryTimerRefreshActions(
            () => false,
            () =>
            {
                timerRefreshes++;
                return Task.CompletedTask;
            },
            () => timerCompletes++));
        var appliedTimer = await service.RefreshTimerAsync(new DownloadHistoryTimerRefreshActions(
            () => true,
            () =>
            {
                timerRefreshes++;
                return Task.CompletedTask;
            },
            () => timerCompletes++));
        var listed = await service.ListJobsAsync();
        var noSelection = await service.ResumeAsync(null, settings, CommandActions());
        var blocked = await service.ResumeAsync(completed, settings, CommandActions());
        var resumed = await service.ResumeAsync(paused, settings, CommandActions());
        var pausedResult = await service.PauseAsync(paused, CommandActions());
        var stopped = await service.StopAsync(paused, CommandActions());
        var deleteConfirmations = 0;
        var cancelledDelete = await service.DeleteAsync(
            deleteJob,
            settings,
            new DownloadHistoryDeleteApplicationActions(
                _ =>
                {
                    deleteConfirmations++;
                    return false;
                },
                CommandActions()),
            TestContext.Current.CancellationToken);
        var appliedDelete = await service.DeleteAsync(
            deleteJob,
            settings,
            new DownloadHistoryDeleteApplicationActions(
                plan =>
                {
                    deleteConfirmations++;
                    Assert.Contains("Completed model files are kept.", plan.ConfirmationMessage, StringComparison.Ordinal);
                    return true;
                },
                CommandActions()),
            TestContext.Current.CancellationToken);

        Assert.Contains(listed, job => job.Id == paused.Id);
        Assert.Equal(DownloadHistoryApplicationOutcome.Applied, shown);
        Assert.Equal(DownloadHistoryTimerRefreshOutcome.Skipped, skippedTimer);
        Assert.Equal(DownloadHistoryTimerRefreshOutcome.Applied, appliedTimer);
        Assert.Equal(DownloadHistoryApplicationOutcome.NoSelection, noSelection);
        Assert.Equal(DownloadHistoryApplicationOutcome.Blocked, blocked);
        Assert.Equal(DownloadHistoryApplicationOutcome.Applied, resumed);
        Assert.Equal(DownloadHistoryApplicationOutcome.Applied, pausedResult);
        Assert.Equal(DownloadHistoryApplicationOutcome.Applied, stopped);
        Assert.Equal(DownloadHistoryApplicationOutcome.Cancelled, cancelledDelete);
        Assert.Equal(DownloadHistoryApplicationOutcome.Applied, appliedDelete);
        Assert.Contains("Select a download history row first.", statuses);
        Assert.Contains("That download already completed.", statuses);
        Assert.Contains("Download started: paused", statuses);
        Assert.Contains("Pause requested: paused", statuses);
        Assert.Contains("Stop requested: paused", statuses);
        Assert.Contains("Deleted download history entry delete.", statuses);
        Assert.Contains("Showing download history for the started model download.", statuses);
        Assert.Equal(["show-host", "configure", "refresh", "select:paused", "timer"], showCalls);
        Assert.Equal(["Starting download...", "Pausing download...", "Stopping download...", "Deleting model download..."], busyMessages);
        Assert.Equal([paused.Id], monitorIds);
        Assert.Equal(1, timerRefreshes);
        Assert.Equal(1, timerCompletes);
        Assert.Equal(4, historyRefreshes);
        Assert.Equal(2, deleteConfirmations);
        Assert.Equal([paused.Id], downloads.ResumedJobIds);
        Assert.Equal([paused.Id], downloads.PausedJobIds);
        Assert.Equal([paused.Id], downloads.StoppedJobIds);
        Assert.DoesNotContain(await store.ListJobsAsync(), job => job.Id == deleteJob.Id);
    }


    [Fact]
    public async Task HuggingFaceSearchApplicationServiceCoordinatesSearchAndInstalledState()
    {
        var root = CreateTempRoot();
        var service = new HuggingFaceSearchApplicationService();
        var settings = AppSettings.CreateDefault(root);
        var file = new HuggingFaceFile("owner/repo", "model-q4.gguf", "model-q4.gguf", "Q4_K_M", 1024, 25);
        var inventory = HuggingFaceInstallStateService.BuildInventory([]);
        var calls = new List<string>();

        var outcome = await service.SearchAsync(
            "qwen",
            settings,
            new HuggingFaceSearchApplicationActions(
                async (message, action) =>
                {
                    calls.Add($"busy:{message}");
                    await action();
                },
                () => calls.Add("grid"),
                () =>
                {
                    calls.Add("inventory");
                    return Task.FromResult(inventory);
                },
                query =>
                {
                    calls.Add($"search:{query}");
                    return Task.FromResult<IReadOnlyList<HuggingFaceFile>>([file]);
                },
                (results, installed, modelsRoot) =>
                {
                    calls.Add($"apply:{results.Single().Name}:{ReferenceEquals(installed, inventory)}:{modelsRoot}");
                }));

        Assert.Equal(HuggingFaceSearchApplicationOutcome.Searched, outcome);
        Assert.Equal([
            "busy:Searching Hugging Face...",
            "grid",
            "inventory",
            "search:qwen",
            $"apply:model-q4.gguf:True:{settings.ModelsRoot}"
        ], calls);
    }


    [Fact]
    public async Task HuggingFaceDownloadApplicationServiceCoordinatesStartedDownloadFollowup()
    {
        var root = CreateTempRoot();
        var service = new HuggingFaceDownloadApplicationService();
        var settings = AppSettings.CreateDefault(root);
        var file = new HuggingFaceFile("owner/repo", "model-q4.gguf", "model-q4.gguf", "Q4_K_M", 1024, 25);
        var job = new JobRecord(
            "job-start",
            "huggingface-download",
            JobStatus.Running,
            "{}",
            Path.Combine(root, "logs", "job-start.log"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow);
        var calls = new List<string>();

        var outcome = await service.StartAsync(
            file,
            settings,
            new HuggingFaceDownloadApplicationActions(
                async (message, action) =>
                {
                    calls.Add($"busy:{message}");
                    await action();
                },
                (downloadFile, downloadSettings) =>
                {
                    calls.Add($"start:{downloadFile.Name}:{downloadSettings.ModelsRoot}");
                    return Task.FromResult(job);
                },
                () =>
                {
                    calls.Add("refresh-overview");
                    return Task.CompletedTask;
                },
                jobId =>
                {
                    calls.Add($"history:{jobId}");
                    return Task.CompletedTask;
                },
                jobId => calls.Add($"monitor:{jobId}"),
                status => calls.Add($"status:{status}")));

        Assert.Equal(HuggingFaceDownloadApplicationOutcome.Started, outcome);
        Assert.Equal([
            "busy:Starting download...",
            $"start:model-q4.gguf:{settings.ModelsRoot}",
            "refresh-overview",
            "history:job-start",
            "monitor:job-start",
            "status:Download started: model-q4.gguf (job-start)"
        ], calls);
    }


    [Fact]
    public async Task DownloadHistoryWorkflowServiceOwnsMonitorCompletionPolling()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var downloads = new FakeDownloadOperations();
        var service = new DownloadHistoryWorkflowService(store, downloads);
        var settings = AppSettings.CreateDefault(root);
        var now = DateTimeOffset.UtcNow;
        var active = new JobRecord("active", "huggingface-download", JobStatus.Running, "{}", "", now, now);
        var inactive = active with { Id = "inactive" };
        await store.UpsertJobAsync(active);
        await store.UpsertJobAsync(inactive);
        await downloads.ResumeDownloadAsync(active, settings);

        await service.WaitUntilInactiveOrTerminalAsync(inactive.Id, TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken);

        var waitTask = service.WaitUntilInactiveOrTerminalAsync(active.Id, TimeSpan.FromMilliseconds(5), TestContext.Current.CancellationToken);
        await Task.Delay(30, TestContext.Current.CancellationToken);
        Assert.False(waitTask.IsCompleted);

        await store.UpsertJobAsync(active with { Status = JobStatus.Completed });
        await waitTask.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
    }


}
