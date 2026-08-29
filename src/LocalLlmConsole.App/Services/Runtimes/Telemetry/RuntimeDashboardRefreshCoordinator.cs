namespace LocalLlmConsole.Services;

public sealed record RuntimeDashboardRefreshTarget(
    bool HasRunningSessions,
    bool HasModelMetric,
    bool HasMetricsGrid,
    bool HasLogBox)
{
    public bool HasAnyTarget => HasRunningSessions || HasModelMetric || HasMetricsGrid || HasLogBox;
}

public sealed class RuntimeDashboardRefreshCoordinator
{
    private readonly RefreshGate _refreshGate = new();

    public bool ShouldRunTimer(string currentPage, bool hasRunningSessions)
        => string.Equals(currentPage, "Overview", StringComparison.Ordinal) || hasRunningSessions;

    public IDisposable? TryBeginRefresh(RuntimeDashboardRefreshTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (!target.HasAnyTarget || !_refreshGate.TryBegin())
            return null;

        return new RefreshLease(_refreshGate.Complete);
    }

    public LoadedModelSessionSnapshot[] PollableSessions(IEnumerable<LoadedModelSessionSnapshot> sessions)
    {
        ArgumentNullException.ThrowIfNull(sessions);

        return sessions
            // An unreachable process must remain pollable so a transient endpoint stall can
            // produce a later successful sample and restore the session to Healthy.
            .Where(session => session is
            {
                IsRunning: true,
                Status: LoadedModelSessionStatus.Running
                    or LoadedModelSessionStatus.Warm
                    or LoadedModelSessionStatus.Unreachable
            })
            .ToArray();
    }

    private sealed class RefreshLease : IDisposable
    {
        private Action? _release;

        public RefreshLease(Action release)
        {
            _release = release ?? throw new ArgumentNullException(nameof(release));
        }

        public void Dispose()
        {
            var release = Interlocked.Exchange(ref _release, null);
            release?.Invoke();
        }
    }
}
