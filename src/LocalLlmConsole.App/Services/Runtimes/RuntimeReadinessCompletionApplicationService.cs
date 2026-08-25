namespace LocalLlmConsole.Services;

public sealed record RuntimeReadinessCompletionActions(
    Action<bool> StopLoadingStatus,
    Func<Task> SelectLoadedOverviewModelAsync,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Action<string> SetStatus,
    Action UpdateActionButtons,
    Func<Task> RefreshRuntimeMetricsAsync,
    Func<Task>? StopUnsafeRuntimeAsync = null);

public sealed class RuntimeReadinessCompletionApplicationService
{
    public async Task ApplyAsync(
        RuntimeReadinessCompletionPlan plan,
        RuntimeReadinessCompletionActions actions)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(actions);

        if (plan.StopUnsafeRuntime)
            await (actions.StopUnsafeRuntimeAsync
                   ?? throw new InvalidOperationException("Unsafe runtime stop action is required."))();
        if (plan.StopLoadingStatus)
            actions.StopLoadingStatus(plan.ShowLoadedDuration);
        if (plan.SelectLoadedOverviewModel)
            await actions.SelectLoadedOverviewModelAsync();
        if (plan.SaveActiveRuntimeSessions)
            await actions.SaveActiveRuntimeSessionsAsync();
        if (!string.IsNullOrWhiteSpace(plan.StatusMessage))
            actions.SetStatus(plan.StatusMessage);
        if (plan.UpdateActionButtons)
            actions.UpdateActionButtons();
        if (plan.RefreshRuntimeMetrics)
            await actions.RefreshRuntimeMetricsAsync();
    }
}
