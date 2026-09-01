namespace LocalLlmConsole.Services;

public static class RuntimeReadinessMonitorActionFactory
{
    public static RuntimeReadinessMonitorApplicationActions Create(
        LoadedModelSessionManager sessions,
        RuntimeReadinessMonitorRegistry monitors,
        LoadedModelSessionSnapshot loadingSession,
        Func<AppSettings, CancellationToken, Task<bool>> isEndpointAliveAsync,
        Func<AppSettings, CancellationToken, Task<RuntimeAuthenticationProbeResult>> verifyAuthenticationAsync,
        Action<bool> stopLoadingStatus,
        Func<string, string, Task> selectLoadedSessionAsync,
        Func<Task> saveActiveSessionsAsync,
        Action<string> setStatus,
        Action updateActionButtons,
        Func<Task> refreshRuntimeMetricsAsync)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(monitors);
        ArgumentNullException.ThrowIfNull(loadingSession);

        return new RuntimeReadinessMonitorApplicationActions(
            sessions.SessionById,
            isEndpointAliveAsync,
            verifyAuthenticationAsync,
            sessions.MarkLoadedIfRunning,
            new RuntimeReadinessCompletionActions(
                stopLoadingStatus,
                () => selectLoadedSessionAsync(loadingSession.SessionId, loadingSession.ModelId),
                saveActiveSessionsAsync,
                setStatus,
                updateActionButtons,
                refreshRuntimeMetricsAsync,
                async () =>
                {
                    if (sessions.SessionById(loadingSession.SessionId) is not null)
                        await sessions.StopAsync(loadingSession.SessionId, "Runtime authentication enforcement failed.");
                }),
            (sessionId, source) => monitors.Complete(sessionId, source));
    }
}
