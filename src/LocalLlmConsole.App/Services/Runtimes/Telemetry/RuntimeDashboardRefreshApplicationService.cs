namespace LocalLlmConsole.Services;

public enum RuntimeDashboardRefreshApplicationOutcome
{
    Skipped,
    RenderedStoppedSelection,
    Applied
}

public sealed record RuntimeDashboardRefreshApplicationRequest(
    RuntimeDashboardRefreshTarget RefreshTarget,
    bool RenderOverview,
    AppSettings Settings,
    string ActiveModelId,
    string ActiveRuntimeId,
    LlamaRuntimeState RuntimeState,
    bool RuntimeIsRunning);

public sealed record RuntimeDashboardRefreshApplicationActions(
    Func<Task> MarkLoadedSessionsIfReadyAsync,
    Action RefreshOverviewSessionRows,
    Func<IReadOnlyList<LoadedModelSessionSnapshot>> SessionSnapshots,
    Func<IReadOnlyList<RuntimeMetricPollResult>, Task> ApplyEndpointHealthAsync,
    Func<IReadOnlyList<RuntimeMetricPollResult>, Task> TrackLifetimeTokenDeltasAsync,
    Func<IReadOnlyList<RuntimeMetricPollResult>, Task> ApplyIdleUnloadPoliciesAsync,
    Func<ModelRecord?> SelectedOverviewModel,
    Func<ModelRecord, bool> IsModelActive,
    Func<ModelRecord, bool> IsModelLoaded,
    Func<string, LoadedModelSessionSnapshot?> SessionForModel,
    Func<LoadedModelSessionSnapshot?> SelectedSession,
    Func<AppSettings?> ActiveSessionSettings,
    Func<AppSettings?> ActiveRuntimeSettings,
    Func<string, RuntimeSessionSelectResult> SelectModel,
    Action<AppSettings?> SetActiveRuntimeSettings,
    Func<Task<(string Model, string Runtime)>> ActiveRuntimeLabelsAsync,
    Action<string> RefreshModelStatusMetric,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Func<Task<HostHardwareSnapshot>> CachedGpuSummaryAsync,
    Func<HostHardwareSnapshot, Task> SetGpuMetricAsync,
    Func<ModelRecord?, bool, Task> RenderStoppedSelectedOverviewModelAsync,
    RuntimeDashboardMetricsApplicationActions MetricsActions,
    Action UpdateOverviewModelActions);

public sealed class RuntimeDashboardRefreshApplicationService
{
    private readonly RuntimeTelemetryApplicationService _telemetry;
    private readonly RuntimeDashboardSelectionService _selection;
    private readonly RuntimeDashboardMetricsApplicationService _metricsApplication;

    public RuntimeDashboardRefreshApplicationService(
        RuntimeTelemetryApplicationService telemetry,
        RuntimeDashboardSelectionService selection,
        RuntimeDashboardMetricsApplicationService metricsApplication)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        _metricsApplication = metricsApplication ?? throw new ArgumentNullException(nameof(metricsApplication));
    }

    public async Task<RuntimeDashboardRefreshApplicationOutcome> RefreshAsync(
        RuntimeDashboardRefreshApplicationRequest request,
        RuntimeDashboardRefreshApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.RefreshTarget);
        ArgumentNullException.ThrowIfNull(request.Settings);
        Validate(actions);

        using var refreshScope = _telemetry.TryBeginRefresh(request.RefreshTarget);
        if (refreshScope is null)
            return RuntimeDashboardRefreshApplicationOutcome.Skipped;

        try
        {
            await actions.MarkLoadedSessionsIfReadyAsync();
            if (request.RenderOverview)
                actions.RefreshOverviewSessionRows();

            RuntimeMetricPollResult? rendered = null;
            var pollResults = await _telemetry.PollSessionsAsync(actions.SessionSnapshots(), async completed =>
            {
                if (request.RenderOverview)
                    (_, rendered) = await RenderSelectionAsync(request, actions, [completed], rendered, completedOnly: true);
            }, cancellationToken);
            await actions.ApplyEndpointHealthAsync(pollResults);
            await actions.TrackLifetimeTokenDeltasAsync(pollResults);
            await actions.ApplyIdleUnloadPoliciesAsync(pollResults);

            if (request.RuntimeState == LlamaRuntimeState.Failed)
                await actions.SaveActiveRuntimeSessionsAsync();
            var (outcome, _) = await RenderSelectionAsync(request, actions, pollResults, rendered, completedOnly: false);
            if (request.RenderOverview)
                await actions.SetGpuMetricAsync(await actions.CachedGpuSummaryAsync());
            return outcome;
        }
        finally
        {
            actions.UpdateOverviewModelActions();
        }
    }

    private async Task<(RuntimeDashboardRefreshApplicationOutcome Outcome, RuntimeMetricPollResult? Rendered)> RenderSelectionAsync(
        RuntimeDashboardRefreshApplicationRequest request,
        RuntimeDashboardRefreshApplicationActions actions,
        IReadOnlyList<RuntimeMetricPollResult> pollResults,
        RuntimeMetricPollResult? previouslyRendered,
        bool completedOnly)
    {
        var selectedOverviewModel = actions.SelectedOverviewModel();
        var selectedOverviewModelSession = selectedOverviewModel is null
            ? null
            : actions.SessionForModel(selectedOverviewModel.Id);
        var selection = _selection.Select(new RuntimeDashboardSelectionRequest(
            selectedOverviewModel,
            selectedOverviewModel is not null && actions.IsModelActive(selectedOverviewModel),
            selectedOverviewModel is not null && actions.IsModelLoaded(selectedOverviewModel),
            selectedOverviewModelSession,
            actions.SelectedSession(),
            actions.ActiveSessionSettings(),
            actions.ActiveRuntimeSettings(),
            request.Settings,
            request.ActiveModelId,
            request.ActiveRuntimeId));
        if (selection.SelectedOverviewModelHasNoRunningSession && !completedOnly)
        {
            await actions.RenderStoppedSelectedOverviewModelAsync(selectedOverviewModel, request.RenderOverview);
            return (RuntimeDashboardRefreshApplicationOutcome.RenderedStoppedSelection, null);
        }

        var selectedSession = selection.Session;
        var runtimeKey = selection.RuntimeKey;
        var selectedPollResult = selectedSession is null
            ? null
            : pollResults.FirstOrDefault(result => string.Equals(result.RuntimeKey, runtimeKey, StringComparison.Ordinal)
                && string.Equals(result.Session.SessionId, selectedSession.SessionId, StringComparison.Ordinal));

        if (completedOnly && selectedPollResult is null)
            return (RuntimeDashboardRefreshApplicationOutcome.Applied, previouslyRendered);
        if (selectedSession is { IsRunning: true } && selectedPollResult is not null
            && ReferenceEquals(selectedPollResult, previouslyRendered))
            return (RuntimeDashboardRefreshApplicationOutcome.Applied, previouslyRendered);
        if (selection.SelectSelectedOverviewModel)
            actions.SetActiveRuntimeSettings(actions.SelectModel(selectedOverviewModel!.Id).ActiveSettings);

        var (modelName, _) = await actions.ActiveRuntimeLabelsAsync();
        if (request.RenderOverview)
            actions.RefreshModelStatusMetric(modelName);

        await _metricsApplication.ApplyAsync(
            new RuntimeDashboardMetricsApplicationRequest(
                request.RenderOverview,
                selectedSession,
                selection.MetricsSettings,
                selectedPollResult,
                runtimeKey),
            actions.MetricsActions);
        return (RuntimeDashboardRefreshApplicationOutcome.Applied, selectedPollResult);
    }

    private static void Validate(RuntimeDashboardRefreshApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.MarkLoadedSessionsIfReadyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewSessionRows);
        ArgumentNullException.ThrowIfNull(actions.SessionSnapshots);
        ArgumentNullException.ThrowIfNull(actions.ApplyEndpointHealthAsync);
        ArgumentNullException.ThrowIfNull(actions.TrackLifetimeTokenDeltasAsync);
        ArgumentNullException.ThrowIfNull(actions.ApplyIdleUnloadPoliciesAsync);
        ArgumentNullException.ThrowIfNull(actions.SelectedOverviewModel);
        ArgumentNullException.ThrowIfNull(actions.IsModelActive);
        ArgumentNullException.ThrowIfNull(actions.IsModelLoaded);
        ArgumentNullException.ThrowIfNull(actions.SessionForModel);
        ArgumentNullException.ThrowIfNull(actions.SelectedSession);
        ArgumentNullException.ThrowIfNull(actions.ActiveSessionSettings);
        ArgumentNullException.ThrowIfNull(actions.ActiveRuntimeSettings);
        ArgumentNullException.ThrowIfNull(actions.SelectModel);
        ArgumentNullException.ThrowIfNull(actions.SetActiveRuntimeSettings);
        ArgumentNullException.ThrowIfNull(actions.ActiveRuntimeLabelsAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshModelStatusMetric);
        ArgumentNullException.ThrowIfNull(actions.SaveActiveRuntimeSessionsAsync);
        ArgumentNullException.ThrowIfNull(actions.CachedGpuSummaryAsync);
        ArgumentNullException.ThrowIfNull(actions.SetGpuMetricAsync);
        ArgumentNullException.ThrowIfNull(actions.RenderStoppedSelectedOverviewModelAsync);
        ArgumentNullException.ThrowIfNull(actions.MetricsActions);
        ArgumentNullException.ThrowIfNull(actions.UpdateOverviewModelActions);
    }
}
