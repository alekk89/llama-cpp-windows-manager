namespace LocalLlmConsole.Services;

// SessionMayHaveStopped is true when the request waited for, or raced with,
// lifecycle work; the caller must re-check that its session is still running.
public readonly record struct ModelGatewayUpstreamEntry(IDisposable Lease, bool SessionMayHaveStopped);

// Tracks gateway requests that are actively exchanging data with a model
// session, and lets lifecycle work (Single active swaps, idle unload) keep new
// requests out of a session while it waits for current responses to finish.
// Requests still waiting for a model load are not counted, so a swap never
// waits on a request that is itself waiting for the load gate.
public sealed class ModelGatewayUpstreamActivity
{
    private readonly object _sync = new();
    private readonly Dictionary<(string ModelId, string ProfileId), int> _active = new(SessionKeyComparer.Instance);
    private readonly List<Block> _blocks = [];
    private TaskCompletionSource _changed = NewSignal();

    private long _blockGeneration;

    // Changes whenever lifecycle work blocks any session. A request that sees
    // the same generation before its session check and at entry knows no
    // gateway swap or idle unload could have stopped the session in between.
    public long BlockGeneration
    {
        get
        {
            lock (_sync)
                return _blockGeneration;
        }
    }

    public int ActiveRequestCount(string modelId, string? profileId = null)
    {
        lock (_sync)
            return _active.Where(entry => Matches(entry.Key, modelId, profileId)).Sum(entry => entry.Value);
    }

    // Waits while lifecycle work has blocked this session, then records an
    // active upstream request until the returned lease is disposed.
    public async Task<ModelGatewayUpstreamEntry> EnterAsync(
        string modelId,
        string profileId,
        long observedGeneration,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        var key = (modelId, profileId);
        var waited = false;
        while (true)
        {
            Task changed;
            lock (_sync)
            {
                if (!_blocks.Any(block => block.Applies(key)))
                {
                    _active[key] = _active.GetValueOrDefault(key) + 1;
                    return new ModelGatewayUpstreamEntry(
                        new Lease(() => Exit(key)),
                        waited || _blockGeneration != observedGeneration);
                }
                changed = _changed.Task;
            }
            waited = true;
            await changed.WaitAsync(cancellationToken);
        }
    }

    // Blocks new requests for the selected sessions and waits until their
    // current requests finish. Throws TimeoutException, leaving them running,
    // if they do not finish within the timeout.
    public async Task<IDisposable> BlockAndDrainAsync(
        Func<string, string, bool> appliesTo,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(appliesTo);
        var block = new Block(appliesTo);
        lock (_sync)
            AddBlockLocked(block);
        var release = new Lease(() => Release(block));
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(timeout);
            while (true)
            {
                Task changed;
                lock (_sync)
                {
                    if (!_active.Any(entry => entry.Value > 0 && block.Applies(entry.Key)))
                        return release;
                    changed = _changed.Task;
                }
                try
                {
                    await changed.WaitAsync(deadline.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException(
                        $"Active gateway responses did not finish within {timeout.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds.");
                }
            }
        }
        catch
        {
            release.Dispose();
            throw;
        }
    }

    // Blocks one session only if it has no active request right now.
    // Returns null, leaving the session untouched, when a request is active.
    public IDisposable? TryBlockIdle(string modelId, string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        var block = new Block((model, profile) => Matches((model, profile), modelId, profileId));
        lock (_sync)
        {
            if (_active.Any(entry => entry.Value > 0 && block.Applies(entry.Key)))
                return null;
            AddBlockLocked(block);
        }
        return new Lease(() => Release(block));
    }

    private void AddBlockLocked(Block block)
    {
        _blocks.Add(block);
        _blockGeneration++;
    }

    private void Exit((string, string) key)
    {
        lock (_sync)
        {
            var remaining = _active.GetValueOrDefault(key) - 1;
            if (remaining > 0) _active[key] = remaining;
            else _active.Remove(key);
            SignalLocked();
        }
    }

    private void Release(Block block)
    {
        lock (_sync)
        {
            _blocks.Remove(block);
            SignalLocked();
        }
    }

    private void SignalLocked()
    {
        var changed = _changed;
        _changed = NewSignal();
        changed.TrySetResult();
    }

    private static bool Matches((string ModelId, string ProfileId) key, string modelId, string? profileId)
        => string.Equals(key.ModelId, modelId, StringComparison.OrdinalIgnoreCase)
           && (string.IsNullOrWhiteSpace(profileId) || string.Equals(key.ProfileId, profileId, StringComparison.OrdinalIgnoreCase));

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class Block(Func<string, string, bool> appliesTo)
    {
        public bool Applies((string ModelId, string ProfileId) key) => appliesTo(key.ModelId, key.ProfileId);
    }

    private sealed class Lease(Action release) : IDisposable
    {
        private Action? _release = release;

        public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
    }

    private sealed class SessionKeyComparer : IEqualityComparer<(string ModelId, string ProfileId)>
    {
        public static readonly SessionKeyComparer Instance = new();

        public bool Equals((string ModelId, string ProfileId) x, (string ModelId, string ProfileId) y)
            => StringComparer.OrdinalIgnoreCase.Equals(x.ModelId, y.ModelId)
               && StringComparer.OrdinalIgnoreCase.Equals(x.ProfileId, y.ProfileId);

        public int GetHashCode((string ModelId, string ProfileId) obj)
            => HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ModelId),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.ProfileId));
    }
}
