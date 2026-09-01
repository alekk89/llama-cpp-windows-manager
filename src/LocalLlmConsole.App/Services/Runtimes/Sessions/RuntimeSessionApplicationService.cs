namespace LocalLlmConsole.Services;

public sealed record RuntimeSessionStopSelectedApplicationRequest(
    LoadedModelSessionSnapshot? SelectedSession,
    bool SelectedModelIsLoading);

public sealed record RuntimeSessionStopModelApplicationRequest(
    ModelRecord Model,
    LoadedModelSessionSnapshot? StoppedSession,
    bool ModelIsActive,
    bool ModelIsLoading);

public sealed record RuntimeStopApplicationRequest(
    RuntimeStopDecision Decision,
    LoadedModelSessionSnapshot? StoppedSession,
    bool ResetMetricCountersBeforeStop,
    Func<Task<RuntimeSessionStopResult>> StopAsync);

public sealed record RuntimeStopApplicationActions(
    Action<string> StopReadinessMonitor,
    Action StopLoadingTimer,
    Action ResetMetricCounters,
    Action<LoadedModelSessionSnapshot?> ResetLifetimeCounters,
    Action<LoadedModelSessionSnapshot?> ResetIdleCounters,
    Action<AppSettings?> SetActiveRuntimeSettings,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Func<Task> RefreshOverviewAsync,
    Func<Task> RefreshRuntimeMetricsAsync,
    Action UpdateActionButtons,
    Action<string> SetStatus);

public sealed record RuntimeSwitchApplicationActions(
    Action<AppSettings?> SetActiveRuntimeSettings,
    Action ResetMetricCounters,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Action StartRuntimeDashboardRefresh,
    Func<Task> RefreshOverviewModelSelectorAsync,
    Func<Task> RefreshRuntimeMetricsAsync,
    Action UpdateActionButtons,
    Action<string> SetStatus);

public sealed class RuntimeSessionApplicationService
{
    private readonly RuntimeSessionCommandService _commands;

    public RuntimeSessionApplicationService(RuntimeSessionCommandService commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public Task StopSelectedAsync(
        RuntimeSessionStopSelectedApplicationRequest request,
        RuntimeStopApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);

        var decision = _commands.PlanStopSelected(
            request.SelectedSession,
            request.SelectedModelIsLoading);
        return ApplyStopAsync(
            new RuntimeStopApplicationRequest(
                decision,
                request.SelectedSession,
                ResetMetricCountersBeforeStop: false,
                _commands.StopSelectedAsync),
            actions);
    }

    public Task StopModelAsync(
        RuntimeSessionStopModelApplicationRequest request,
        RuntimeStopApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(request.Model);

        var decision = _commands.PlanStopModel(
            request.Model,
            request.ModelIsActive,
            request.ModelIsLoading);
        return ApplyStopAsync(
            new RuntimeStopApplicationRequest(
                decision,
                request.StoppedSession,
                ResetMetricCountersBeforeStop: true,
                () => _commands.StopModelAsync(request.Model.Id)),
            actions);
    }

    public Task SwitchToModelAsync(
        ModelRecord model,
        RuntimeSwitchApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(actions);

        return ApplySwitchAsync(
            _commands.SwitchToModel(model),
            actions);
    }

    public Task SwitchToProfileAsync(
        ModelRecord model,
        string launchProfileId,
        RuntimeSwitchApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(actions);

        return ApplySwitchAsync(
            _commands.SwitchToProfile(model, launchProfileId),
            actions);
    }

    internal static async Task ApplyStopAsync(
        RuntimeStopApplicationRequest request,
        RuntimeStopApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);

        var decision = request.Decision;
        if (!string.IsNullOrWhiteSpace(decision.ReadinessMonitorModelId))
            actions.StopReadinessMonitor(decision.ReadinessMonitorModelId);
        if (decision.StopLoadingStatus)
            actions.StopLoadingTimer();
        if (request.ResetMetricCountersBeforeStop && decision.ResetMetricCounters)
            actions.ResetMetricCounters();

        actions.ResetLifetimeCounters(request.StoppedSession);
        actions.ResetIdleCounters(request.StoppedSession);

        var result = await request.StopAsync();
        actions.SetActiveRuntimeSettings(result.ActiveSettings);
        await actions.SaveActiveRuntimeSessionsAsync();

        if (!request.ResetMetricCountersBeforeStop && decision.ResetMetricCounters)
            actions.ResetMetricCounters();

        await actions.RefreshOverviewAsync();
        await actions.RefreshRuntimeMetricsAsync();
        actions.UpdateActionButtons();
        actions.SetStatus(decision.StatusMessage);
    }

    internal static async Task ApplySwitchAsync(
        RuntimeSwitchCommandResult result,
        RuntimeSwitchApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(actions);

        var decision = result.Decision;
        if (!decision.Selected)
        {
            actions.SetStatus(decision.StatusMessage);
            return;
        }

        actions.SetActiveRuntimeSettings(result.ActiveSettings);
        if (decision.ResetMetricCounters)
            actions.ResetMetricCounters();
        await actions.SaveActiveRuntimeSessionsAsync();
        if (decision.StartDashboardRefresh)
            actions.StartRuntimeDashboardRefresh();
        await actions.RefreshOverviewModelSelectorAsync();
        await actions.RefreshRuntimeMetricsAsync();
        actions.UpdateActionButtons();
        actions.SetStatus(decision.StatusMessage);
    }
}
