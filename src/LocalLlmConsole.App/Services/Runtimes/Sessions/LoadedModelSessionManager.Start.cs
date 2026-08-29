namespace LocalLlmConsole.Services;

public sealed partial class LoadedModelSessionManager
{
    public async Task<LoadedModelSessionSnapshot> StartAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings settings,
        string logRoot,
        string launchProfileId = "",
        string launchProfileName = "")
        => await ExecuteLifecycleAsync(() => StartCoreAsync(
            runtime, model, settings, logRoot, launchProfileId, launchProfileName));

    private async Task<LoadedModelSessionSnapshot> StartCoreAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings settings,
        string logRoot,
        string launchProfileId = "",
        string launchProfileName = "")
    {
        var sessionId = SessionIdFor(model.Id);
        await StopCoreAsync(sessionId, "Replaced by a new launch", CancellationToken.None);
        var supervisor = CreateSupervisor();
        try
        {
            await supervisor.StartAsync(runtime, model, settings, logRoot);
        }
        catch
        {
            supervisor.Dispose();
            throw;
        }
        var session = new LoadedModelSession
        {
            SessionId = sessionId,
            Model = model,
            Runtime = runtime,
            LaunchSettings = settings,
            ModelSizeBytes = ReadModelSizeBytes(model.ModelPath),
            StartedAt = _utcNow(),
            Supervisor = supervisor,
            LaunchProfileId = launchProfileId ?? "",
            LaunchProfileName = launchProfileName ?? ""
        };
        lock (_stateLock)
        {
            _sessions[sessionId] = session;
            _selectedSessionId = sessionId;
            RecordEventLocked(session, "stopped", "loading", "user", "LLWM-SESSION-START", "running", "pending", "not-requested");
            return ToSnapshotLocked(session);
        }
    }
}
