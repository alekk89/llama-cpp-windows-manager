using LocalLlmConsole.Models;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


public sealed class LogsAndCacheTests : ManagerRegressionTestBase
{


    [Fact]
    public async Task AppLogApplicationServiceWritesRedactedBoundedExceptionLogs()
    {
        var root = CreateTempRoot();
        var now = new DateTimeOffset(2026, 5, 31, 10, 20, 30, TimeSpan.Zero);
        var apiKey = new string('e', 32);
        var service = new AppLogApplicationService(root, () => now);
        var exception = new InvalidOperationException($"failure with key {apiKey}");

        await service.WriteExceptionAsync(
            exception,
            apiKey,
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken);

        var path = Path.Combine(root, "logs", "app-20260531.log");
        var text = await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken);

        Assert.Contains("2026-05-31T10:20:30.0000000+00:00", text, StringComparison.Ordinal);
        Assert.Contains("ERROR InvalidOperationException", text, StringComparison.Ordinal);
        Assert.Contains("[redacted]", text, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, text, StringComparison.Ordinal);
    }


    [Fact]
    public async Task CacheClearWorkflowServicePlansAndClearsSafeCache()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = "abcdefghijklmnopqrstuvwxyz123456" };
        Directory.CreateDirectory(settings.CacheRoot);
        var service = new CacheClearWorkflowService(root, store);

        var empty = await service.PlanAsync(settings, hasActiveDownloads: false, TestContext.Current.CancellationToken);
        Assert.Equal(CacheClearPlanStatus.Empty, empty.Status);

        await File.WriteAllTextAsync(Path.Combine(settings.CacheRoot, "cache.bin"), "cached", TestContext.Current.CancellationToken);
        var ready = await service.PlanAsync(settings, hasActiveDownloads: false, TestContext.Current.CancellationToken);
        Assert.Equal(CacheClearPlanStatus.Ready, ready.Status);
        Assert.True(ready.SizeBytes > 0);
        Assert.Contains(ready.DisplaySize, ready.Message, StringComparison.Ordinal);

        await service.ClearAsync(settings, TestContext.Current.CancellationToken);
        Assert.Empty(Directory.EnumerateFileSystemEntries(settings.CacheRoot));

        var now = DateTimeOffset.UtcNow;
        await store.UpsertJobAsync(new JobRecord("job-1", "runtime-build", JobStatus.Running, "{}", "", now, now));
        var busy = await service.PlanAsync(settings, hasActiveDownloads: false, TestContext.Current.CancellationToken);
        Assert.Equal(CacheClearPlanStatus.Busy, busy.Status);

        var unsafeSettings = settings with
        {
            CacheRoot = Path.Combine(Directory.GetParent(root)!.FullName, $"{Path.GetFileName(root)}-outside-cache")
        };
        var unsafeRoot = await service.PlanAsync(unsafeSettings, hasActiveDownloads: false, TestContext.Current.CancellationToken);
        Assert.Equal(CacheClearPlanStatus.UnsafeRoot, unsafeRoot.Status);
    }

    [Fact]
    public async Task CacheClearApplicationServiceOwnsPlanPromptsAndExecution()
    {
        var service = new CacheClearApplicationService();
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var calls = new List<string>();
        var hasActiveDownloads = false;
        var settingsVisible = true;

        var unsafeRoot = await service.ClearAsync(
            settings,
            Actions(new CacheClearPlan(CacheClearPlanStatus.UnsafeRoot, 0, "", "unsafe"), confirmResult: true),
            TestContext.Current.CancellationToken);
        var busy = await service.ClearAsync(
            settings,
            Actions(new CacheClearPlan(CacheClearPlanStatus.Busy, 0, "", "busy"), confirmResult: true),
            TestContext.Current.CancellationToken);
        var empty = await service.ClearAsync(
            settings,
            Actions(new CacheClearPlan(CacheClearPlanStatus.Empty, 0, "0 B", "empty"), confirmResult: true),
            TestContext.Current.CancellationToken);
        var declined = await service.ClearAsync(
            settings,
            Actions(new CacheClearPlan(CacheClearPlanStatus.Ready, 1024, "1.0 KB", "ready"), confirmResult: false),
            TestContext.Current.CancellationToken);
        var cleared = await service.ClearAsync(
            settings,
            Actions(new CacheClearPlan(CacheClearPlanStatus.Ready, 1024, "1.0 KB", "ready"), confirmResult: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(CacheClearApplicationOutcome.UnsafeRoot, unsafeRoot);
        Assert.Equal(CacheClearApplicationOutcome.Busy, busy);
        Assert.Equal(CacheClearApplicationOutcome.Empty, empty);
        Assert.Equal(CacheClearApplicationOutcome.Declined, declined);
        Assert.Equal(CacheClearApplicationOutcome.Cleared, cleared);
        Assert.Contains("notify:Clear cache:Warning:unsafe", calls);
        Assert.Contains("notify:Clear cache:Information:busy", calls);
        Assert.Contains("notify:Clear cache:Information:empty", calls);
        Assert.Contains("show-settings", calls);
        Assert.Contains("confirm:Clear cache:Warning:ready", calls);
        Assert.Contains("busy:Clearing cache...", calls);
        Assert.Contains("clear", calls);
        Assert.Contains("status:Cleared cache (1.0 KB).", calls);

        CacheClearApplicationActions Actions(CacheClearPlan plan, bool confirmResult)
            => new(
                (appSettings, activeDownloads, _) =>
                {
                    calls.Add($"plan:{appSettings.CacheRoot}:{activeDownloads}");
                    return Task.FromResult(plan);
                },
                (_, _) =>
                {
                    calls.Add("clear");
                    return Task.CompletedTask;
                },
                () => hasActiveDownloads,
                () => settingsVisible,
                () => calls.Add("show-settings"),
                prompt => calls.Add($"notify:{prompt.Title}:{prompt.Kind}:{prompt.Message}"),
                prompt =>
                {
                    calls.Add($"confirm:{prompt.Title}:{prompt.Kind}:{prompt.Message}");
                    return confirmResult;
                },
                async (message, action) =>
                {
                    calls.Add($"busy:{message}");
                    await action();
                },
                status => calls.Add($"status:{status}"));
    }


    [Fact]
    public async Task LogPageWorkflowServiceLoadsPreviewsValidatesAndDeletesLogs()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var service = new LogPageWorkflowService(root, store);
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = "abcdefghijklmnopqrstuvwxyz123456" };
        Directory.CreateDirectory(service.LogRoot);
        var appLog = Path.Combine(service.LogRoot, "app.log");
        var runtimeLog = Path.Combine(service.LogRoot, "runtime.log");
        await File.WriteAllTextAsync(appLog, $"hello {settings.ModelApiKey}", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(runtimeLog, "runtime", TestContext.Current.CancellationToken);
        var now = DateTimeOffset.UtcNow;
        await store.UpsertJobAsync(new JobRecord("job-logs", "runtime-build", JobStatus.Completed, "{}", runtimeLog, now, now));
        var activeSession = new LoadedModelSessionSnapshot(
            "session-1",
            "model-1",
            "Qwen",
            "runtime-1",
            "Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            runtimeLog,
            now,
            "",
            0,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true);

        var refresh = await service.LoadAsync(activeSession, TestContext.Current.CancellationToken);
        var preview = await service.BuildPreviewAsync(new LogPreviewRequest(
            appLog,
            "App",
            Path.GetFileName(appLog),
            "app",
            "now",
            "small",
            settings.ModelApiKey,
            HasRows: true), TestContext.Current.CancellationToken);
        var deletionPlan = service.BuildDeletionPlan([appLog, runtimeLog], [activeSession]);
        var singleDelete = service.BuildSingleDeletionCommand(appLog, [activeSession]);
        var activeRuntimeDelete = service.BuildSingleDeletionCommand(runtimeLog, [activeSession]);
        var emptySelectionDelete = service.BuildSelectedDeletionCommand([], [activeSession]);
        var selectedDelete = service.BuildSelectedDeletionCommand([appLog, runtimeLog], [activeSession]);
        var allDelete = await service.BuildAllDeletionCommandAsync([activeSession], TestContext.Current.CancellationToken);
        var deletion = await service.DeleteAsync(selectedDelete, TestContext.Current.CancellationToken);

        Assert.Equal(2, refresh.Files.Count);
        Assert.True(refresh.JobsByLogPath.ContainsKey(LogFileService.NormalizePath(runtimeLog)));
        Assert.Equal(LogFileService.NormalizePath(runtimeLog), refresh.ActiveLogPath);
        Assert.Equal("Qwen", refresh.ActiveModel);
        Assert.Contains("App | app.log", preview, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.ModelApiKey, preview, StringComparison.Ordinal);
        Assert.True(service.TryValidateForOpen(runtimeLog, out var validationError), validationError);
        Assert.True(service.IsActiveRuntimeLog(runtimeLog, [activeSession]));
        Assert.Single(deletionPlan.DeletablePaths);
        Assert.Equal(1, deletion.Skipped);
        Assert.Equal(1, deletion.Deleted);
        Assert.True(singleDelete.CanDelete);
        Assert.Equal("Delete log", singleDelete.ConfirmationTitle);
        Assert.Contains("app.log", singleDelete.ConfirmationMessage, StringComparison.Ordinal);
        Assert.False(activeRuntimeDelete.CanDelete);
        Assert.Contains("Stop the running model", activeRuntimeDelete.StatusMessage, StringComparison.Ordinal);
        Assert.False(emptySelectionDelete.CanDelete);
        Assert.Contains("Select one or more", emptySelectionDelete.StatusMessage, StringComparison.Ordinal);
        Assert.True(selectedDelete.CanDelete);
        Assert.Single(selectedDelete.DeletionPlan.DeletablePaths);
        Assert.Equal("Delete selected logs", selectedDelete.ConfirmationTitle);
        Assert.True(allDelete.CanDelete);
        Assert.Equal("Delete all logs", allDelete.ConfirmationTitle);
        Assert.Contains(service.LogRoot, allDelete.ConfirmationMessage, StringComparison.Ordinal);
        Assert.False(File.Exists(appLog));
        Assert.True(File.Exists(runtimeLog));
    }

    [Fact]
    public async Task LogPageApplicationServiceBuildsPreviewAndCoordinatesDeletion()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var workflow = new LogPageWorkflowService(root, store);
        var service = new LogPageApplicationService(workflow);
        var apiKey = "abcdefghijklmnopqrstuvwxyz123456";
        Directory.CreateDirectory(workflow.LogRoot);
        var appLog = Path.Combine(workflow.LogRoot, "app.log");
        await File.WriteAllTextAsync(appLog, $"hello {apiKey}", TestContext.Current.CancellationToken);
        var row = new LogFileRow
        {
            Type = "App",
            FileName = "app.log",
            Related = "Application",
            Updated = "now",
            Size = "small",
            FullPath = appLog
        };

        var preview = await service.BuildPreviewAsync(
            new LogPreviewApplicationRequest(row, apiKey, HasRows: true),
            TestContext.Current.CancellationToken);
        var missingRow = new LogFileRow
        {
            Type = "App",
            FileName = "missing-preview.log",
            FullPath = Path.Combine(workflow.LogRoot, "missing-preview.log")
        };
        var missingPreview = await service.BuildPreviewAsync(
            new LogPreviewApplicationRequest(missingRow, apiKey, HasRows: true),
            TestContext.Current.CancellationToken);
        var emptyPreview = await service.BuildPreviewAsync(
            new LogPreviewApplicationRequest(null, apiKey, HasRows: false),
            TestContext.Current.CancellationToken);
        var deletionPlan = service.BuildSingleDeletionCommand(appLog, []);
        var openCalls = new List<string>();
        var missingOpen = service.Open(
            Path.Combine(workflow.LogRoot, "missing.log"),
            new LogPageOpenApplicationActions(
                path => openCalls.Add($"open:{path}"),
                status => openCalls.Add($"status:{status}")));
        var opened = service.Open(
            appLog,
            new LogPageOpenApplicationActions(
                path => openCalls.Add($"open:{Path.GetFileName(path)}"),
                status => openCalls.Add($"status:{status}")));

        var cancelledConfirmations = 0;
        var cancelled = await service.DeleteAsync(
            deletionPlan,
            new LogPageDeleteApplicationActions(
                _ =>
                {
                    cancelledConfirmations++;
                    return false;
                },
                (_, _) => throw new InvalidOperationException("Cancelled deletes must not enter the busy runner."),
                () => throw new InvalidOperationException("Cancelled deletes must not clear the preview."),
                () => throw new InvalidOperationException("Cancelled deletes must not refresh."),
                _ => throw new InvalidOperationException("Cancelled deletes must not set status.")),
            TestContext.Current.CancellationToken);

        var statuses = new List<string>();
        var busyMessages = new List<string>();
        var refreshCount = 0;
        var clearedPreview = false;
        var confirmCount = 0;
        var deleted = await service.DeleteAsync(
            deletionPlan,
            new LogPageDeleteApplicationActions(
                _ =>
                {
                    confirmCount++;
                    return true;
                },
                async (message, action) =>
                {
                    busyMessages.Add(message);
                    await action();
                },
                () => clearedPreview = true,
                () =>
                {
                    refreshCount++;
                    return Task.CompletedTask;
                },
                statuses.Add),
            TestContext.Current.CancellationToken);
        var blocked = await service.DeleteAsync(
            LogDeleteCommandPlan.Blocked("Select one or more log files first."),
            new LogPageDeleteApplicationActions(
                _ => throw new InvalidOperationException("Blocked deletes must not prompt."),
                (_, _) => throw new InvalidOperationException("Blocked deletes must not enter the busy runner."),
                () => throw new InvalidOperationException("Blocked deletes must not clear the preview."),
                () => throw new InvalidOperationException("Blocked deletes must not refresh."),
                statuses.Add),
            TestContext.Current.CancellationToken);

        Assert.Contains("App | app.log", preview, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, preview, StringComparison.Ordinal);
        Assert.Equal("Select a log file to view it.", missingPreview);
        Assert.Equal("No app or model logs yet.", emptyPreview);
        Assert.Equal(LogPageOpenApplicationOutcome.Blocked, missingOpen);
        Assert.Equal(LogPageOpenApplicationOutcome.Opened, opened);
        Assert.Contains(openCalls, call => call.Contains("status:That log file is no longer available.", StringComparison.Ordinal));
        Assert.Contains("open:app.log", openCalls);
        Assert.Equal(LogPageDeleteApplicationOutcome.Cancelled, cancelled);
        Assert.Equal(1, cancelledConfirmations);
        Assert.Equal(LogPageDeleteApplicationOutcome.Deleted, deleted);
        Assert.Equal(1, confirmCount);
        Assert.Equal(["Deleting log..."], busyMessages);
        Assert.True(clearedPreview);
        Assert.Equal(1, refreshCount);
        Assert.Contains("Deleted log app.log.", statuses);
        Assert.False(File.Exists(appLog));
        Assert.Equal(LogPageDeleteApplicationOutcome.Blocked, blocked);
        Assert.Contains("Select one or more log files first.", statuses);
    }


}
