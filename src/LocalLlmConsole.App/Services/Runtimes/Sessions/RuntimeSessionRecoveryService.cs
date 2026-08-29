namespace LocalLlmConsole.Services;

public delegate Task<IReadOnlyList<string>> RuntimeServedModelsReader(AppSettings launchSettings, CancellationToken cancellationToken);

public delegate Task<bool> RuntimeEndpointStatusProbe(AppSettings launchSettings, CancellationToken cancellationToken);

public delegate bool RuntimeNativeProcessMatcher(ActiveRuntimeSession session, RuntimeRecord runtime);

public sealed record RuntimeSessionRecoveryRequest(
    IReadOnlyList<ActiveRuntimeSession> PersistedSessions,
    IReadOnlyList<ModelRecord> Models,
    IReadOnlyList<RuntimeRecord> Runtimes,
    RuntimeServedModelsReader ReadServedModelsAsync,
    RuntimeEndpointStatusProbe IsEndpointAliveAsync,
    RuntimeEndpointStatusProbe IsEndpointRespondingAsync,
    RuntimeNativeProcessMatcher NativeProcessMatches);

public sealed record RuntimeSessionRecoveryAttachment(
    LoadedModelSessionSnapshot Snapshot,
    ModelRecord Model,
    LlamaRuntimeState State,
    bool WasSelected)
{
    public bool NeedsReadinessMonitor => State == LlamaRuntimeState.Loading;
}

public sealed record RuntimeSessionRecoveryResult(
    IReadOnlyList<RuntimeSessionRecoveryAttachment> AttachedSessions,
    int SkippedSessionCount);

public sealed class RuntimeSessionRecoveryService
{
    private readonly LoadedModelSessionManager _sessions;

    public RuntimeSessionRecoveryService(LoadedModelSessionManager sessions)
    {
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public async Task<RuntimeSessionRecoveryResult> RecoverAsync(
        RuntimeSessionRecoveryRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ReadServedModelsAsync);
        ArgumentNullException.ThrowIfNull(request.IsEndpointAliveAsync);
        ArgumentNullException.ThrowIfNull(request.IsEndpointRespondingAsync);
        ArgumentNullException.ThrowIfNull(request.NativeProcessMatches);

        var models = request.Models.ToDictionary(model => model.Id, StringComparer.OrdinalIgnoreCase);
        var runtimes = request.Runtimes.ToDictionary(runtime => runtime.Id, StringComparer.OrdinalIgnoreCase);
        var attached = new List<RuntimeSessionRecoveryAttachment>();
        var skipped = 0;

        foreach (var session in request.PersistedSessions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await TryAttachAsync(session, models, runtimes, request, attached, cancellationToken))
                skipped++;
        }

        return new RuntimeSessionRecoveryResult(attached, skipped);
    }

    public static bool NativeRuntimeProcessMatches(ActiveRuntimeSession session, RuntimeRecord runtime)
    {
        if (session.ProcessId <= 0) return false;
        try
        {
            using var process = Process.GetProcessById(session.ProcessId);
            if (process.HasExited) return false;
            var processPath = process.MainModule?.FileName;
            if (string.IsNullOrWhiteSpace(processPath)) return false;
            return string.Equals(Path.GetFullPath(processPath), Path.GetFullPath(runtime.ExecutablePath), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> TryAttachAsync(
        ActiveRuntimeSession session,
        IReadOnlyDictionary<string, ModelRecord> models,
        IReadOnlyDictionary<string, RuntimeRecord> runtimes,
        RuntimeSessionRecoveryRequest request,
        ICollection<RuntimeSessionRecoveryAttachment> attached,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(session.ModelId) || string.IsNullOrWhiteSpace(session.RuntimeId))
            return false;
        if (!models.TryGetValue(session.ModelId, out var model) || !runtimes.TryGetValue(session.RuntimeId, out var runtime))
            return false;
        if (runtime.Mode == RuntimeMode.Wsl && string.IsNullOrWhiteSpace(session.ProcessMarker))
            return false;
        if (runtime.Mode == RuntimeMode.Native && !request.NativeProcessMatches(session, runtime))
            return false;

        var servedModels = await request.ReadServedModelsAsync(session.LaunchSettings, cancellationToken);
        if (servedModels.Count > 0 && !servedModels.Any(served => RuntimeEndpointService.ServedModelMatches(model, served)))
            return false;

        var alive = await request.IsEndpointAliveAsync(session.LaunchSettings, cancellationToken);
        if (!alive && !await request.IsEndpointRespondingAsync(session.LaunchSettings, cancellationToken))
            return false;

        var state = alive ? LlamaRuntimeState.Loaded : LlamaRuntimeState.Loading;
        var snapshot = _sessions.AttachExisting(
            runtime,
            model,
            session.LaunchSettings,
            session.LogPath,
            state,
            session.ProcessMarker,
            session.SessionId,
            session.StartedAt,
            session.ProcessId,
            session.LaunchProfileId,
            session.LaunchProfileName);
        if (session.IsSelected)
            _sessions.SelectSession(snapshot.SessionId);
        attached.Add(new RuntimeSessionRecoveryAttachment(snapshot, model, state, session.IsSelected));
        return true;
    }
}
