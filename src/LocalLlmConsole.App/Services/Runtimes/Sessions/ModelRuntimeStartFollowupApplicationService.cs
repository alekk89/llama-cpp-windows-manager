namespace LocalLlmConsole.Services;

public sealed record ModelRuntimeStartSessionActions(
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Action StartReadinessMonitor,
    Action StartRuntimeDashboardRefresh,
    Action UpdateLoadingStatus,
    Func<Task> RefreshOverviewAsync,
    Func<Task> RefreshOverviewModelSelectorAsync,
    Func<TimeSpan, Task> DelayAsync,
    Func<Task> RefreshRuntimeMetricsAsync);

public sealed record ModelRuntimeStartInitialMetricsActions(
    Action StopRuntimeDashboardRefresh,
    Action UpdateActionButtons,
    Action StopLoadingTimer,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Action<string> SetStatus,
    Action UpdateLoadingStatus);

public sealed class ModelRuntimeStartFollowupApplicationService
{
    public async Task ApplyAfterSessionStartedAsync(
        ModelRuntimeStartSessionPlan plan,
        ModelRuntimeStartSessionActions actions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(actions);

        if (plan.SaveActiveRuntimeSessions)
            await actions.SaveActiveRuntimeSessionsAsync();
        if (plan.StartReadinessMonitor)
            actions.StartReadinessMonitor();
        if (plan.StartRuntimeDashboardRefresh)
            actions.StartRuntimeDashboardRefresh();
        if (plan.UpdateLoadingStatus)
            actions.UpdateLoadingStatus();
        if (plan.RefreshOverview)
            await actions.RefreshOverviewAsync();
        if (plan.RefreshOverviewModelSelector)
            await actions.RefreshOverviewModelSelectorAsync();
        if (plan.InitialMetricsDelay > TimeSpan.Zero)
            await actions.DelayAsync(plan.InitialMetricsDelay);
        if (plan.RefreshRuntimeMetrics)
            await actions.RefreshRuntimeMetricsAsync();
    }

    public async Task ApplyAfterInitialMetricsAsync(
        ModelRuntimeStartInitialMetricsPlan plan,
        ModelRuntimeStartInitialMetricsActions actions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(actions);

        if (plan.StopRuntimeDashboardRefresh)
            actions.StopRuntimeDashboardRefresh();
        if (plan.UpdateActionButtons)
            actions.UpdateActionButtons();
        if (plan.StopLoadingTimer)
            actions.StopLoadingTimer();
        if (plan.SaveActiveRuntimeSessions)
            await actions.SaveActiveRuntimeSessionsAsync();
        if (!string.IsNullOrWhiteSpace(plan.StatusMessage))
            actions.SetStatus(plan.StatusMessage);
        if (plan.UpdateLoadingStatus)
            actions.UpdateLoadingStatus();
    }
}
