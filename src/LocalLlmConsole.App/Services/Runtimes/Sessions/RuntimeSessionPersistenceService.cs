namespace LocalLlmConsole.Services;

public sealed record RuntimeSessionPersistenceResult(int SavedSessionCount, bool Cleared);

public sealed class RuntimeSessionPersistenceService
{
    private readonly ActiveRuntimeSessionStore _store;
    private readonly LoadedModelSessionManager _sessions;

    public RuntimeSessionPersistenceService(ActiveRuntimeSessionStore store, LoadedModelSessionManager sessions)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public Task<IReadOnlyList<ActiveRuntimeSession>> ReadAllAsync(CancellationToken cancellationToken = default)
        => _store.ReadAllAsync(cancellationToken);

    public async Task<RuntimeSessionPersistenceResult> SaveRunningAsync(CancellationToken cancellationToken = default)
    {
        var sessions = ActiveSessionsFrom(_sessions.Snapshots());
        if (sessions.Count == 0)
        {
            Clear();
            return new RuntimeSessionPersistenceResult(0, Cleared: true);
        }

        await _store.SaveAllAsync(sessions, cancellationToken);
        return new RuntimeSessionPersistenceResult(sessions.Count, Cleared: false);
    }

    public void Clear()
        => _store.Clear();

    public static IReadOnlyList<ActiveRuntimeSession> ActiveSessionsFrom(IEnumerable<LoadedModelSessionSnapshot> snapshots)
        => snapshots
            .Where(session => session.IsRunning)
            .Select(session => new ActiveRuntimeSession(
                session.ModelId,
                session.RuntimeId,
                session.LaunchSettings,
                session.LogPath,
                session.StartedAt,
                session.ProcessMarker,
                session.ProcessId,
                session.SessionId,
                session.IsSelected,
                session.LaunchProfileId,
                session.LaunchProfileName))
            .ToArray();
}
