namespace LocalLlmConsole.Services;

public sealed record RuntimeSessionStopResult(
    LoadedModelSessionSnapshot? StoppedSession,
    AppSettings? ActiveSettings);

public sealed record RuntimeSessionSelectResult(
    bool Selected,
    AppSettings? ActiveSettings);

public sealed class RuntimeSessionCoordinator
{
    private readonly LoadedModelSessionManager _sessions;
    private readonly string _logRoot;

    public RuntimeSessionCoordinator(LoadedModelSessionManager sessions, string logRoot)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
        _logRoot = string.IsNullOrWhiteSpace(logRoot)
            ? throw new ArgumentException("Runtime log root is required.", nameof(logRoot))
            : logRoot;
    }

    public LoadedModelSessionManager Sessions => _sessions;

    public void EnsureLaunchPortAvailable(
        string modelId,
        AppSettings launchSettings,
        bool autoLoadGatewayEnabled,
        int autoLoadGatewayPort)
    {
        var sessionId = LoadedModelSessionManager.SessionIdFor(modelId);
        if (_sessions.ReservedPorts(sessionId).Contains(launchSettings.Port))
            throw new InvalidOperationException($"Port {launchSettings.Port} is already assigned to another loaded model. Set a unique model port next to the runtime before launching.");
        if (autoLoadGatewayEnabled && launchSettings.Port == autoLoadGatewayPort)
            throw new InvalidOperationException($"Port {launchSettings.Port} is reserved for the auto-load gateway. Choose a different model port.");
    }

    public async Task<LoadedModelSessionSnapshot> StartAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings launchSettings,
        string launchProfileId = "",
        string launchProfileName = "")
        => await _sessions.StartAsync(runtime, model, launchSettings, _logRoot, launchProfileId, launchProfileName);

    public async Task<RuntimeSessionStopResult> StopSelectedAsync()
    {
        var stoppedSession = _sessions.SelectedSnapshot();
        await _sessions.StopSelectedAsync();
        return new RuntimeSessionStopResult(stoppedSession, _sessions.ActiveSettings);
    }

    public async Task<RuntimeSessionStopResult> StopModelAsync(string modelId)
    {
        var stoppedSession = _sessions.SessionForModel(modelId);
        await _sessions.StopModelAsync(modelId);
        return new RuntimeSessionStopResult(stoppedSession, _sessions.ActiveSettings);
    }

    public RuntimeSessionSelectResult SelectModel(string modelId)
    {
        var selected = _sessions.SelectModel(modelId);
        return new RuntimeSessionSelectResult(selected, _sessions.ActiveSettings);
    }

    public RuntimeSessionSelectResult SelectSession(string sessionId)
    {
        var selected = _sessions.SelectSession(sessionId);
        return new RuntimeSessionSelectResult(selected, _sessions.ActiveSettings);
    }

    public async Task StopAllAsync()
        => await _sessions.StopAllAsync();
}
