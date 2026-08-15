using LocalLlmConsole.Models;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{


    [Fact]
    public void WindowsStartupRegistrationServiceOwnsRunKeyCommands()
    {
        string? command = null;
        var service = new WindowsStartupRegistrationService(
            () => command,
            value => command = value,
            () => command = null,
            () => @"C:\Program Files\LlamaCppWindowsManager\LlamaCppWindowsManager.exe");

        var enabled = service.Apply(startWithWindows: true);
        var reconciled = service.Reconcile(AppSettings.CreateDefault(CreateTempRoot()));
        var enabledCommand = command;
        var disabled = service.Apply(startWithWindows: false);

        Assert.True(enabled.Success);
        Assert.Equal(@"""C:\Program Files\LlamaCppWindowsManager\LlamaCppWindowsManager.exe""", enabledCommand);
        Assert.True(reconciled.StartWithWindows);
        Assert.True(disabled.Success);
        Assert.Null(command);
    }


    [Fact]
    public async Task SettingsRowActionApplicationServiceOwnsRowCommandsAndSecretActions()
    {
        var generatedKey = new string('a', 64);
        var selectedFolder = Path.Combine(CreateTempRoot(), "models");
        var calls = new List<string>();
        var copied = new List<string>();
        string? pickedFolder = null;
        var service = new SettingsRowActionApplicationService(() => generatedKey);

        SettingsRowActionApplicationActions RowActions()
            => new(
                () =>
                {
                    calls.Add("clear-cache");
                    return Task.CompletedTask;
                },
                current =>
                {
                    calls.Add($"pick:{current}");
                    return pickedFolder;
                },
                status => calls.Add($"status:{status}"));

        SettingsSecretCopyApplicationActions CopyActions()
            => new(copied.Add, status => calls.Add($"status:{status}"));

        Assert.Equal(
            SettingsRowActionOutcome.Ignored,
            await service.RunActionAsync(null, RowActions()));
        Assert.Equal(
            SettingsRowActionOutcome.Ignored,
            await service.RunActionAsync(new EditableSettingRow { Key = "noop", Type = "text" }, RowActions()));

        var cacheRow = new EditableSettingRow { Key = "cache", Type = "action" };
        var apiKeyRow = new EditableSettingRow { Key = "modelApiKey", Type = "secret" };
        var folderRow = new EditableSettingRow { Key = "modelsRoot", Type = "folder", Label = "Models", Value = "old" };

        Assert.Equal(SettingsRowActionOutcome.CacheCleared, await service.RunActionAsync(cacheRow, RowActions()));
        Assert.Equal(SettingsRowActionOutcome.ApiKeyGenerated, await service.RunActionAsync(apiKeyRow, RowActions()));
        Assert.Equal(generatedKey, apiKeyRow.Value);
        Assert.Contains("clear-cache", calls);
        Assert.Contains("status:New model API key generated; applying automatically.", calls);

        Assert.Equal(SettingsRowActionOutcome.FolderSelectionCanceled, await service.RunActionAsync(folderRow, RowActions()));
        Assert.Equal("old", folderRow.Value);

        pickedFolder = selectedFolder;

        Assert.Equal(SettingsRowActionOutcome.FolderSelected, await service.RunActionAsync(folderRow, RowActions()));
        Assert.Equal(Path.GetFullPath(selectedFolder), folderRow.Value);
        Assert.Contains("pick:old", calls);
        Assert.Contains("status:Models folder selected; applying automatically.", calls);

        var secretRow = new EditableSettingRow { Type = "secret", Value = "  secret-token  " };

        Assert.Equal(SettingsSecretActionOutcome.Revealed, service.ToggleSecret(secretRow, status => calls.Add($"status:{status}")));
        Assert.True(secretRow.IsSecretVisible);
        Assert.Equal(SettingsSecretActionOutcome.Hidden, service.ToggleSecret(secretRow, status => calls.Add($"status:{status}")));
        Assert.False(secretRow.IsSecretVisible);
        Assert.Equal(SettingsSecretActionOutcome.Ignored, service.ToggleSecret(new EditableSettingRow { Type = "text" }, _ => { }));
        Assert.Equal(SettingsSecretActionOutcome.Copied, service.CopySecret(secretRow, CopyActions()));
        Assert.Equal(["secret-token"], copied);

        secretRow.Value = "";

        Assert.Equal(SettingsSecretActionOutcome.Empty, service.CopySecret(secretRow, CopyActions()));
        Assert.Contains("status:API key is visible in Settings.", calls);
        Assert.Contains("status:API key hidden.", calls);
        Assert.Contains("status:API key copied to clipboard.", calls);
        Assert.Contains("status:No API key is available to copy.", calls);
    }


    [Fact]
    public async Task FolderSettingsApplicationServiceOwnsFolderChangeSequence()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var selectedModelsRoot = Path.Combine(root, "new-models");
        var selectedRuntimeRoot = Path.Combine(root, "new-runtimes");
        var calls = new List<string>();
        var currentPage = "Models";
        string? selectedFolder = null;
        var service = new FolderSettingsApplicationService();

        FolderSettingsApplicationActions Actions()
            => new(
                initial =>
                {
                    calls.Add($"pick:{initial}");
                    return selectedFolder;
                },
                async (message, action) =>
                {
                    calls.Add($"busy:{message}");
                    await action();
                },
                next =>
                {
                    calls.Add($"persist:{next.ModelsRoot}:{next.RuntimeRoot}");
                    return Task.FromResult(next);
                },
                modelsRoot =>
                {
                    calls.Add($"scan-models:{modelsRoot}");
                    return Task.CompletedTask;
                },
                runtimeRoot =>
                {
                    calls.Add($"scan-runtimes:{runtimeRoot}");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("refresh-all");
                    return Task.CompletedTask;
                },
                () => currentPage == "Models",
                () => currentPage == "Runtimes",
                () => currentPage == "Settings",
                () => calls.Add("show-models"),
                () => calls.Add("show-runtimes"),
                () => calls.Add("show-settings"),
                status => calls.Add($"status:{status}"));

        var cancelled = await service.ChooseModelsFolderAsync(settings, scanAfter: true, Actions());

        selectedFolder = selectedModelsRoot;

        var models = await service.ChooseModelsFolderAsync(settings, scanAfter: true, Actions());

        currentPage = "Settings";
        selectedFolder = selectedRuntimeRoot;

        var runtimes = await service.ChooseRuntimeFolderAsync(models.Settings, scanAfter: false, Actions());

        Assert.Equal(FolderSettingsApplicationOutcome.Cancelled, cancelled.Outcome);
        Assert.Same(settings, cancelled.Settings);
        Assert.Equal(FolderSettingsApplicationOutcome.Applied, models.Outcome);
        Assert.Equal(Path.GetFullPath(selectedModelsRoot), models.Settings.ModelsRoot);
        Assert.Equal(FolderSettingsApplicationOutcome.Applied, runtimes.Outcome);
        Assert.Equal(Path.GetFullPath(selectedRuntimeRoot), runtimes.Settings.RuntimeRoot);
        Assert.Contains($"busy:Changing models folder...", calls);
        Assert.Contains($"persist:{Path.GetFullPath(selectedModelsRoot)}:{settings.RuntimeRoot}", calls);
        Assert.Contains($"scan-models:{Path.GetFullPath(selectedModelsRoot)}", calls);
        Assert.Contains("show-models", calls);
        Assert.Contains($"status:Models folder set to {Path.GetFullPath(selectedModelsRoot)}", calls);
        Assert.Contains($"busy:Changing runtimes folder...", calls);
        Assert.DoesNotContain($"scan-runtimes:{Path.GetFullPath(selectedRuntimeRoot)}", calls);
        Assert.Contains("show-settings", calls);
        Assert.Contains($"status:Runtimes folder set to {Path.GetFullPath(selectedRuntimeRoot)}", calls);
        Assert.True(calls.IndexOf($"persist:{Path.GetFullPath(selectedModelsRoot)}:{settings.RuntimeRoot}") < calls.IndexOf($"scan-models:{Path.GetFullPath(selectedModelsRoot)}"));
        Assert.True(calls.IndexOf($"scan-models:{Path.GetFullPath(selectedModelsRoot)}") < calls.IndexOf("refresh-all"));
    }


    [Fact]
    public async Task LifetimeMetricResetApplicationServiceOwnsResetBranches()
    {
        var calls = new List<string>();
        var confirm = false;
        var service = new LifetimeMetricResetApplicationService();
        var total = new UiRow
        {
            C1 = "All models",
            B1 = true,
            Data = new JsonObject { ["Kind"] = "total" }
        };
        var blocked = new UiRow
        {
            C1 = "Blocked",
            B1 = false,
            Data = new JsonObject { ["Kind"] = "model", ["ModelId"] = "blocked", ["ModelName"] = "Blocked" }
        };
        var missingModelId = new UiRow
        {
            C1 = "Missing",
            B1 = true,
            Data = new JsonObject { ["Kind"] = "model", ["ModelName"] = "Missing" }
        };
        var model = new UiRow
        {
            C1 = "Model One",
            B1 = true,
            Data = new JsonObject { ["Kind"] = "model", ["ModelId"] = "model-1", ["ModelName"] = "Model One" }
        };

        LifetimeMetricResetApplicationActions Actions()
            => new(
                confirmation =>
                {
                    calls.Add($"confirm:{confirmation.Title}:{confirmation.Message}");
                    return confirm;
                },
                modelId =>
                {
                    calls.Add($"delete-model:{modelId}");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("delete-all");
                    return Task.CompletedTask;
                },
                () => calls.Add("reset-counters"),
                () =>
                {
                    calls.Add("refresh");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"));

        var ignored = await service.ResetAsync(null, Actions());
        var blockedResult = await service.ResetAsync(blocked, Actions());
        var missingModelIdResult = await service.ResetAsync(missingModelId, Actions());
        var cancelledModel = await service.ResetAsync(model, Actions());

        confirm = true;

        var resetModel = await service.ResetAsync(model, Actions());
        var resetAll = await service.ResetAsync(total, Actions());

        Assert.Equal(LifetimeMetricResetApplicationOutcome.Ignored, ignored);
        Assert.Equal(LifetimeMetricResetApplicationOutcome.Blocked, blockedResult);
        Assert.Equal(LifetimeMetricResetApplicationOutcome.Blocked, missingModelIdResult);
        Assert.Equal(LifetimeMetricResetApplicationOutcome.Cancelled, cancelledModel);
        Assert.Equal(LifetimeMetricResetApplicationOutcome.ResetModel, resetModel);
        Assert.Equal(LifetimeMetricResetApplicationOutcome.ResetAll, resetAll);
        Assert.Contains("status:Only model rows can be reset individually.", calls);
        Assert.Contains(calls, call => call.StartsWith("confirm:Reset lifetime metrics:", StringComparison.Ordinal)
            && call.Contains("Model One", StringComparison.Ordinal));
        Assert.Contains("delete-model:model-1", calls);
        Assert.Contains("status:Lifetime metrics reset for Model One.", calls);
        Assert.Contains("delete-all", calls);
        Assert.Contains("reset-counters", calls);
        Assert.Contains("status:All lifetime metrics reset.", calls);
        Assert.True(calls.IndexOf("delete-model:model-1") < calls.IndexOf("status:Lifetime metrics reset for Model One."));
        Assert.True(calls.IndexOf("delete-all") < calls.IndexOf("reset-counters"));
        Assert.True(calls.IndexOf("reset-counters") < calls.LastIndexOf("refresh"));
    }


    [Fact]
    public async Task LifetimeMetricsApplicationServiceOwnsTokenUsagePersistence()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var service = new LifetimeMetricsApplicationService(store);

        await service.AddUsageAsync(new TokenUsageDelta("empty", "Empty", 0, 0));
        await service.AddUsageAsync(new TokenUsageDelta("model-a", "Model A", 3, 7));
        await service.AddUsageAsync(new TokenUsageDelta("model-b", "Model B", 11, 13));
        await service.DeleteModelUsageAsync("model-a");
        var afterModelDelete = await service.ListAsync();

        await service.DeleteAllUsageAsync();
        var afterAllDelete = await service.ListAsync();

        var row = Assert.Single(afterModelDelete);
        Assert.Equal("model-b", row.ModelId);
        Assert.Equal(11, row.PromptTokens);
        Assert.Equal(13, row.GeneratedTokens);
        Assert.Empty(afterAllDelete);
    }


    [Fact]
    public async Task LoadedLookupApplicationServicesOwnCatalogReads()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.UtcNow;
        var modelPath = Path.Combine(root, "models", "Qwen.gguf");
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var model = new ModelRecord("model-1", "Qwen", modelPath, OwnershipKind.External, "{}", now);
        var runtime = new RuntimeRecord("runtime-1", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", now);
        var job = new JobRecord("job-1", "runtime-build", JobStatus.Completed, "{}", Path.Combine(root, "job.log"), now, now);
        await store.UpsertModelAsync(model);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertJobAsync(job);
        var models = new ModelLookupApplicationService(store);

        var listedModels = await models.ListAsync();
        var foundModel = await models.FindByIdAsync("MODEL-1");
        var missingModel = await models.FindByIdAsync("");
        var displayName = await models.DisplayNameAsync(model.Id);
        var fallbackDisplayName = await models.DisplayNameAsync("missing-model");
        var inventory = await models.BuildHuggingFaceInstallInventoryAsync();
        var listedRuntimes = await store.ListRuntimesAsync();
        var listedJobs = await store.ListJobsAsync();

        Assert.Equal([model.Id], listedModels.Select(item => item.Id).ToArray());
        Assert.Equal(model.Id, foundModel?.Id);
        Assert.Null(missingModel);
        Assert.Equal(model.Name, displayName);
        Assert.Equal("missing-model", fallbackDisplayName);
        Assert.Contains(Path.GetFileName(modelPath), inventory.FileNames);
        Assert.Equal([runtime.Id], listedRuntimes.Select(item => item.Id).ToArray());
        Assert.Equal([job.Id], listedJobs.Select(item => item.Id).ToArray());
    }


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
        var row = new UiRow
        {
            C1 = "App",
            C3 = "Application",
            C4 = "now",
            C5 = "small",
            Data = new JsonObject { ["Path"] = appLog }
        };

        var preview = await service.BuildPreviewAsync(
            new LogPreviewApplicationRequest(row, apiKey, HasRows: true),
            TestContext.Current.CancellationToken);
        var missingRow = new UiRow
        {
            C1 = "App",
            Data = new JsonObject { ["Path"] = Path.Combine(workflow.LogRoot, "missing-preview.log") }
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
        var jobRefreshes = 0;
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
            () =>
            {
                jobRefreshes++;
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
        Assert.Equal(4, jobRefreshes);
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
                    calls.Add("refresh-jobs");
                    return Task.CompletedTask;
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
            "refresh-jobs",
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


    [Fact]
    public void WindowsToolSetupWorkflowServiceBuildsInstallPlans()
    {
        var service = new WindowsToolSetupWorkflowService(
            new VisibleCommandLaunchService(_ => { }),
            () => new WindowsToolSnapshot(false, "", false, "", false, "", false, "", false, "", false, ""));

        var cpu = service.Plan(WindowsToolSetupAction.Cpu);
        var cuda = service.Plan(WindowsToolSetupAction.Cuda);
        var vulkan = service.Plan(WindowsToolSetupAction.Vulkan);
        var sycl = service.Plan(WindowsToolSetupAction.Sycl);

        Assert.Equal("Install Windows CPU tools", cpu.Title);
        Assert.Contains(WindowsSetupCommands.GitWingetId, cpu.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.VisualStudioBuildToolsWingetId, cpu.PowerShellScript, StringComparison.Ordinal);
        Assert.True(cpu.Elevated);
        Assert.Contains("CPU tool setup started", cpu.StartedStatus, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.CudaWingetId, cuda.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains("NVIDIA CUDA Toolkit", cuda.ConfirmationMessage, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.VulkanSdkWingetId, vulkan.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.OneApiBaseToolkitWingetId, sycl.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains("Level Zero GPU", sycl.ConfirmationMessage, StringComparison.Ordinal);
    }


    [Fact]
    public void WindowsToolSetupApplicationServiceOwnsConfirmExecuteAndStatus()
    {
        var plan = new WindowsToolSetupPlan(
            WindowsToolSetupAction.Cpu,
            "Install CPU",
            "Install tools?",
            "Write-Host test",
            Elevated: true,
            "Started CPU tools.");
        var calls = new List<string>();
        var confirm = false;
        var service = new WindowsToolSetupApplicationService(
            action =>
            {
                calls.Add($"plan:{action}");
                return plan;
            },
            executedPlan => calls.Add($"execute:{executedPlan.Action}:{executedPlan.Elevated}"));
        WindowsToolSetupApplicationActions Actions()
            => new(
                confirmation =>
                {
                    calls.Add($"confirm:{confirmation.Title}");
                    return confirm;
                },
                status => calls.Add($"status:{status}"));

        var cancelled = service.Run(WindowsToolSetupAction.Cpu, Actions());

        confirm = true;

        var started = service.Run(WindowsToolSetupAction.Cpu, Actions());

        Assert.Equal(ToolSetupApplicationOutcome.Cancelled, cancelled);
        Assert.Equal(ToolSetupApplicationOutcome.Started, started);
        Assert.Equal([
            "plan:Cpu",
            "confirm:Install CPU",
            "plan:Cpu",
            "confirm:Install CPU",
            "execute:Cpu:True",
            "status:Started CPU tools."
        ], calls);
    }

    [Fact]
    public async Task WindowsToolSetupApplicationServiceOwnsRefreshSequence()
    {
        var snapshot = new WindowsToolSnapshot(
            GitInstalled: true,
            GitPath: "git.exe",
            CMakeInstalled: true,
            CMakePath: "cmake.exe",
            MsvcInstalled: true,
            MsvcDetails: "MSVC ready",
            NvidiaDriverVisible: false,
            NvidiaSmiPath: "",
            CudaToolsInstalled: false,
            CudaDetails: "CUDA missing",
            VulkanToolsInstalled: false,
            VulkanDetails: "Vulkan missing",
            SyclToolsInstalled: false,
            SyclDetails: "oneAPI missing");
        var calls = new List<string>();
        var service = new WindowsToolSetupApplicationService(
            _ => throw new InvalidOperationException("Not used by refresh."),
            _ => throw new InvalidOperationException("Not used by refresh."));

        var result = await service.RefreshAsync(new WindowsToolRefreshApplicationActions(
            async (label, action) =>
            {
                calls.Add($"busy:{label}");
                await action();
            },
            () =>
            {
                calls.Add("detect");
                return Task.FromResult(snapshot);
            },
            tools => calls.Add($"store:{tools.GitPath}"),
            tools => calls.Add($"populate:{tools.CpuToolsInstalled}"),
            status => calls.Add($"status:{status}")));

        Assert.Equal(snapshot, result);
        Assert.Equal([
            "busy:Detecting Windows build tools...",
            "detect",
            "store:git.exe",
            "populate:True",
            "status:Windows CPU build tools ready"
        ], calls);
    }


    [Fact]
    public async Task WindowsToolSetupWorkflowServiceRefreshesDetectedTools()
    {
        var snapshot = new WindowsToolSnapshot(
            GitInstalled: true,
            GitPath: "git.exe",
            CMakeInstalled: true,
            CMakePath: "cmake.exe",
            MsvcInstalled: true,
            MsvcDetails: "MSVC ready",
            NvidiaDriverVisible: true,
            NvidiaSmiPath: "nvidia-smi.exe",
            CudaToolsInstalled: true,
            CudaDetails: "CUDA ready",
            VulkanToolsInstalled: false,
            VulkanDetails: "Vulkan missing",
            SyclToolsInstalled: true,
            SyclDetails: "oneAPI ready");
        var calls = 0;
        var service = new WindowsToolSetupWorkflowService(new VisibleCommandLaunchService(_ => { }), () =>
        {
            calls++;
            return snapshot;
        });

        var detected = await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(snapshot, detected);
        Assert.Equal(1, calls);
        Assert.True(detected.CpuToolsInstalled);
        Assert.True(detected.CudaToolsInstalled);
        Assert.False(detected.VulkanToolsInstalled);
        Assert.True(detected.SyclToolsInstalled);
    }


    [Fact]
    public void ModelLaunchDefaultsUseHighContextGpuAndQ8Cache()
    {
        var root = CreateTempRoot();

        var settings = AppSettings.CreateDefault(root);
        var modelSettings = ModelLaunchSettings.FromAppSettings(settings);
        var applied = modelSettings.ApplyTo(settings with { Port = 9000 });

        Assert.Equal(131_072, settings.ContextSize);
        Assert.Equal(999, settings.GpuLayers);
        Assert.Equal(8081, modelSettings.Port);
        Assert.Equal(8081, applied.Port);
        Assert.Equal(4096, settings.BatchSize);
        Assert.Equal("q8_0", settings.CacheTypeK);
        Assert.Equal("q8_0", settings.CacheTypeV);
        Assert.Equal(0.65, settings.Temperature);
        Assert.Equal(settings.ContextSize, modelSettings.ContextSize);
        Assert.Equal(settings.GpuLayers, modelSettings.GpuLayers);
        Assert.Equal(settings.BatchSize, modelSettings.BatchSize);
        Assert.Equal(settings.CacheTypeK, modelSettings.CacheTypeK);
        Assert.Equal(settings.CacheTypeV, modelSettings.CacheTypeV);
        Assert.Equal(settings.Temperature, modelSettings.Temperature);
        Assert.Equal("none", settings.SpeculativeType);
        Assert.Equal("q8_0", settings.SpecDraftCacheTypeK);
        Assert.Equal("q8_0", settings.SpecDraftCacheTypeV);
        Assert.Equal(-1, settings.Seed);
        Assert.Equal(-1, settings.MaxTokens);
        Assert.Equal(0, settings.VisionImageMinTokens);
        Assert.Equal(0, settings.VisionImageMaxTokens);
        Assert.Equal("", settings.VisionProjectorPath);
        Assert.Equal("", settings.MtpHeadPath);
        Assert.Equal(settings.VisionProjectorPath, modelSettings.VisionProjectorPath);
        Assert.Equal(settings.MtpHeadPath, modelSettings.MtpHeadPath);
        Assert.Equal(settings.VisionImageMinTokens, modelSettings.VisionImageMinTokens);
        Assert.Equal(settings.VisionImageMaxTokens, modelSettings.VisionImageMaxTokens);
        Assert.Equal("auto", settings.RopeScaling);
    }


    [Fact]
    public void ModelSettingsDefaultToSimpleWithRequestedAdvancedGroups()
    {
        var source = ReadMainWindowSources();
        var launchProfileService = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Models", "ModelLaunchProfileService.cs"));
        var advancedSections = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AdvancedSectionStateController.cs"));
        var selectedCapabilities = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Models", "SelectedModelCapabilityController.cs"));
        var controlStateService = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "LaunchSettingsControlStateService.cs"));
        var launchFormBinder = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsFormBinder.cs"));
        var launchPanelFactory = ReadLaunchSettingsPanelFactorySources();
        var launchUiSchema = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingUiSchema.cs"));
        var launchPanelState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "LaunchSettingsPanelState.cs"));
        var advancedState = new AdvancedSectionStateController();

        Assert.False(advancedState.ShowLaunchSettings);
        advancedState.SetLaunchSettings(true);
        Assert.True(advancedState.ShowLaunchSettings);
        Assert.Contains("public bool ShowLaunchSettings { get; private set; }", advancedSections, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.AdvancedSections.ShowLaunchSettings", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.AdvancedSections.SetLaunchSettings(showAdvanced)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_showAdvancedLaunchSettings", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsPanelFactory.Create", source, StringComparison.Ordinal);
        Assert.Contains("private readonly LaunchSettingsPanelState _launchSettingsPanel;", source, StringComparison.Ordinal);
        Assert.Contains("_launchSettingsPanel = uiState.LaunchSettingsPanel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly LaunchSettingsPanelState _launchSettingsPanel = new();", source, StringComparison.Ordinal);
        Assert.Contains("_launchSettingsPanel.Apply(panel);", source, StringComparison.Ordinal);
        Assert.Contains("public LaunchSettingsFormControls FormControls { get; private set; } = new();", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("public string SaveAsNewModelName", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("SetSaveForModelState", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("SaveModelLaunchSettingsButton.Visibility = visible ? Visibility.Visible : Visibility.Collapsed", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("SetSaveForModelState(content, hasNamedProfile && state.CanSaveForModel, hasNamedProfile)", source, StringComparison.Ordinal);
        Assert.Contains("Select a named launch profile to update it", source, StringComparison.Ordinal);
        Assert.Contains("public void ApplyControlState(LaunchSettingsControlStatePlan plan)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsSearch.From(LaunchSettingsSearchBox?.Text)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("ApplyLaunchSectionVisibility(plan, search)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("AdvancedLaunchSettingLabels.Contains(label)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("Terms.All(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("_launchSettingsPanel.ApplyControlState(plan)", source, StringComparison.Ordinal);
        Assert.Contains("LaunchTextBox(request.Settings.Port)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("Tooltip.LaunchPortBox", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsFormBinder.Read(_settings, _launchSettingsPanel.FormControls)", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsFormBinder.Apply(_launchSettingsPanel.FormControls", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsFormBinder.AttachChangeHandlers(_launchSettingsPanel.FormControls", source, StringComparison.Ordinal);
        Assert.Contains("public sealed record LaunchSettingsPanelRequest", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("public sealed class LaunchSettingsPanelControls", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsSearchBox = launchSettingsSearchBox", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("AdvancedLaunchSettingsButton = advancedLaunchSettingsButton", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingElements = launchSettingElements", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingSections = launchSettingSections", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("AdvancedLaunchSettingLabels = advancedLaunchSettingLabels", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("public sealed class LaunchSettingsFormControls", launchFormBinder, StringComparison.Ordinal);
        Assert.Contains("public static void ValidateCrossFieldRules", launchFormBinder, StringComparison.Ordinal);
        Assert.DoesNotContain("Port = ReadInt(_launchPortBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LaunchPortBox = _launchPortBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private WpfTextBox? _launchPortBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_launchSettingsFormControls", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeCombo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveModelLaunchSettingsButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveAsNewModelNameBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveAsNewModelButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_launchSettingElements", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_advancedLaunchSections", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_advancedLaunchSettingsToggle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelCapabilityText", source, StringComparison.Ordinal);
        Assert.Contains("var launchSettings = ModelServices.ModelLaunchSettingsWorkflow;", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.LaunchSettingsRenderApplication.RenderSelectedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("SelectedModelLaunchProfileId());", source, StringComparison.Ordinal);
        Assert.Contains("private async Task<ModelLaunchSettings?> EnsureModelLaunchProfileAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelLaunchPortAvailableAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GetModelLaunchSettingsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveModelLaunchSettingsAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelPortAllocator.NextAvailable", source, StringComparison.Ordinal);
        Assert.Contains("ReadAsync(ModelRecord model, string profileId", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("DraftAsync(ModelRecord model, AppSettings defaults, string profileId", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("ListNamedAsync(model)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("SaveNamedAsync(saved)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("EnsureAsync(ModelRecord model, AppSettings defaults)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("IsPortAvailableAsync(string modelId, int port, AppSettings settings, string currentProfileId", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("ModelPortAllocator.NextAvailable(settings.Port, used)", launchProfileService, StringComparison.Ordinal);
        Assert.Contains("foreach (var sectionGroup in LaunchSettingUiSchema.Definitions.GroupBy", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"ContextSize\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"FlashAttention\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"SpeculativeMtp\", \"SpecType\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ChatCapabilities\", \"Vision\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"GenerationDefaults\", \"Temperature\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ContextExtension\", \"RopeScaling\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Server\", \"ParallelSlots\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsSearchHost(request.LaunchSettingsSearchChanged, out searchBox)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("FormControls.RuntimeOptions?.ApplyVisibility(plan.ShowAdvancedSections, LaunchSettingsSearchBox?.Text)", launchPanelState, StringComparison.Ordinal);
        Assert.Contains("AdvancedButtonText(showAdvanced)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingEditorKind.VisionProjector", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("Picker.Vision.Embedded", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingEditorKind.MtpHead", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ChatCapabilities\", \"ImageMin\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"ChatCapabilities\", \"ImageMax\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"GpuMode\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"GpuDevices\", advanced: true", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"Basic\", \"GpuSplit\", advanced: true", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("public bool VisionLaunchSettingsAvailable => Capabilities.LikelyVision;", selectedCapabilities, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.SelectedCapabilities.Apply(model, capabilities)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.LaunchSettingsControlStates.Build(new LaunchSettingsControlStateRequest(", source, StringComparison.Ordinal);
        Assert.Contains("\"Image min\"] = visionAvailable", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"Image max\"] = visionAvailable", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"Vision head\"] = visionAvailable", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"Draft model\"", controlStateService, StringComparison.Ordinal);
        Assert.Contains("\"MTP head\"", controlStateService, StringComparison.Ordinal);
        Assert.DoesNotContain("var visionLaunchSettingsAvailable = _coreServices.Ui.SelectedCapabilities.VisionLaunchSettingsAvailable;", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLaunchSettingVisible(\"Image min\", visionLaunchSettingsAvailable);", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private void SetLaunchSettingVisible", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectedModelCapabilities", source, StringComparison.Ordinal);

        Assert.Contains("builder.AddSection(title, section, grid, isAdvancedSection);", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("if (isAdvancedSection)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("builder.AddAdvancedSection(section);", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("if (definition.Advanced)", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"KvOffload\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"PromptCache\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"PerformanceMemory\", \"MemoryLock\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"SpeculativeMtp\", \"DraftGpu\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"GenerationDefaults\", \"MaxTokens\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("\"GenerationDefaults\", \"Frequency\"", launchUiSchema, StringComparison.Ordinal);
        Assert.Contains("nameof(AppSettings.CustomParameters)", launchUiSchema, StringComparison.Ordinal);
        Assert.DoesNotContain("Performance & Memory - Advanced", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Speculative / MTP - Advanced", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Generation Defaults - Advanced", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingsControlStateServiceOwnsGpuVisionAndSpeculativeRules()
    {
        var service = new LaunchSettingsControlStateService();

        var cudaVisionDraft = service.Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: true,
            RuntimeBackend: RuntimeBackend.Cuda,
            VisionLaunchSettingsAvailable: true,
            SpeculativeType: "draft-model"));
        var cpuNoVision = service.Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: false,
            RuntimeBackend: RuntimeBackend.Cpu,
            VisionLaunchSettingsAvailable: false,
            SpeculativeType: "none"));
        var mtpHead = service.Build(new LaunchSettingsControlStateRequest(
            ShowAdvancedSections: true,
            RuntimeBackend: RuntimeBackend.Cuda,
            VisionLaunchSettingsAvailable: false,
            SpeculativeType: "atomic-mtp"));

        Assert.True(cudaVisionDraft.ShowAdvancedSections);
        Assert.True(cudaVisionDraft.GpuLayersAvailable);
        Assert.True(cudaVisionDraft.VisionLaunchSettingsAvailable);
        Assert.True(cudaVisionDraft.DraftSpeculativeSettingsAvailable);
        Assert.False(cudaVisionDraft.MtpHeadSettingsAvailable);
        Assert.True(cudaVisionDraft.VisibleSettings["GPU layers"]);
        Assert.True(cudaVisionDraft.VisibleSettings["GPU mode"]);
        Assert.True(cudaVisionDraft.EnabledSettings["GPU devices"]);
        Assert.True(cudaVisionDraft.VisibleSettings["Vision head"]);
        Assert.True(cudaVisionDraft.VisibleSettings["Image min"]);
        Assert.True(cudaVisionDraft.EnabledSettings["Draft model"]);
        Assert.True(cudaVisionDraft.EnabledSettings["Split prob"]);

        Assert.False(cpuNoVision.ShowAdvancedSections);
        Assert.False(cpuNoVision.GpuLayersAvailable);
        Assert.True(cpuNoVision.VisionLaunchSettingsAvailable);
        Assert.False(cpuNoVision.DraftSpeculativeSettingsAvailable);
        Assert.False(cpuNoVision.MtpHeadSettingsAvailable);
        Assert.False(cpuNoVision.VisibleSettings["GPU layers"]);
        Assert.False(cpuNoVision.VisibleSettings["GPU mode"]);
        Assert.False(cpuNoVision.EnabledSettings["GPU split"]);
        Assert.True(cpuNoVision.VisibleSettings["Vision head"]);
        Assert.True(cpuNoVision.VisibleSettings["Image max"]);
        Assert.False(cpuNoVision.EnabledSettings["Draft GPU"]);
        Assert.True(cpuNoVision.VisibleSettings["Reasoning"]);
        Assert.True(cpuNoVision.VisibleSettings["Jinja chat"]);

        Assert.True(mtpHead.MtpHeadSettingsAvailable);
        Assert.False(mtpHead.DraftSpeculativeSettingsAvailable);
        Assert.True(mtpHead.EnabledSettings["MTP head"]);
        Assert.False(mtpHead.EnabledSettings["Draft model"]);
    }


    [Theory]
    [InlineData("196", 200704)]
    [InlineData("196k", 200704)]
    [InlineData("196K", 200704)]
    [InlineData("196.5k", 201728)]
    [InlineData("196000", 195584)]
    [InlineData("128,000", 128000)]
    [InlineData("1m", 1048576)]
    [InlineData("0", 0)]
    public void ContextSizeParserNormalizesShorthandToLlamaFriendlySteps(string text, int expected)
    {
        var ok = LaunchSettingParser.TryNormalizeContextSize(text, out var value);

        Assert.True(ok);
        Assert.Equal(expected, value);
    }


    [Fact]
    public void LaunchSettingParserValidatesNumericInputs()
    {
        Assert.Equal(200704, LaunchSettingParser.ReadContextSize("196k"));
        Assert.Equal(4, LaunchSettingParser.ReadInt("4", "Threads", 0, 8));
        Assert.Equal(0.75, LaunchSettingParser.ReadDouble("0.75", "Top P", 0, 1), precision: 3);
        Assert.Contains("whole number", Assert.Throws<InvalidOperationException>(() => LaunchSettingParser.ReadInt("1.5", "Threads", 0)).Message, StringComparison.Ordinal);
        Assert.Contains("at least 0", Assert.Throws<InvalidOperationException>(() => LaunchSettingParser.ReadDouble("-0.1", "Top P", 0, 1)).Message, StringComparison.Ordinal);
        Assert.Contains("no more than 1", Assert.Throws<InvalidOperationException>(() => LaunchSettingParser.ReadDouble("1.2", "Top P", 0, 1)).Message, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingsFormBinderOwnsCrossFieldValidation()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot());

        LaunchSettingsFormBinder.ValidateCrossFieldRules(settings);
        Assert.Contains("Draft min tokens", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { SpecDraftMinTokens = 32, SpecDraftMaxTokens = 16 })).Message, StringComparison.Ordinal);
        Assert.Contains("Image min tokens", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { VisionImageMinTokens = 640, VisionImageMaxTokens = 320 })).Message, StringComparison.Ordinal);
        Assert.Contains("Draft split probability", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { SpecDraftPSplit = -0.5 })).Message, StringComparison.Ordinal);
        Assert.Contains("Draft min probability", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { SpecDraftPMin = -0.5 })).Message, StringComparison.Ordinal);
        Assert.Contains("Prompt cache MB", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { PromptCacheMode = "on", PromptCacheRamMb = 0 })).Message, StringComparison.Ordinal);
        Assert.Contains("Checkpoint count", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { ContextCheckpointsMode = "on", ContextCheckpointCount = 0 })).Message, StringComparison.Ordinal);
        Assert.Contains("Checkpoint spacing", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { ContextCheckpointsMode = "on", ContextCheckpointEveryNTokens = -1 })).Message, StringComparison.Ordinal);
        Assert.Contains("Single GPU mode", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { GpuMode = "single", GpuDevices = "CUDA0,CUDA1" })).Message, StringComparison.Ordinal);
        Assert.Contains("same number", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { GpuMode = "layer", GpuDevices = "CUDA0,CUDA1", GpuSplit = "1" })).Message, StringComparison.Ordinal);
        Assert.Contains("unterminated quote", Assert.Throws<InvalidOperationException>(() =>
            LaunchSettingsFormBinder.ValidateCrossFieldRules(settings with { CustomParameters = "\"oops" })).Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public void LaunchSettingsSaveStateServiceOwnsButtonRules()
    {
        var source = ReadMainWindowSources();
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var model = new ModelRecord("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var saved = ModelLaunchSettings.FromAppSettings(settings);
        var changed = saved with { ContextSize = saved.ContextSize + 1024 };

        var noSelection = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            null,
            HasSavedProfile: false,
            SavedProfile: null,
            CurrentProfileReadable: false,
            CurrentProfile: null,
            RequestedVariantName: "Qwen 32K"));
        var newProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: false,
            SavedProfile: null,
            CurrentProfileReadable: false,
            CurrentProfile: null,
            RequestedVariantName: model.Name));
        var unreadableCurrentProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: true,
            SavedProfile: saved,
            CurrentProfileReadable: false,
            CurrentProfile: null,
            RequestedVariantName: "Qwen 32K"));
        var cleanProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: true,
            SavedProfile: saved,
            CurrentProfileReadable: true,
            CurrentProfile: saved,
            RequestedVariantName: "Qwen 32K"));
        var dirtyProfile = LaunchSettingsSaveStateService.Evaluate(new LaunchSettingsSaveStateRequest(
            model,
            HasSavedProfile: true,
            SavedProfile: saved,
            CurrentProfileReadable: true,
            CurrentProfile: changed,
            RequestedVariantName: "  qwen  "));

        Assert.Equal(LaunchSettingsSaveStateService.SaveForModelText, noSelection.SaveForModelContent);
        Assert.False(noSelection.CanSaveForModel);
        Assert.False(noSelection.CanSaveAsNewVariant);
        Assert.Equal(LaunchSettingsSaveStateService.SaveForModelText, newProfile.SaveForModelContent);
        Assert.True(newProfile.CanSaveForModel);
        Assert.True(newProfile.CanSaveAsNewVariant);
        Assert.True(unreadableCurrentProfile.CanSaveForModel);
        Assert.True(unreadableCurrentProfile.CanSaveAsNewVariant);
        Assert.Equal(LaunchSettingsSaveStateService.SavedText, cleanProfile.SaveForModelContent);
        Assert.False(cleanProfile.CanSaveForModel);
        Assert.True(cleanProfile.CanSaveAsNewVariant);
        Assert.Equal(LaunchSettingsSaveStateService.SaveForModelText, dirtyProfile.SaveForModelContent);
        Assert.True(dirtyProfile.CanSaveForModel);
        Assert.True(dirtyProfile.CanSaveAsNewVariant);
        Assert.Contains("LaunchSettingsSaveStateService.Evaluate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveModelLaunchSettingsButton.Content = \"Saved\"", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingsEditorSessionOwnsSelectedProfileSnapshot()
    {
        var source = ReadMainWindowSources();
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var model = new ModelRecord("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var saved = ModelLaunchSettings.FromAppSettings(settings) with { ContextSize = 32768 };
        var session = new LaunchSettingsEditorSession();
        var viewState = new ModelLaunchSettingsViewState(
            model.Id,
            HasSavedProfile: true,
            SavedProfile: saved,
            RuntimeId: "runtime-1",
            LaunchSettings: saved.ApplyTo(settings));

        Assert.False(session.IsLoadedFor(model.Id));
        Assert.True(session.TryChangeSaveAsNewSource(model));
        Assert.False(session.TryChangeSaveAsNewSource(model));

        session.Load(viewState);

        Assert.True(session.IsLoadedFor("MODEL-1"));
        Assert.True(session.HasSavedProfile);
        Assert.Same(saved, session.SavedProfile);
        Assert.False(session.IsProgrammaticUpdate);
        var observedProgrammaticUpdate = false;
        session.RunProgrammaticUpdate(() => observedProgrammaticUpdate = session.IsProgrammaticUpdate);
        Assert.True(observedProgrammaticUpdate);
        Assert.False(session.IsProgrammaticUpdate);

        var nextSaved = saved with { ContextSize = 65536 };
        session.MarkSaved(model.Id, "", nextSaved);

        Assert.Same(nextSaved, session.SavedProfile);

        session.Clear();

        Assert.False(session.IsLoadedFor(model.Id));
        Assert.False(session.HasSavedProfile);
        Assert.Null(session.SavedProfile);
        Assert.Contains("_coreServices.Ui.LaunchSettingsEditor.Load,", source, StringComparison.Ordinal);
        Assert.Contains("LaunchSettingsRenderActions(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelLaunchSettingsSaveApplication.SaveSelectedProfileAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.LaunchSettingsEditor.MarkSaved,", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.LaunchSettingsEditor.IsLoadedFor,", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.LaunchSettingsEditor.TryChangeSaveAsNewSource(model, SelectedModelLaunchProfileId())", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.LaunchSettingsEditor.RunProgrammaticUpdate", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.LaunchSettingsEditor.IsProgrammaticUpdate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_launchSettingsModelId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_savedLaunchSettingsSnapshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_hasSavedLaunchSettingsSnapshot", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_saveAsNewSourceModelId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_updatingLaunchSettingsControls", source, StringComparison.Ordinal);
    }


    [Fact]
    public void LaunchSettingMetadataOwnsTooltipsAndContextSuggestions()
    {
        Loc.LoadLanguage("en");
        Assert.Equal(Loc.T("Tooltip.Field.ContextSize"), LaunchSettingMetadataService.Tooltip("Context size"));
        Assert.Equal(Loc.T("Tooltip.Field.Default"), LaunchSettingMetadataService.Tooltip("Unknown setting"));
        Assert.Contains("Suggestion:", LaunchSettingMetadataService.ContextSizeTooltip("196k"), StringComparison.Ordinal);
        Assert.DoesNotContain("Suggestion:", LaunchSettingMetadataService.ContextSizeTooltip("200704"), StringComparison.Ordinal);
        Assert.DoesNotContain("Suggestion:", LaunchSettingMetadataService.ContextSizeTooltip("200_704"), StringComparison.Ordinal);
        Assert.Contains("q4_0", LaunchSettingMetadataService.CacheTypeOptions);
        Assert.Contains("atomic-mtp", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.DoesNotContain("mtp", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.Contains("draft-mtp", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.Contains("draft-dspark", LaunchSettingMetadataService.SpeculativeTypeOptions);
        Assert.True(LaunchSettingMetadataService.IsAtomicMtpSpeculativeType("mtp"));
        Assert.Equal("mtp", LaunchSettingMetadataService.LlamaSpeculativeTypeArgument("atomic-mtp"));
        Assert.Equal(Loc.T("Tooltip.Current.MtpHead"), LaunchSettingMetadataService.Tooltip("MTP head"));
        Assert.Equal(Loc.T("Tooltip.Field.CustomParams"), LaunchSettingMetadataService.Tooltip("Custom params"));
        Assert.Equal(Loc.T("Tooltip.Field.ImageMin"), LaunchSettingMetadataService.Tooltip("Image min"));
        Assert.Equal(Loc.T("Tooltip.Field.ImageMax"), LaunchSettingMetadataService.Tooltip("Image max"));
        Assert.Equal("auto", LaunchSettingMetadataService.AutoOnOffOptions[0]);
        Assert.Equal("on", LaunchSettingMetadataService.OnOffOptions[0]);
    }


    [Fact]
    public void AppOwnedDeletionRejectsRootAndOutsidePaths()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "model",
            "Model",
            Path.Combine(root, "models", "model.gguf"),
            OwnershipKind.AppOwned,
            "{}",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => FileOwnershipService.EnsureDeletionAllowed(model, root, root));
        Assert.Throws<InvalidOperationException>(() => FileOwnershipService.EnsureDeletionAllowed(model, Path.GetTempPath(), root));

        var appOwnedChild = Path.Combine(root, "models", "model");
        FileOwnershipService.EnsureDeletionAllowed(model, appOwnedChild, root);
    }


    [Fact]
    public void AppOwnedDeletionRejectsExistingFolderThatDoesNotContainModel()
    {
        var root = CreateTempRoot();
        var target = Path.Combine(root, "models", "different-model");
        Directory.CreateDirectory(target);
        var model = new ModelRecord(
            "model",
            "Model",
            Path.Combine(root, "models", "model.gguf"),
            OwnershipKind.AppOwned,
            "{}",
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() => FileOwnershipService.EnsureDeletionAllowed(model, target, root));
    }



    [Fact]
    public void ModelFolderApplicationServiceOwnsFolderResolutionAndBlockedStatus()
    {
        var root = CreateTempRoot();
        var service = new ModelFolderApplicationService();
        var calls = new List<string>();
        var model = new ModelRecord(
            "model",
            "Model",
            Path.Combine(root, "models", "model.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var invalid = model with { ModelPath = "model.gguf" };

        ModelFolderApplicationActions Actions()
            => new(
                folder => calls.Add($"open:{folder}"),
                status => calls.Add($"status:{status}"));

        var ignored = service.Open(null, Actions());
        var blocked = service.Open(invalid, Actions());
        var opened = service.Open(model, Actions());

        Assert.Equal(ModelFolderApplicationOutcome.Ignored, ignored);
        Assert.Equal(ModelFolderApplicationOutcome.Blocked, blocked);
        Assert.Equal(ModelFolderApplicationOutcome.Opened, opened);
        Assert.Equal([
            "status:Model folder is unavailable.",
            $"open:{Path.Combine(root, "models")}"
        ], calls);
    }
}
