namespace LocalLlmConsole.Services;

public enum RuntimeReadinessStatus
{
    Loaded,
    NoLongerLoading,
    SessionChanged,
    AuthenticationFailed
}

public sealed record RuntimeReadinessResult(RuntimeReadinessStatus Status, string Reason = "");

public sealed record RuntimeReadinessWorkflowRequest(
    string ModelId,
    AppSettings LaunchSettings,
    Func<string, LoadedModelSessionSnapshot?> SessionForModel,
    Func<AppSettings, CancellationToken, Task<bool>> IsEndpointAliveAsync,
    Func<string, bool> MarkModelLoadedIfRunning,
    TimeSpan? PollInterval = null,
    Func<AppSettings, CancellationToken, Task<RuntimeAuthenticationProbeResult>>? VerifyAuthenticationAsync = null);

public sealed class RuntimeReadinessWorkflowService
{
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromSeconds(2);

    public async Task<RuntimeReadinessResult> WaitUntilReadyAsync(
        RuntimeReadinessWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.SessionForModel);
        ArgumentNullException.ThrowIfNull(request.IsEndpointAliveAsync);
        ArgumentNullException.ThrowIfNull(request.MarkModelLoadedIfRunning);

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(request.PollInterval ?? DefaultPollInterval, cancellationToken);

            var session = request.SessionForModel(request.ModelId);
            if (session is not { IsRunning: true, Status: LoadedModelSessionStatus.Loading })
                return new RuntimeReadinessResult(RuntimeReadinessStatus.NoLongerLoading);

            if (!await request.IsEndpointAliveAsync(request.LaunchSettings, cancellationToken))
                continue;

            if (request.VerifyAuthenticationAsync is not null)
            {
                var authentication = await request.VerifyAuthenticationAsync(request.LaunchSettings, cancellationToken);
                if (authentication.Status == RuntimeAuthenticationProbeStatus.Unavailable)
                    continue;
                if (!authentication.IsVerified)
                    return new RuntimeReadinessResult(
                        RuntimeReadinessStatus.AuthenticationFailed,
                        authentication.Message);
            }

            return request.MarkModelLoadedIfRunning(request.ModelId)
                ? new RuntimeReadinessResult(RuntimeReadinessStatus.Loaded)
                : new RuntimeReadinessResult(RuntimeReadinessStatus.SessionChanged);
        }
    }
}
