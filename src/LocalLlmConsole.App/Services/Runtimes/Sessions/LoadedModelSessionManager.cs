namespace LocalLlmConsole.Services;

public sealed partial class LoadedModelSessionManager : IDisposable
{
    private sealed class LoadedModelSession
    {
        public required string SessionId { get; init; }
        public required ModelRecord Model { get; init; }
        public required RuntimeRecord Runtime { get; init; }
        public required AppSettings LaunchSettings { get; set; }
        public required long ModelSizeBytes { get; init; }
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
    private const int MaximumDiagnosticEventCount = 200;
    private readonly Dictionary<string, LoadedModelSession> _sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<LoadedModelSessionSnapshot> _recentlyStopped = [];
    private readonly Queue<SessionLifecycleDiagnosticEvent> _diagnosticEvents = [];
    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private readonly AsyncLocal<int> _lifecycleDepth = new();
    private readonly Func<LlamaProcessSupervisor> _createSupervisor;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly LlamaProcessSupervisor _inactiveSupervisor;
    private int _benchmarkLeaseActive;

    public LoadedModelSessionManager(
        Func<LlamaProcessSupervisor> createSupervisor,
        Func<DateTimeOffset>? utcNow = null)
    {
        _createSupervisor = createSupervisor ?? throw new ArgumentNullException(nameof(createSupervisor));
        _utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
        _inactiveSupervisor = CreateSupervisor();
    }

    private string _selectedSessionId = "";
    private bool _disposed;

    public string SelectedSessionId
    {
        get
        {
            lock (_stateLock)
                return _selectedSessionId;
        }
        private set => _selectedSessionId = value;
    }

    public LlamaProcessSupervisor ActiveSupervisor
    {
        get
        {
            lock (_stateLock)
                return !string.IsNullOrWhiteSpace(_selectedSessionId) && _sessions.TryGetValue(_selectedSessionId, out var session)
                    ? session.Supervisor
                    : _inactiveSupervisor;
        }
    }

    public AppSettings? ActiveSettings
    {
        get
        {
            lock (_stateLock)
                return !string.IsNullOrWhiteSpace(_selectedSessionId) && _sessions.TryGetValue(_selectedSessionId, out var session)
                    ? session.LaunchSettings
                    : null;
        }
    }

    public bool HasRunningSessions
    {
        get
        {
            lock (_stateLock)
                return _sessions.Values.Any(session => session.Supervisor.IsRunning);
        }
    }

    public bool HasRunningGpuSessions
    {
        get
        {
            lock (_stateLock)
                return _sessions.Values.Any(session =>
                    session.Supervisor.IsRunning
                    && session.Runtime.Backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Sycl or RuntimeBackend.Rocm);
        }
    }

    public bool HasBenchmarkLease => Volatile.Read(ref _benchmarkLeaseActive) == 1;

    public IReadOnlyList<LoadedModelSessionSnapshot> Snapshots()
    {
        lock (_stateLock)
            return SnapshotArrayLocked();
    }

    public IReadOnlyList<LoadedModelSessionSnapshot> OverviewSnapshots()
    {
        lock (_stateLock)
        {
            PruneRecentStoppedLocked();
            return SnapshotArrayLocked().Concat(_recentlyStopped)
                .OrderByDescending(snapshot => snapshot.IsSelected)
                .ThenBy(snapshot => snapshot.ModelName, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public LoadedModelSessionSnapshot? SelectedSnapshot()
        => Snapshots().FirstOrDefault(snapshot => snapshot.IsSelected)
            ?? Snapshots().FirstOrDefault();

    public LoadedModelSessionSnapshot? SessionById(string sessionId)
        => Snapshots().FirstOrDefault(snapshot =>
            string.Equals(snapshot.SessionId, sessionId, StringComparison.OrdinalIgnoreCase));

    public LoadedModelSessionSnapshot? SessionForModel(string modelId)
    {
        var sessions = SessionsForModel(modelId);
        return sessions.FirstOrDefault(snapshot => snapshot.IsSelected)
            ?? sessions.FirstOrDefault();
    }

    public IReadOnlyList<LoadedModelSessionSnapshot> SessionsForModel(string modelId)
        => Snapshots()
            .Where(snapshot => string.Equals(snapshot.ModelId, modelId, StringComparison.OrdinalIgnoreCase))
            .ToArray();

    public LoadedModelSessionSnapshot? SessionForProfile(string modelId, string launchProfileId)
        => Snapshots().FirstOrDefault(snapshot =>
            string.Equals(snapshot.ModelId, modelId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(snapshot.LaunchProfileId, launchProfileId ?? "", StringComparison.OrdinalIgnoreCase));

    public bool IsModelLoaded(string modelId)
        => SessionForModel(modelId) is { IsRunning: true };

    public bool IsModelActive(string modelId)
        => SessionForModel(modelId) is { IsRunning: true, IsSelected: true };

    public IEnumerable<int> ReservedPorts(string? exceptSessionId = null)
        => Snapshots()
            .Where(session => !string.Equals(session.SessionId, exceptSessionId, StringComparison.OrdinalIgnoreCase))
            .Select(session => session.LaunchSettings.Port)
            .Where(RuntimePortAllocator.IsValidPort)
            .Distinct()
            .ToArray();

    public async Task<T> ExecuteLifecycleAsync<T>(
        Func<Task<T>> operation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (_lifecycleDepth.Value > 0)
            return await operation();

        if (HasBenchmarkLease)
            throw new InvalidOperationException("A benchmark is in progress. Wait for it to finish or cancel it before loading or changing model sessions.");

        await _lifecycleGate.WaitAsync(cancellationToken);
        _lifecycleDepth.Value++;
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await operation();
        }
        finally
        {
            _lifecycleDepth.Value--;
            _lifecycleGate.Release();
        }
    }

    public async Task ExecuteLifecycleAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
        => await ExecuteLifecycleAsync(async () =>
        {
            await operation();
            return true;
        }, cancellationToken);

    public bool SelectSession(string sessionId)
    {
        lock (_stateLock)
        {
            if (!_sessions.ContainsKey(sessionId)) return false;
            _selectedSessionId = sessionId;
            return true;
        }
    }

    public bool SelectModel(string modelId)
    {
        lock (_stateLock)
        {
            var session = _sessions.Values.FirstOrDefault(item => string.Equals(item.Model.Id, modelId, StringComparison.OrdinalIgnoreCase));
            if (session is null) return false;
            _selectedSessionId = session.SessionId;
            return true;
        }
    }

    public bool SelectProfile(string modelId, string launchProfileId)
    {
        lock (_stateLock)
        {
            var session = _sessions.Values.FirstOrDefault(item =>
                string.Equals(item.Model.Id, modelId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(item.LaunchProfileId, launchProfileId ?? "", StringComparison.OrdinalIgnoreCase));
            if (session is null) return false;
            _selectedSessionId = session.SessionId;
            return true;
        }
    }

    public async Task StopModelAsync(string modelId)
        => await ExecuteLifecycleAsync(async () =>
        {
            string[] sessionIds;
            lock (_stateLock)
                sessionIds = _sessions.Values
                    .Where(item => string.Equals(item.Model.Id, modelId, StringComparison.OrdinalIgnoreCase))
                    .Select(item => item.SessionId)
                    .ToArray();
            foreach (var sessionId in sessionIds)
                await StopCoreAsync(sessionId, "Unloaded by user", CancellationToken.None);
        });

    public async Task StopSelectedAsync()
        => await ExecuteLifecycleAsync(async () =>
        {
            string sessionId;
            lock (_stateLock)
                sessionId = _selectedSessionId;
            if (!string.IsNullOrWhiteSpace(sessionId))
                await StopCoreAsync(sessionId, "Unloaded by user", CancellationToken.None);
        });

    public async Task StopAsync(
        string sessionId,
        string reason = "Unloaded by user",
        CancellationToken cancellationToken = default)
        => await ExecuteLifecycleAsync(
            () => StopCoreAsync(sessionId, reason, cancellationToken),
            cancellationToken);

    private async Task StopCoreAsync(
        string sessionId,
        string reason,
        CancellationToken cancellationToken)
    {
        LoadedModelSession? session;
        lock (_stateLock)
        {
            if (!_sessions.TryGetValue(sessionId, out session)) return;
            session.IsStopping = true;
            session.StatusReason = "Stopping runtime process";
            RecordEventLocked(session, ToSnapshotLocked(session).Status.ToString(), "stopping", "user", "LLWM-SESSION-STOP", "running", "not-applicable", "pending");
        }
        LlamaProcessSupervisor.StopVerification stop;
        try
        {
            stop = await session.Supervisor.StopVerifiedAsync(cancellationToken);
        }
        catch
        {
            lock (_stateLock)
            {
                if (IsCurrentSessionLocked(sessionId, session))
                {
                    session.IsStopping = false;
                    session.StatusReason = "Runtime stop was interrupted";
                    RecordEventLocked(session, "stopping", "running", "user", "LLWM-SESSION-STOP-INTERRUPTED", "running", "not-applicable", "interrupted");
                }
            }
            throw;
        }
        if (!stop.VerifiedStopped)
        {
            lock (_stateLock)
            {
                if (IsCurrentSessionLocked(sessionId, session))
                {
                    session.IsStopping = false;
                    session.EndpointHealth = RuntimeEndpointHealth.Unreachable;
                    session.StatusReason = string.IsNullOrWhiteSpace(stop.Error)
                        ? $"[{DiagnosticErrorCodes.SessionStopUnverified}] Runtime process did not stop"
                        : $"[{DiagnosticErrorCodes.SessionStopUnverified}] {stop.Error}";
                    RecordEventLocked(session, "stopping", "unreachable", "user", "LLWM-SESSION-STOP-UNVERIFIED", "unknown", "not-applicable", "failed");
                }
            }
            throw new InvalidOperationException($"Could not verify that {session.Model.Name} stopped. {session.StatusReason} Create a diagnostics bundle from Logs before retrying.");
        }

        lock (_stateLock)
        {
            if (!IsCurrentSessionLocked(sessionId, session)) return;
            _sessions.Remove(sessionId);
            AddRecentlyStoppedLocked(session, reason);
            RecordEventLocked(session, "stopping", "stopped", "user", "LLWM-SESSION-STOPPED", "expected", "not-applicable", "verified");
            if (string.Equals(_selectedSessionId, sessionId, StringComparison.OrdinalIgnoreCase))
                _selectedSessionId = _sessions.Keys.FirstOrDefault() ?? "";
        }
    }

    public async Task StopAllAsync()
        => await ExecuteLifecycleAsync(async () =>
        {
            string[] sessionIds;
            lock (_stateLock)
                sessionIds = _sessions.Keys.ToArray();
            foreach (var sessionId in sessionIds)
                await StopCoreAsync(sessionId, "Unloaded by user", CancellationToken.None);
            lock (_stateLock)
                _selectedSessionId = "";
        });

    public bool MarkLoadedIfRunning(string sessionId)
    {
        lock (_stateLock)
        {
            if (!_sessions.TryGetValue(sessionId, out var session) || !session.Supervisor.MarkLoadedIfRunning()) return false;
            RecordEventLocked(session, "loading", "running", "readiness-monitor", "LLWM-SESSION-READY", "running", "ready", "not-requested");
            return true;
        }
    }

    public bool MarkModelLoadedIfRunning(string modelId)
    {
        lock (_stateLock)
        {
            var session = _sessions.Values.FirstOrDefault(item => string.Equals(item.Model.Id, modelId, StringComparison.OrdinalIgnoreCase));
            if (session is null || !session.Supervisor.MarkLoadedIfRunning()) return false;
            RecordEventLocked(session, "loading", "running", "readiness-monitor", "LLWM-SESSION-READY", "running", "ready", "not-requested");
            return true;
        }
    }

    public int RemoveFailedOrStopped()
    {
        lock (_stateLock)
        {
            var removed = 0;
            foreach (var session in _sessions.Values.Where(session => !session.IsStopping && !session.Supervisor.IsRunning).ToArray())
            {
                _sessions.Remove(session.SessionId);
                var exit = session.Supervisor.LastExitCode is { } code
                    ? $"[{DiagnosticErrorCodes.SessionUnexpectedExit}] Runtime process exited with code {code}. Create a diagnostics bundle from Logs if this repeats."
                    : $"[{DiagnosticErrorCodes.SessionUnexpectedExit}] Runtime process is no longer running. Create a diagnostics bundle from Logs if this repeats.";
                AddRecentlyStoppedLocked(session, exit, LoadedModelSessionStatus.Failed);
                RecordEventLocked(session, ToSnapshotLocked(session).Status.ToString(), "failed", "process-supervisor", "LLWM-SESSION-UNEXPECTED-EXIT", "unexpected", "not-applicable", "not-requested");
                session.Supervisor.Dispose();
                removed++;
            }
            if (!string.IsNullOrWhiteSpace(_selectedSessionId) && !_sessions.ContainsKey(_selectedSessionId))
                _selectedSessionId = _sessions.Keys.FirstOrDefault() ?? "";
            return removed;
        }
    }

    public async Task<int> StopUnavailableRecoveredSessionsAsync(Func<LoadedModelSessionSnapshot, Task<bool>> isAvailable)
        => await ExecuteLifecycleAsync(async () =>
    {
        var removed = 0;
        LoadedModelSession[] sessions;
        lock (_stateLock)
            sessions = _sessions.Values.ToArray();
        foreach (var session in sessions)
        {
            if (!session.Supervisor.IsRecovered
                || !session.Supervisor.IsRunning
                || session.Supervisor.State is not (LlamaRuntimeState.Loading or LlamaRuntimeState.Loaded))
                continue;

            LoadedModelSessionSnapshot snapshot;
            lock (_stateLock)
            {
                if (!IsCurrentSessionLocked(session.SessionId, session)) continue;
                snapshot = ToSnapshotLocked(session);
            }
            if (await isAvailable(snapshot)) continue;

            await StopCoreAsync(session.SessionId, "Recovered runtime endpoint was unavailable.", CancellationToken.None);
            removed++;
        }
        return removed;
    });

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var session in _sessions.Values)
                session.Supervisor.Dispose();
            _sessions.Clear();
            _recentlyStopped.Clear();
            _diagnosticEvents.Clear();
            _inactiveSupervisor.Dispose();
        }
        _lifecycleGate.Dispose();
    }

}
