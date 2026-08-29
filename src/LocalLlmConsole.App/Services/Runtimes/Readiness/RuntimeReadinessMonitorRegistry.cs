namespace LocalLlmConsole.Services;

public sealed class RuntimeReadinessMonitorRegistry : IDisposable
{
    private readonly Dictionary<string, CancellationTokenSource> _monitors = new(StringComparer.OrdinalIgnoreCase);

    public int Count => _monitors.Count;

    public CancellationTokenSource Start(string modelId)
    {
        Stop(modelId);
        var cancellation = new CancellationTokenSource();
        _monitors[modelId] = cancellation;
        return cancellation;
    }

    public bool Contains(string modelId)
        => _monitors.ContainsKey(modelId);

    public void Stop(string modelId)
    {
        if (!_monitors.Remove(modelId, out var cancellation)) return;
        CancelAndDispose(cancellation);
    }

    public void StopAll()
    {
        foreach (var modelId in _monitors.Keys.ToArray())
            Stop(modelId);
    }

    public bool Complete(string modelId, CancellationTokenSource cancellation)
    {
        if (!_monitors.TryGetValue(modelId, out var active)
            || !ReferenceEquals(active, cancellation))
        {
            cancellation.Dispose();
            return false;
        }

        _monitors.Remove(modelId);
        cancellation.Dispose();
        return true;
    }

    public void Dispose()
        => StopAll();

    private static void CancelAndDispose(CancellationTokenSource cancellation)
    {
        try { cancellation.Cancel(); }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Runtime readiness monitor cancellation failed: {ex.Message}");
        }
        cancellation.Dispose();
    }
}
