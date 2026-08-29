namespace LocalLlmConsole.Services;

public sealed partial class LoadedModelSessionManager
{
    public IReadOnlyList<SessionLifecycleDiagnosticEvent> DiagnosticEvents()
    {
        lock (_stateLock)
            return _diagnosticEvents.ToArray();
    }

    private void RecordEventLocked(
        LoadedModelSession session,
        string previousState,
        string newState,
        string actor,
        string reasonCode,
        string processExitCategory,
        string readinessResult,
        string stopVerificationResult)
    {
        _diagnosticEvents.Enqueue(new SessionLifecycleDiagnosticEvent(
            session.SessionId,
            session.Model.Id,
            session.Runtime.Id,
            previousState,
            newState,
            _utcNow(),
            actor,
            reasonCode,
            processExitCategory,
            readinessResult,
            stopVerificationResult));
        while (_diagnosticEvents.Count > MaximumDiagnosticEventCount)
            _diagnosticEvents.Dequeue();
    }
}
