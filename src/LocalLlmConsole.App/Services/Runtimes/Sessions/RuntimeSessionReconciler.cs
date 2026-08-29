namespace LocalLlmConsole.Services;

public sealed record RuntimeSessionLoadedTransition(
    string ModelId,
    string ModelName,
    AppSettings LaunchSettings);

public sealed record RuntimeSessionReconcileResult(
    int RemovedSessionCount,
    IReadOnlyList<RuntimeSessionLoadedTransition> LoadedTransitions,
    IReadOnlyList<LoadedModelSessionSnapshot>? RemovedSessions = null)
{
    public bool HasChanges => RemovedSessionCount > 0 || LoadedTransitions.Count > 0;
}

public sealed class RuntimeSessionReconciler
{
    public async Task<RuntimeSessionReconcileResult> ReconcileAsync(
        LoadedModelSessionManager sessions,
        Func<LoadedModelSessionSnapshot, Task<bool>> recoveredSessionAvailable,
        Func<LoadedModelSessionSnapshot, Task<bool>> loadingSessionReady)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(recoveredSessionAvailable);
        ArgumentNullException.ThrowIfNull(loadingSessionReady);

        var priorSessionIds = sessions.Snapshots().Select(session => session.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var removed = sessions.RemoveFailedOrStopped();
        removed += await sessions.StopUnavailableRecoveredSessionsAsync(recoveredSessionAvailable);

        var loadedTransitions = new List<RuntimeSessionLoadedTransition>();
        foreach (var session in sessions.Snapshots().Where(session => session is { IsRunning: true, Status: LoadedModelSessionStatus.Loading }))
        {
            if (!await loadingSessionReady(session)) continue;
            if (!sessions.MarkModelLoadedIfRunning(session.ModelId)) continue;

            loadedTransitions.Add(new RuntimeSessionLoadedTransition(
                session.ModelId,
                session.ModelName,
                session.LaunchSettings));
        }

        var removedSessions = sessions.OverviewSnapshots()
            .Where(session => !session.IsRunning && priorSessionIds.Contains(session.SessionId))
            .ToArray();
        return new RuntimeSessionReconcileResult(removed, loadedTransitions, removedSessions);
    }
}
