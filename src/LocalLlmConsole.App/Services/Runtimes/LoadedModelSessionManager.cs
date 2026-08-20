namespace LocalLlmConsole.Services;

public sealed class LoadedModelSessionManager : IDisposable
{
    private sealed class LoadedModelSession
    {
        public required string SessionId { get; init; }
        public required ModelRecord Model { get; init; }
        public required RuntimeRecord Runtime { get; init; }
        public required AppSettings LaunchSettings { get; set; }
        public required DateTimeOffset StartedAt { get; set; }
        public required LlamaProcessSupervisor Supervisor { get; init; }
        public string LaunchProfileId { get; init; } = "";
        public string LaunchProfileName { get; init; } = "";
        public RuntimeEndpointHealth EndpointHealth { get; set; }
        public int ConsecutiveEndpointFailures { get; set; }
        public string StatusReason { get; set; } = "";
        public bool IsStopping { get; set; }
    }

    private static readonly TimeSpan RecentStoppedLifetime = TimeSpan.FromSeconds(2);
    private const int EndpointFailureThreshold = 3;
    private readonly Dictionary<string, LoadedModelSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadedModelSessionSnapshot> _recentlyStopped = [];
    private readonly Func<LlamaProcessSupervisor> _createSupervisor;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly LlamaProcessSupervisor _inactiveSupervisor;

    public LoadedModelSessionManager(
        Func<LlamaProcessSupervisor> createSupervisor,
        Func<DateTimeOffset>? utcNow = null)
    {
        _createSupervisor = createSupervisor ?? throw new ArgumentNullException(nameof(createSupervisor));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _inactiveSupervisor = CreateSupervisor();
    }

    public string SelectedSessionId { get; private set; } = "";

    public LlamaProcessSupervisor ActiveSupervisor
        => !string.IsNullOrWhiteSpace(SelectedSessionId) && _sessions.TryGetValue(SelectedSessionId, out var session)
            ? session.Supervisor
            : _inactiveSupervisor;

    public AppSettings? ActiveSettings
        => !string.IsNullOrWhiteSpace(SelectedSessionId) && _sessions.TryGetValue(SelectedSessionId, out var session)
            ? session.LaunchSettings
            : null;

    public bool HasRunningSessions => _sessions.Values.Any(session => session.Supervisor.IsRunning);

    public bool HasRunningGpuSessions => _sessions.Values.Any(session =>
        session.Supervisor.IsRunning
        && session.Runtime.Backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Sycl);

    public IReadOnlyList<LoadedModelSessionSnapshot> Snapshots()
        => _sessions.Values
            .Select(ToSnapshot)
            .OrderByDescending(snapshot => snapshot.IsSelected)
            .ThenBy(snapshot => snapshot.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<LoadedModelSessionSnapshot> OverviewSnapshots()
    {
        PruneRecentStopped();
        return Snapshots().Concat(_recentlyStopped)
            .OrderByDescending(snapshot => snapshot.IsSelected)
            .ThenBy(snapshot => snapshot.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public LoadedModelSessionSnapshot? SelectedSnapshot()
        => Snapshots().FirstOrDefault(snapshot => snapshot.IsSelected)
            ?? Snapshots().FirstOrDefault();

    public LoadedModelSessionSnapshot? SessionForModel(string modelId)
        => Snapshots().FirstOrDefault(snapshot => string.Equals(snapshot.ModelId, modelId, StringComparison.OrdinalIgnoreCase));

    public bool IsModelLoaded(string modelId)
        => SessionForModel(modelId) is { IsRunning: true };

    public bool IsModelActive(string modelId)
        => SessionForModel(modelId) is { IsRunning: true, IsSelected: true };

    public IEnumerable<int> ReservedPorts(string? exceptSessionId = null)
        => _sessions.Values
            .Where(session => !string.Equals(session.SessionId, exceptSessionId, StringComparison.OrdinalIgnoreCase))
            .Select(session => session.LaunchSettings.Port)
            .Where(RuntimePortAllocator.IsValidPort)
            .Distinct();

    public async Task<LoadedModelSessionSnapshot> StartAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings settings,
        string logRoot,
        string launchProfileId = "",
        string launchProfileName = "")
    {
        var sessionId = SessionIdFor(model.Id);
        await StopAsync(sessionId);
        var supervisor = CreateSupervisor();
        await supervisor.StartAsync(runtime, model, settings, logRoot);
        var session = new LoadedModelSession
        {
            SessionId = sessionId,
            Model = model,
            Runtime = runtime,
            LaunchSettings = settings,
            StartedAt = _utcNow(),
            Supervisor = supervisor,
            LaunchProfileId = launchProfileId ?? "",
            LaunchProfileName = launchProfileName ?? ""
        };
        _sessions[sessionId] = session;
        SelectedSessionId = sessionId;
        return ToSnapshot(session);
    }

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
        var resolvedSessionId = string.IsNullOrWhiteSpace(sessionId) ? SessionIdFor(model.Id) : sessionId;
        var supervisor = CreateSupervisor();
        supervisor.AttachExisting(runtime, model.Id, settings, logPath, state, processMarker, processId);
        var session = new LoadedModelSession
        {
            SessionId = resolvedSessionId,
            Model = model,
            Runtime = runtime,
            LaunchSettings = settings,
            StartedAt = startedAt,
            Supervisor = supervisor,
            LaunchProfileId = launchProfileId ?? "",
            LaunchProfileName = launchProfileName ?? "",
            EndpointHealth = RuntimeEndpointHealth.Healthy
        };
        _sessions[resolvedSessionId] = session;
        if (string.IsNullOrWhiteSpace(SelectedSessionId))
            SelectedSessionId = resolvedSessionId;
        return ToSnapshot(session);
    }

    public bool SelectSession(string sessionId)
    {
        if (!_sessions.ContainsKey(sessionId)) return false;
        SelectedSessionId = sessionId;
        return true;
    }

    public bool SelectModel(string modelId)
    {
        var session = _sessions.Values.FirstOrDefault(item => string.Equals(item.Model.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (session is null) return false;
        SelectedSessionId = session.SessionId;
        return true;
    }

    public async Task StopModelAsync(string modelId)
    {
        var session = _sessions.Values.FirstOrDefault(item => string.Equals(item.Model.Id, modelId, StringComparison.OrdinalIgnoreCase));
        if (session is not null)
            await StopAsync(session.SessionId);
    }

    public async Task StopSelectedAsync()
    {
        if (!string.IsNullOrWhiteSpace(SelectedSessionId))
            await StopAsync(SelectedSessionId);
    }

    public async Task StopAsync(
        string sessionId,
        string reason = "Unloaded by user",
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(sessionId, out var session)) return;
        session.IsStopping = true;
        session.StatusReason = "Stopping runtime process";
        LlamaProcessSupervisor.StopVerification stop;
        try
        {
            stop = await session.Supervisor.StopVerifiedAsync(cancellationToken);
        }
        catch
        {
            session.IsStopping = false;
            session.StatusReason = "Runtime stop was interrupted";
            throw;
        }
        if (!stop.VerifiedStopped)
        {
            session.IsStopping = false;
            session.EndpointHealth = RuntimeEndpointHealth.Unreachable;
            session.StatusReason = string.IsNullOrWhiteSpace(stop.Error)
                ? "Runtime process did not stop"
                : stop.Error;
            throw new InvalidOperationException($"Could not verify that {session.Model.Name} stopped. {session.StatusReason}");
        }

        _sessions.Remove(sessionId);
        AddRecentlyStopped(session, reason);
        if (string.Equals(SelectedSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
            SelectedSessionId = _sessions.Keys.FirstOrDefault() ?? "";
    }

    public async Task StopAllAsync()
    {
        foreach (var sessionId in _sessions.Keys.ToArray())
            await StopAsync(sessionId);
        SelectedSessionId = "";
    }

    public bool MarkLoadedIfRunning(string sessionId)
        => _sessions.TryGetValue(sessionId, out var session) && session.Supervisor.MarkLoadedIfRunning();

    public bool MarkModelLoadedIfRunning(string modelId)
    {
        var session = _sessions.Values.FirstOrDefault(item => string.Equals(item.Model.Id, modelId, StringComparison.OrdinalIgnoreCase));
        return session is not null && session.Supervisor.MarkLoadedIfRunning();
    }

    public int RemoveFailedOrStopped()
    {
        var removed = 0;
        foreach (var session in _sessions.Values.Where(session => !session.Supervisor.IsRunning).ToArray())
        {
            _sessions.Remove(session.SessionId);
            var exit = session.Supervisor.LastExitCode is { } code
                ? $"Runtime process exited with code {code}."
                : "Runtime process is no longer running.";
            AddRecentlyStopped(session, exit, LoadedModelSessionStatus.Failed);
            session.Supervisor.Dispose();
            removed++;
        }
        if (!string.IsNullOrWhiteSpace(SelectedSessionId) && !_sessions.ContainsKey(SelectedSessionId))
            SelectedSessionId = _sessions.Keys.FirstOrDefault() ?? "";
        return removed;
    }

    public async Task<int> StopUnavailableRecoveredSessionsAsync(Func<LoadedModelSessionSnapshot, Task<bool>> isAvailable)
    {
        var removed = 0;
        foreach (var session in _sessions.Values.ToArray())
        {
            if (!session.Supervisor.IsRecovered
                || !session.Supervisor.IsRunning
                || session.Supervisor.State is not (LlamaRuntimeState.Loading or LlamaRuntimeState.Loaded))
                continue;

            if (await isAvailable(ToSnapshot(session))) continue;

            await StopAsync(session.SessionId, "Recovered runtime endpoint was unavailable.");
            removed++;
        }
        return removed;
    }

    public void Dispose()
    {
        foreach (var session in _sessions.Values)
            session.Supervisor.Dispose();
        _sessions.Clear();
        _recentlyStopped.Clear();
        _inactiveSupervisor.Dispose();
    }

    public IReadOnlyList<RuntimeSessionHealthTransition> ApplyEndpointHealth(
        IEnumerable<RuntimeMetricPollResult> pollResults)
    {
        ArgumentNullException.ThrowIfNull(pollResults);
        var transitions = new List<RuntimeSessionHealthTransition>();
        foreach (var result in pollResults)
        {
            if (!_sessions.TryGetValue(result.Session.SessionId, out var session) || !session.Supervisor.IsRunning)
                continue;

            var previous = session.EndpointHealth;
            if (result.EndpointResponded)
            {
                session.ConsecutiveEndpointFailures = 0;
                session.EndpointHealth = RuntimeEndpointHealth.Healthy;
                session.StatusReason = "";
            }
            else
            {
                session.ConsecutiveEndpointFailures++;
                session.StatusReason = string.IsNullOrWhiteSpace(result.Error)
                    ? "Runtime endpoint did not respond."
                    : result.Error;
                if (session.ConsecutiveEndpointFailures >= EndpointFailureThreshold)
                    session.EndpointHealth = RuntimeEndpointHealth.Unreachable;
            }

            if (previous != session.EndpointHealth)
                transitions.Add(new RuntimeSessionHealthTransition(
                    session.SessionId,
                    session.Model.Id,
                    session.Model.Name,
                    previous,
                    session.EndpointHealth,
                    session.StatusReason));
        }

        return transitions;
    }

    public static string SessionIdFor(string modelId)
        => ModelCatalogService.SafeId($"session-{modelId}");

    private LlamaProcessSupervisor CreateSupervisor()
        => _createSupervisor() ?? throw new InvalidOperationException("Supervisor factory returned no supervisor.");

    private LoadedModelSessionSnapshot ToSnapshot(LoadedModelSession session)
    {
        var state = session.Supervisor.State;
        var status = session.IsStopping
            ? LoadedModelSessionStatus.Stopping
            : session.EndpointHealth == RuntimeEndpointHealth.Unreachable && session.Supervisor.IsRunning
                ? LoadedModelSessionStatus.Unreachable
                : state switch
                {
                    LlamaRuntimeState.Loading => LoadedModelSessionStatus.Loading,
                    LlamaRuntimeState.Loaded => LoadedModelSessionStatus.Running,
                    LlamaRuntimeState.Failed => LoadedModelSessionStatus.Failed,
                    _ => LoadedModelSessionStatus.Stopped
                };
        return new LoadedModelSessionSnapshot(
            session.SessionId,
            session.Model.Id,
            session.Model.Name,
            session.Runtime.Id,
            session.Runtime.Name,
            session.Runtime.Mode,
            session.Runtime.Backend,
            session.LaunchSettings,
            session.Supervisor.LogPath,
            session.StartedAt,
            session.Supervisor.WslProcessMarker,
            session.Supervisor.ProcessId,
            status,
            session.Supervisor.IsRunning,
            string.Equals(session.SessionId, SelectedSessionId, StringComparison.OrdinalIgnoreCase),
            ModelSizeBytes(session.Model.ModelPath),
            session.LaunchProfileId,
            session.LaunchProfileName,
            session.EndpointHealth,
            session.ConsecutiveEndpointFailures,
            session.StatusReason);
    }

    private void AddRecentlyStopped(
        LoadedModelSession session,
        string reason,
        LoadedModelSessionStatus status = LoadedModelSessionStatus.Stopped)
    {
        var snapshot = ToSnapshot(session) with
        {
            Status = status,
            IsRunning = false,
            IsSelected = false,
            EndpointHealth = RuntimeEndpointHealth.Unreachable,
            StatusReason = reason,
            StoppedAt = _utcNow()
        };
        _recentlyStopped.RemoveAll(item => string.Equals(item.SessionId, session.SessionId, StringComparison.OrdinalIgnoreCase));
        _recentlyStopped.Add(snapshot);
        PruneRecentStopped();
    }

    private void PruneRecentStopped()
    {
        var cutoff = _utcNow() - RecentStoppedLifetime;
        _recentlyStopped.RemoveAll(snapshot => snapshot.StoppedAt is null || snapshot.StoppedAt < cutoff);
    }

    private static long ModelSizeBytes(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }
}

public sealed record RuntimeSessionHealthTransition(
    string SessionId,
    string ModelId,
    string ModelName,
    RuntimeEndpointHealth Previous,
    RuntimeEndpointHealth Current,
    string Reason);
