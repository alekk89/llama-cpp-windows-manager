namespace LocalLlmConsole.Services;

internal sealed class BenchmarkGpuMemorySampler : IAsyncDisposable
{
    internal const int IntervalMilliseconds = 1000;
    private readonly IGpuMemoryProbe? _probe;
    private readonly CancellationTokenSource _stop;
    private readonly Task _loop;
    private IReadOnlyList<BenchmarkGpuMemoryPeak> _peaks = [];

    private BenchmarkGpuMemorySampler(IGpuMemoryProbe? probe, CancellationToken cancellationToken, TimeSpan interval)
    {
        _probe = probe;
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Read();
        _loop = PollAsync(interval);
    }

    internal static Task<BenchmarkGpuMemorySampler> StartAsync(
        Func<IGpuMemoryProbe> createProbe,
        CancellationToken cancellationToken,
        TimeSpan? interval = null)
        => Task.Run(() =>
        {
            IGpuMemoryProbe? probe = null;
            try { probe = createProbe(); }
            catch (Exception ex) { Trace.TraceInformation($"GPU memory probe unavailable: {ex.Message}"); }
            return new BenchmarkGpuMemorySampler(probe, cancellationToken, interval ?? TimeSpan.FromMilliseconds(IntervalMilliseconds));
        }, cancellationToken);

    internal async Task<IReadOnlyList<BenchmarkGpuMemoryPeak>> FinishAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        Read();
        return _peaks;
    }

    private async Task PollAsync(TimeSpan interval)
    {
        if (_probe is null) return;
        try
        {
            using var timer = new PeriodicTimer(interval);
            while (await timer.WaitForNextTickAsync(_stop.Token)) Read();
        }
        catch (OperationCanceledException) when (_stop.IsCancellationRequested) { }
    }

    private void Read()
    {
        if (_probe is null) return;
        try
        {
            var samples = _probe.Read().Select(sample => new BenchmarkGpuMemoryPeak(
                sample.DeviceId, sample.DeviceName, NonNegative(sample.DedicatedCapacityMiB),
                NonNegative(sample.DedicatedUsedMiB), NonNegative(sample.SharedUsedMiB),
                sample.DedicatedUsedMiB is >= 0 || sample.SharedUsedMiB is >= 0 ? 1 : 0));
            _peaks = BenchmarkGpuMemoryService.Merge(_peaks.Concat(samples));
        }
        catch (Exception ex) { Trace.TraceInformation($"GPU memory sample unavailable: {ex.Message}"); }
    }

    private static long? NonNegative(long? value) => value is >= 0 ? value : null;

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        await _loop;
        _probe?.Dispose();
        _stop.Dispose();
    }
}
