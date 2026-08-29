namespace LocalLlmConsole.Services;

public sealed record RuntimeReadinessMonitorWorkflowRequest(
    string ModelId,
    string ModelName,
    AppSettings LaunchSettings,
    bool ModelIsStillLoading,
    bool IsOverviewPage,
    Func<string, LoadedModelSessionSnapshot?> SessionForModel,
    Func<AppSettings, CancellationToken, Task<bool>> IsEndpointAliveAsync,
    Func<string, bool> MarkModelLoadedIfRunning,
    TimeSpan? PollInterval = null,
    Func<AppSettings, CancellationToken, Task<RuntimeAuthenticationProbeResult>>? VerifyAuthenticationAsync = null);

public sealed record RuntimeReadinessMonitorWorkflowResult(
    RuntimeReadinessStatus Status,
    RuntimeReadinessCompletionPlan CompletionPlan);

public sealed class RuntimeReadinessMonitorWorkflowService
{
    private readonly RuntimeReadinessWorkflowService _readiness;
    private readonly RuntimeReadinessCompletionService _completion;

    public RuntimeReadinessMonitorWorkflowService(
        RuntimeReadinessWorkflowService readiness,
        RuntimeReadinessCompletionService completion)
    {
        _readiness = readiness ?? throw new ArgumentNullException(nameof(readiness));
        _completion = completion ?? throw new ArgumentNullException(nameof(completion));
    }

    public async Task<RuntimeReadinessMonitorWorkflowResult> RunAsync(
        RuntimeReadinessMonitorWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var result = await _readiness.WaitUntilReadyAsync(new RuntimeReadinessWorkflowRequest(
            request.ModelId,
            request.LaunchSettings,
            request.SessionForModel,
            request.IsEndpointAliveAsync,
            request.MarkModelLoadedIfRunning,
            request.PollInterval,
            request.VerifyAuthenticationAsync),
            cancellationToken);

        var completionPlan = _completion.Build(new RuntimeReadinessCompletionRequest(
            result.Status,
            request.ModelName,
            request.LaunchSettings,
            request.ModelIsStillLoading,
            request.IsOverviewPage,
            result.Reason));
        return new RuntimeReadinessMonitorWorkflowResult(result.Status, completionPlan);
    }
}
