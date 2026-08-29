using System.Collections.Concurrent;

namespace LocalLlmConsole.Services;

public sealed class ModelGatewayRequestGate
{
    private readonly ConcurrentDictionary<string, ModelGateState> _gates = new(StringComparer.OrdinalIgnoreCase);

    internal int TrackedModelCount => _gates.Count;

    public async Task<IDisposable> EnterAsync(
        string modelId,
        string profileId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelId);
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        while (true)
        {
            var state = _gates.GetOrAdd(modelId, _ => new ModelGateState());
            if (!state.TryRetain())
            {
                _gates.TryRemove(new KeyValuePair<string, ModelGateState>(modelId, state));
                continue;
            }

            try
            {
                var lease = await state.EnterAsync(profileId, cancellationToken);
                return new TrackedLease(lease, () => Release(modelId, state));
            }
            catch
            {
                Release(modelId, state);
                throw;
            }
        }
    }

    private void Release(string modelId, ModelGateState state)
    {
        if (state.ReleaseReference())
            _gates.TryRemove(new KeyValuePair<string, ModelGateState>(modelId, state));
    }

    private sealed class TrackedLease(IDisposable inner, Action release) : IDisposable
    {
        private IDisposable? _inner = inner;
        private Action? _release = release;

        public void Dispose()
        {
            Interlocked.Exchange(ref _inner, null)?.Dispose();
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }

    private sealed class ModelGateState
    {
        private readonly object _sync = new();
        private string _activeProfileId = "";
        private int _activeRequests;
        private int _references;
        private bool _retired;
        private readonly LinkedList<Waiter> _waiters = [];

        public bool TryRetain()
        {
            lock (_sync)
            {
                if (_retired) return false;
                _references++;
                return true;
            }
        }

        public bool ReleaseReference()
        {
            lock (_sync)
            {
                if (--_references != 0) return false;
                _retired = true;
                return true;
            }
        }

        public async Task<IDisposable> EnterAsync(string profileId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Waiter? waiter = null;
            lock (_sync)
            {
                if (_waiters.Count == 0
                    && (_activeRequests == 0 || _activeProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase)))
                {
                    if (_activeRequests == 0)
                        _activeProfileId = profileId;
                    _activeRequests++;
                    return new Lease(this);
                }

                waiter = new Waiter(profileId);
                waiter.Node = _waiters.AddLast(waiter);
            }

            try
            {
                await waiter.Ready.Task.WaitAsync(cancellationToken);
                return new Lease(this);
            }
            catch (OperationCanceledException)
            {
                var ready = CancelWaiter(waiter);
                Complete(ready);
                throw;
            }
        }

        private void Exit()
        {
            List<TaskCompletionSource<bool>> ready;
            lock (_sync)
            {
                if (--_activeRequests != 0) return;
                ready = ActivateNextGroupLocked();
            }

            Complete(ready);
        }

        private List<TaskCompletionSource<bool>> CancelWaiter(Waiter waiter)
        {
            lock (_sync)
            {
                if (waiter.State == WaiterState.Queued)
                {
                    _waiters.Remove(waiter.Node!);
                    waiter.State = WaiterState.Cancelled;
                    return [];
                }

                if (waiter.State != WaiterState.Activated)
                    return [];

                waiter.State = WaiterState.Cancelled;
                if (--_activeRequests != 0) return [];
                return ActivateNextGroupLocked();
            }
        }

        private List<TaskCompletionSource<bool>> ActivateNextGroupLocked()
        {
            _activeProfileId = "";
            var ready = new List<TaskCompletionSource<bool>>();
            if (_waiters.First is null) return ready;

            var profileId = _waiters.First.Value.ProfileId;
            _activeProfileId = profileId;
            while (_waiters.First is { } node
                   && node.Value.ProfileId.Equals(profileId, StringComparison.OrdinalIgnoreCase))
            {
                _waiters.RemoveFirst();
                node.Value.Node = null;
                node.Value.State = WaiterState.Activated;
                _activeRequests++;
                ready.Add(node.Value.Ready);
            }
            return ready;
        }

        private static void Complete(IEnumerable<TaskCompletionSource<bool>> ready)
        {
            foreach (var completion in ready)
                completion.TrySetResult(true);
        }

        private enum WaiterState
        {
            Queued,
            Activated,
            Cancelled
        }

        private sealed class Waiter(string profileId)
        {
            public string ProfileId { get; } = profileId;
            public TaskCompletionSource<bool> Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public LinkedListNode<Waiter>? Node { get; set; }
            public WaiterState State { get; set; }
        }

        private sealed class Lease(ModelGateState owner) : IDisposable
        {
            private ModelGateState? _owner = owner;

            public void Dispose()
                => Interlocked.Exchange(ref _owner, null)?.Exit();
        }
    }
}
