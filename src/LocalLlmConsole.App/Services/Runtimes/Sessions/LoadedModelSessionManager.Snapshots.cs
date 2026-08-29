namespace LocalLlmConsole.Services;

public sealed partial class LoadedModelSessionManager
{
    public IReadOnlyList<RuntimeSessionHealthTransition> ApplyEndpointHealth(
        IEnumerable<RuntimeMetricPollResult> pollResults)
    {
        ArgumentNullException.ThrowIfNull(pollResults);
        lock (_stateLock)
        {
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
                {
                    transitions.Add(new RuntimeSessionHealthTransition(
                        session.SessionId,
                        session.Model.Id,
                        session.Model.Name,
                        previous,
                        session.EndpointHealth,
                        session.StatusReason));
                    RecordEventLocked(
                        session,
                        previous.ToString(),
                        session.EndpointHealth.ToString(),
                        "health-monitor",
                        session.EndpointHealth == RuntimeEndpointHealth.Healthy ? "LLWM-ENDPOINT-RECOVERED" : "LLWM-ENDPOINT-UNREACHABLE",
                        "running",
                        result.EndpointResponded ? "ready" : "unreachable",
                        "not-requested");
                }
            }
            return transitions;
        }
    }

    public static string SessionIdFor(string modelId)
        => ModelCatalogService.SafeId($"session-{modelId}");

    private LlamaProcessSupervisor CreateSupervisor()
        => _createSupervisor() ?? throw new InvalidOperationException("Supervisor factory returned no supervisor.");

    private LoadedModelSessionSnapshot[] SnapshotArrayLocked()
        => _sessions.Values
            .Select(ToSnapshotLocked)
            .OrderByDescending(snapshot => snapshot.IsSelected)
            .ThenBy(snapshot => snapshot.ModelName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private bool IsCurrentSessionLocked(string sessionId, LoadedModelSession session)
        => _sessions.TryGetValue(sessionId, out var current) && ReferenceEquals(current, session);

    private LoadedModelSessionSnapshot ToSnapshotLocked(LoadedModelSession session)
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
            string.Equals(session.SessionId, _selectedSessionId, StringComparison.OrdinalIgnoreCase),
            session.ModelSizeBytes,
            session.LaunchProfileId,
            session.LaunchProfileName,
            session.EndpointHealth,
            session.ConsecutiveEndpointFailures,
            session.StatusReason);
    }

    private void AddRecentlyStoppedLocked(
        LoadedModelSession session,
        string reason,
        LoadedModelSessionStatus status = LoadedModelSessionStatus.Stopped)
    {
        var snapshot = ToSnapshotLocked(session) with
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
        PruneRecentStoppedLocked();
    }

    private void PruneRecentStoppedLocked()
    {
        var cutoff = _utcNow() - RecentStoppedLifetime;
        _recentlyStopped.RemoveAll(snapshot => snapshot.StoppedAt is null || snapshot.StoppedAt < cutoff);
    }
}

public sealed record RuntimeSessionHealthTransition(
    string SessionId,
    string ModelId,
    string ModelName,
    RuntimeEndpointHealth Previous,
    RuntimeEndpointHealth Current,
    string Reason);
