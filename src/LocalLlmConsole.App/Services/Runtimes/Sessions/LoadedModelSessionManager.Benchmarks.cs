namespace LocalLlmConsole.Services;

public sealed partial class LoadedModelSessionManager
{
    public async Task<BenchmarkSessionLease> AcquireBenchmarkLeaseAsync(
        bool stopActiveSessions,
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            string[] activeSessionIds;
            lock (_stateLock)
                activeSessionIds = _sessions.Values.Where(session => session.Supervisor.IsRunning).Select(session => session.SessionId).ToArray();
            if (activeSessionIds.Length > 0 && !stopActiveSessions)
                throw new InvalidOperationException($"Benchmarking requires all model sessions to be stopped. {activeSessionIds.Length} session(s) are currently running.");
            foreach (var sessionId in activeSessionIds)
                await StopCoreAsync(sessionId, "Stopped for an explicitly confirmed benchmark", CancellationToken.None);
            if (Interlocked.CompareExchange(ref _benchmarkLeaseActive, 1, 0) != 0)
                throw new InvalidOperationException("Another benchmark already owns the machine.");
            return new BenchmarkSessionLease(this);
        }
        catch
        {
            _lifecycleGate.Release();
            throw;
        }
    }

    public sealed class BenchmarkSessionLease : IAsyncDisposable
    {
        private LoadedModelSessionManager? _owner;
        private readonly HashSet<string> _ownedSessionIds = new(StringComparer.OrdinalIgnoreCase);

        internal BenchmarkSessionLease(LoadedModelSessionManager owner) => _owner = owner;

        public async Task<LoadedModelSessionSnapshot> StartAsync(
            RuntimeRecord runtime,
            ModelRecord model,
            AppSettings settings,
            string logRoot,
            string launchProfileId,
            string launchProfileName)
        {
            var owner = _owner ?? throw new ObjectDisposedException(nameof(BenchmarkSessionLease));
            var session = await owner.StartCoreAsync(runtime, model, settings, logRoot, launchProfileId, launchProfileName);
            _ownedSessionIds.Add(session.SessionId);
            return session;
        }

        public async Task StopAsync(string sessionId, CancellationToken cancellationToken = default)
        {
            var owner = _owner ?? throw new ObjectDisposedException(nameof(BenchmarkSessionLease));
            if (!_ownedSessionIds.Remove(sessionId)) return;
            await owner.StopCoreAsync(sessionId, "Benchmark workload completed", cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is null) return;
            try
            {
                foreach (var sessionId in _ownedSessionIds.ToArray())
                {
                    try { await owner.StopCoreAsync(sessionId, "Benchmark lease released", CancellationToken.None); }
                    catch (Exception ex) { Trace.TraceError($"Could not stop benchmark-owned session {sessionId}: {ex}"); }
                }
                _ownedSessionIds.Clear();
            }
            finally
            {
                Volatile.Write(ref owner._benchmarkLeaseActive, 0);
                owner._lifecycleGate.Release();
            }
        }
    }
}
