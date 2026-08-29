namespace LocalLlmConsole.Services;

public sealed record RuntimeSessionReconciliationApplicationActions(
    Func<LoadedModelSessionSnapshot, Task<bool>> RecoveredSessionAvailableAsync,
    Func<LoadedModelSessionSnapshot, Task<bool>> LoadingSessionReadyAsync,
    Action<AppSettings?> ApplyActiveSettings,
    Action<RuntimeSessionLoadedTransition> ApplyLoadedTransition,
    Action RefreshOverviewSessionRows,
    Action UpdateOverviewModelActions);

public sealed class RuntimeSessionReconciliationApplicationService
{
    private readonly LoadedModelSessionManager _sessions;
    private readonly RuntimeSessionPersistenceService _persistence;
    private readonly RuntimeSessionReconciler _reconciler;

    public RuntimeSessionReconciliationApplicationService(
        LoadedModelSessionManager sessions,
        RuntimeSessionPersistenceService persistence,
        RuntimeSessionReconciler reconciler)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _reconciler = reconciler ?? throw new ArgumentNullException(nameof(reconciler));
    }

    public async Task<RuntimeSessionReconcileResult> ReconcileAsync(
        RuntimeSessionReconciliationApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);

        var result = await _reconciler.ReconcileAsync(
            _sessions,
            actions.RecoveredSessionAvailableAsync,
            actions.LoadingSessionReadyAsync);
        if (!result.HasChanges)
            return result;

        actions.ApplyActiveSettings(_sessions.ActiveSettings);
        foreach (var transition in result.LoadedTransitions)
            actions.ApplyLoadedTransition(transition);

        await _persistence.SaveRunningAsync(cancellationToken);
        actions.RefreshOverviewSessionRows();
        actions.UpdateOverviewModelActions();
        return result;
    }
}
