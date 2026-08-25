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

public sealed class RuntimeReadinessMonitorApplicationService
{
    private readonly RuntimeReadinessMonitorWorkflowService _workflow;
    private readonly RuntimeReadinessCompletionApplicationService _completionApplication;

    public RuntimeReadinessMonitorApplicationService(
        RuntimeReadinessMonitorWorkflowService workflow,
        RuntimeReadinessCompletionApplicationService completionApplication)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _completionApplication = completionApplication ?? throw new ArgumentNullException(nameof(completionApplication));
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

            await _completionApplication.ApplyAsync(result.CompletionPlan, actions.CompletionActions);
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
