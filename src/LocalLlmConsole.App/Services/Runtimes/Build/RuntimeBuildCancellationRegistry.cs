namespace LocalLlmConsole.Services;

public sealed class RuntimeBuildCancellationRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<string, CancellationTokenSource> _cancellations = new(StringComparer.OrdinalIgnoreCase);

    public int ActiveCount
    {
        get
        {
            lock (_gate)
                return _cancellations.Count;
        }
    }

    public CancellationTokenSource Register(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
            throw new ArgumentException("Job id is required.", nameof(jobId));

        var cancellation = new CancellationTokenSource();
        lock (_gate)
        {
            if (_cancellations.Remove(jobId, out var existing))
                existing.Dispose();
            _cancellations[jobId] = cancellation;
        }

        return cancellation;
    }

    public bool TryCancel(string jobId)
    {
        lock (_gate)
        {
            if (!_cancellations.TryGetValue(jobId, out var cancellation)) return false;
            cancellation.Cancel();
            return true;
        }
    }

    public void Unregister(string jobId, CancellationTokenSource cancellation)
    {
        lock (_gate)
        {
            if (_cancellations.TryGetValue(jobId, out var current) && ReferenceEquals(current, cancellation))
                _cancellations.Remove(jobId);
        }

        cancellation.Dispose();
    }
}
