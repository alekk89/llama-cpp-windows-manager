namespace LocalLlmConsole.Services;

public sealed record RuntimeSessionRecoveryApplicationActions(
    Func<Task<IReadOnlyList<ModelRecord>>> ListModelsAsync,
    Func<Task<IReadOnlyList<RuntimeRecord>>> ListRuntimesAsync,
    RuntimeServedModelsReader ReadServedModelsAsync,
    RuntimeEndpointStatusProbe IsEndpointAliveAsync,
    RuntimeEndpointStatusProbe IsEndpointRespondingAsync,
    Action<ModelRecord, AppSettings> StartLoadingStatus,
    Action<ModelRecord, AppSettings> StartReadinessMonitor,
    Action<AppSettings?> ApplyActiveSettings,
    Action<string> SetStatus,
    Action StartRuntimeDashboardRefreshTimer,
    Func<Task> RefreshOverviewModelSelectorAsync,
    Func<Task> RefreshRuntimeMetricsAsync);

public sealed record RuntimeSessionRecoveryApplicationResult(
    bool Attempted,
    bool Recovered,
    int AttachedSessionCount,
    int SkippedSessionCount,
    LoadedModelSessionSnapshot? SelectedSession)
{
    public static RuntimeSessionRecoveryApplicationResult NoPersistedSessions { get; } = new(
        Attempted: false,
        Recovered: false,
        AttachedSessionCount: 0,
        SkippedSessionCount: 0,
        SelectedSession: null);

    public static RuntimeSessionRecoveryApplicationResult Failed { get; } = new(
        Attempted: true,
        Recovered: false,
        AttachedSessionCount: 0,
        SkippedSessionCount: 0,
        SelectedSession: null);
}

public sealed class RuntimeSessionRecoveryApplicationService
{
    private readonly LoadedModelSessionManager _sessions;
    private readonly RuntimeSessionPersistenceService _persistence;
    private readonly RuntimeSessionRecoveryService _recovery;

    public RuntimeSessionRecoveryApplicationService(
        LoadedModelSessionManager sessions,
        RuntimeSessionPersistenceService persistence,
        RuntimeSessionRecoveryService recovery)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
    }

    public async Task<RuntimeSessionRecoveryApplicationResult> RecoverAsync(
        RuntimeSessionRecoveryApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var persistedSessions = await _persistence.ReadAllAsync(cancellationToken);
        if (persistedSessions.Count == 0)
            return RuntimeSessionRecoveryApplicationResult.NoPersistedSessions;

        try
        {
            var models = await actions.ListModelsAsync();
            var runtimes = await actions.ListRuntimesAsync();
            var recovery = await _recovery.RecoverAsync(new RuntimeSessionRecoveryRequest(
                persistedSessions,
                models,
                runtimes,
                actions.ReadServedModelsAsync,
                actions.IsEndpointAliveAsync,
                actions.IsEndpointRespondingAsync,
                RuntimeSessionRecoveryService.NativeRuntimeProcessMatches),
                cancellationToken);

            foreach (var attachment in recovery.AttachedSessions.Where(attachment => attachment.NeedsReadinessMonitor))
            {
                if (attachment.WasSelected)
                    actions.StartLoadingStatus(attachment.Model, attachment.Snapshot.LaunchSettings);
                actions.StartReadinessMonitor(attachment.Model, attachment.Snapshot.LaunchSettings);
            }

            actions.ApplyActiveSettings(_sessions.ActiveSettings);
            if (!_sessions.HasRunningSessions)
            {
                _persistence.Clear();
                return new RuntimeSessionRecoveryApplicationResult(
                    Attempted: true,
                    Recovered: false,
                    AttachedSessionCount: recovery.AttachedSessions.Count,
                    SkippedSessionCount: recovery.SkippedSessionCount,
                    SelectedSession: null);
            }

            await _persistence.SaveRunningAsync(cancellationToken);
            var selected = _sessions.SelectedSnapshot();
            if (selected is not null)
            {
                actions.SetStatus($"Recovered {_sessions.Snapshots().Count} loaded model session(s). Selected {selected.ModelName} at {selected.EndpointDisplay}.");
            }

            actions.StartRuntimeDashboardRefreshTimer();
            await actions.RefreshOverviewModelSelectorAsync();
            await actions.RefreshRuntimeMetricsAsync();

            return new RuntimeSessionRecoveryApplicationResult(
                Attempted: true,
                Recovered: true,
                AttachedSessionCount: recovery.AttachedSessions.Count,
                SkippedSessionCount: recovery.SkippedSessionCount,
                SelectedSession: selected);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            _persistence.Clear();
            return RuntimeSessionRecoveryApplicationResult.Failed;
        }
    }
}
