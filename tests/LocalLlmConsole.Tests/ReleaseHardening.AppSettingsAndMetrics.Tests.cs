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


}
