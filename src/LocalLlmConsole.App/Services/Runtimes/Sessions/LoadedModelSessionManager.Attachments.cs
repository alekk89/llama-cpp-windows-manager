namespace LocalLlmConsole.Services;

public sealed partial class LoadedModelSessionManager
{
    public LoadedModelSessionSnapshot AttachExisting(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings settings,
        string logPath,
        LlamaRuntimeState state,
        string processMarker,
        string sessionId,
        DateTimeOffset startedAt,
        int processId = 0,
        string launchProfileId = "",
        string launchProfileName = "")
    {
        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? SessionIdFor(model.Id, launchProfileId) : sessionId;
        var supervisor = CreateSupervisor();
        supervisor.AttachExisting(runtime, model.Id, settings, logPath, state, processMarker, processId);
        var session = new LoadedModelSession
        {
            SessionId = resolvedSessionId,
            Model = model,
            Runtime = runtime,
            LaunchSettings = settings,
            ModelSizeBytes = ReadModelSizeBytes(model.ModelPath),
            StartedAt = startedAt,
            Supervisor = supervisor,
            LaunchProfileId = launchProfileId ?? "",
            LaunchProfileName = launchProfileName ?? "",
            EndpointHealth = RuntimeEndpointHealth.Healthy
        };
        lock (_stateLock)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_sessions.Remove(resolvedSessionId, out var replaced))
                replaced.Supervisor.Dispose();
            _sessions[resolvedSessionId] = session;
            if (string.IsNullOrWhiteSpace(_selectedSessionId))
                _selectedSessionId = resolvedSessionId;
            RecordEventLocked(session, "detached", state.ToString(), "recovery", "LLWM-SESSION-RECOVERED", "running", "recovered", "not-requested");
            return ToSnapshotLocked(session);
        }
    }
}
