namespace LocalLlmConsole.Services;

public enum RuntimeSourceApplicationOutcome
{
    Blocked,
    UnknownLocalVersion,
    Applied
}

public sealed record RuntimeSourceApplicationActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshRuntimesAsync,
    Func<Task> RefreshOverviewAsync,
    Func<Task> YieldUiAsync,
    Action RefreshRuntimeBuildGrid,
    Action<string> SetStatus,
    Action<string, string> ShowInformation);

public sealed class RuntimeSourceApplicationService
{
    private const string DownloadDisabledMessage =
        "Check the source repository first. Download is enabled only after a successful check finds source that is not already downloaded or built.";

    private readonly StateStore _stateStore;
    private readonly RuntimeCatalogDataService _catalogData;
    private readonly RuntimeSourceWorkflowService _sourceWorkflow;

    public RuntimeSourceApplicationService(
        StateStore stateStore,
        RuntimeCatalogDataService catalogData,
        RuntimeSourceWorkflowService sourceWorkflow)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _catalogData = catalogData ?? throw new ArgumentNullException(nameof(catalogData));
        _sourceWorkflow = sourceWorkflow ?? throw new ArgumentNullException(nameof(sourceWorkflow));
    }

    public async Task<RuntimeSourceApplicationOutcome> DownloadAsync(
        RuntimeBuildPreset preset,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        long maxLogBytes,
        RuntimeSourceApplicationActions actions)
    {
        Validate(preset, settings, sessionState, actions);

        var local = await BuildLocalStateAsync(preset, settings, sessionState);
        if (!local.CanDownload)
        {
            actions.ShowInformation("Download disabled", DownloadDisabledMessage);
            await actions.RefreshRuntimesAsync();
            return RuntimeSourceApplicationOutcome.Blocked;
        }

        await actions.RunBusyAsync($"Downloading {preset.Label}...", async () =>
        {
            await _sourceWorkflow.DownloadAsync(new RuntimeSourceDownloadWorkflowRequest(
                preset,
                settings,
                maxLogBytes));
            await actions.RefreshRuntimesAsync();
            await actions.RefreshOverviewAsync();
        });
        return RuntimeSourceApplicationOutcome.Applied;
    }

    public async Task<RuntimeSourceApplicationOutcome> CheckUpdateAsync(
        RuntimeBuildPreset preset,
        RuntimeBuildPresetRow? row,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        long maxLogBytes,
        RuntimeSourceApplicationActions actions)
    {
        Validate(preset, settings, sessionState, actions);

        if (row is not null)
        {
            ApplyCheckingState(row);
            actions.RefreshRuntimeBuildGrid();
            await actions.YieldUiAsync();
        }

        var sourcesTask = _catalogData.LoadSourcesAsync(settings.RuntimeRoot);
        var runtimesTask = _stateStore.ListRuntimesAsync();
        await Task.WhenAll(sourcesTask, runtimesTask);
        var sources = await sourcesTask;
        var runtimes = await runtimesTask;
        var localState = RuntimeCatalogDataService.BuildPresetLocalState(
            preset,
            runtimes,
            sources,
            sessionState.RuntimeUpdateStates);
        var local = RuntimeSourceRepositoryService.LatestLocalVersion(preset, sources, runtimes);
        if (localState.LocalCount > 0 && string.IsNullOrWhiteSpace(local.Commit))
        {
            ApplyUnknownLocalVersion(row);
            if (row is not null)
                actions.RefreshRuntimeBuildGrid();
            actions.SetStatus("Local runtime version is unknown. Delete the local source/build before checking again.");
            return RuntimeSourceApplicationOutcome.UnknownLocalVersion;
        }

        await actions.RunBusyAsync($"Checking {preset.Label} for updates...", async () =>
        {
            try
            {
                var outcome = await _sourceWorkflow.CheckUpdateAsync(new RuntimeSourceUpdateCheckWorkflowRequest(
                    preset,
                    local,
                    maxLogBytes));
                var updateState = sessionState.SetRuntimeUpdateState(preset.Id, outcome.State);
                if (row is not null)
                {
                    await ApplyCheckResultAsync(preset, row, settings, sessionState, updateState, outcome.Message);
                    actions.RefreshRuntimeBuildGrid();
                }

                actions.ShowInformation("Runtime update check", outcome.Message);
            }
            catch (Exception ex)
            {
                if (row is not null)
                {
                    ApplyCheckFailedState(row, ex.Message);
                    actions.RefreshRuntimeBuildGrid();
                }
                throw;
            }
        });
        await actions.RefreshRuntimesAsync();
        return RuntimeSourceApplicationOutcome.Applied;
    }

    private async Task<RuntimeBuildPresetLocalState> BuildLocalStateAsync(
        RuntimeBuildPreset preset,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState)
    {
        var sourcesTask = _catalogData.LoadSourcesAsync(settings.RuntimeRoot);
        var runtimesTask = _stateStore.ListRuntimesAsync();
        await Task.WhenAll(sourcesTask, runtimesTask);
        return RuntimeCatalogDataService.BuildPresetLocalState(
            preset,
            await runtimesTask,
            await sourcesTask,
            sessionState.RuntimeUpdateStates);
    }

    private async Task ApplyCheckResultAsync(
        RuntimeBuildPreset preset,
        RuntimeBuildPresetRow row,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        RuntimeUpdateState updateState,
        string message)
    {
        var currentLocal = await BuildLocalStateAsync(preset, settings, sessionState);
        row.LocalStatus = updateState.HasUpdate ? "Update available" : "Up to date";
        row.LatestLocal = message;
        row.CheckAction = "Check";
        row.CanCheck = true;
        row.DownloadAction = currentLocal.DownloadAction;
        row.CanDownload = currentLocal.CanDownload;
    }

    private static void ApplyCheckingState(RuntimeBuildPresetRow row)
    {
        row.LocalStatus = "Checking...";
        row.LatestLocal = "Checking remote...";
        row.CheckAction = "Checking";
        row.CanCheck = false;
    }

    private static void ApplyUnknownLocalVersion(RuntimeBuildPresetRow? row)
    {
        if (row is null) return;
        row.LatestLocal = "Cannot compare: local commit unavailable. Delete the local source/build and download again if you need to refresh metadata.";
        row.LocalStatus = "Version unknown";
        row.CheckAction = "Check";
        row.CanDownload = false;
        row.CanCheck = true;
    }

    private static void ApplyCheckFailedState(RuntimeBuildPresetRow row, string message)
    {
        row.LocalStatus = "Check failed";
        row.LatestLocal = $"Check failed: {message}";
        row.CheckAction = "Check";
        row.CanCheck = true;
    }

    private static void Validate(
        RuntimeBuildPreset preset,
        AppSettings settings,
        RuntimeCatalogSessionState sessionState,
        RuntimeSourceApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(preset);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessionState);
        Validate(actions);
    }

    private static void Validate(RuntimeSourceApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
        ArgumentNullException.ThrowIfNull(actions.YieldUiAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimeBuildGrid);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
        ArgumentNullException.ThrowIfNull(actions.ShowInformation);
    }
}
