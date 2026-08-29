namespace LocalLlmConsole.Services;

public enum RuntimeReadinessMonitorApplicationOutcome
{
    Completed,
    Cancelled
}

public sealed record RuntimeReadinessMonitorApplicationRequest(
    string ModelId,
    string ModelName,
    AppSettings LaunchSettings,
    bool ModelIsStillLoading,
    bool IsOverviewPage,
    CancellationTokenSource CancellationSource);

public sealed record RuntimeReadinessMonitorApplicationActions(
    Func<string, LoadedModelSessionSnapshot?> SessionForModel,
    Func<AppSettings, CancellationToken, Task<bool>> IsEndpointAliveAsync,
    Func<AppSettings, CancellationToken, Task<RuntimeAuthenticationProbeResult>> VerifyAuthenticationAsync,
    Func<string, bool> MarkModelLoadedIfRunning,
    RuntimeReadinessCompletionActions CompletionActions,
    Action<string, CancellationTokenSource> CompleteMonitor);

public sealed record RuntimeReadinessCompletionActions(
    Action<bool> StopLoadingStatus,
    Func<Task> SelectLoadedOverviewModelAsync,
    Func<Task> SaveActiveRuntimeSessionsAsync,
    Action<string> SetStatus,
    Action UpdateActionButtons,
    Func<Task> RefreshRuntimeMetricsAsync,
    Func<Task>? StopUnsafeRuntimeAsync = null);

public sealed class RuntimeReadinessMonitorApplicationService
{
    private readonly RuntimeReadinessMonitorWorkflowService _workflow;

    public RuntimeReadinessMonitorApplicationService(RuntimeReadinessMonitorWorkflowService workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    }

    public async Task<RuntimeReadinessMonitorApplicationOutcome> RunAsync(
        RuntimeReadinessMonitorApplicationRequest request,
        RuntimeReadinessMonitorApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(actions);

        var cancellationToken = request.CancellationSource.Token;
        try
        {
            var result = await _workflow.RunAsync(new RuntimeReadinessMonitorWorkflowRequest(
                request.ModelId,
                request.ModelName,
                request.LaunchSettings,
                request.ModelIsStillLoading,
                request.IsOverviewPage,
                actions.SessionForModel,
                actions.IsEndpointAliveAsync,
                actions.MarkModelLoadedIfRunning,
                VerifyAuthenticationAsync: actions.VerifyAuthenticationAsync),
                cancellationToken);

            await ApplyCompletionAsync(result.CompletionPlan, actions.CompletionActions);
            return RuntimeReadinessMonitorApplicationOutcome.Completed;
        }
        catch (OperationCanceledException)
        {
            return RuntimeReadinessMonitorApplicationOutcome.Cancelled;
        }
        finally
        {
            actions.CompleteMonitor(request.ModelId, request.CancellationSource);
        }
    }

    internal static async Task ApplyCompletionAsync(
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

    private static void Validate(RuntimeReadinessMonitorApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.SessionForModel);
        ArgumentNullException.ThrowIfNull(actions.IsEndpointAliveAsync);
        ArgumentNullException.ThrowIfNull(actions.VerifyAuthenticationAsync);
        ArgumentNullException.ThrowIfNull(actions.MarkModelLoadedIfRunning);
        ArgumentNullException.ThrowIfNull(actions.CompletionActions);
        ArgumentNullException.ThrowIfNull(actions.CompleteMonitor);
    }
}
