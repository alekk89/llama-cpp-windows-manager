namespace LocalLlmConsole.Services;

public sealed record GatewayPerformanceSnapshot(
    long RequestCount,
    long FailureCount,
    double? LastTimeToFirstDataMilliseconds,
    double? LastRequestDurationMilliseconds,
    double? LastResponseTokensPerSecond,
    DateTimeOffset? CapturedAt)
{
    public double? FailureRatePercent => RequestCount > 0
        ? 100d * FailureCount / RequestCount
        : null;
}

public sealed class GatewayPerformanceTracker
{
    private readonly object _gate = new();
    private long _requestCount;
    private long _failureCount;
    private double? _lastTimeToFirstDataMilliseconds;
    private double? _lastRequestDurationMilliseconds;
    private double? _lastResponseTokensPerSecond;
    private DateTimeOffset? _capturedAt;

    public void Observe(bool succeeded, TimeSpan duration, TimeSpan? timeToFirstData, double? responseTokensPerSecond)
    {
        lock (_gate)
        {
            _requestCount++;
            if (!succeeded) _failureCount++;
            _lastRequestDurationMilliseconds = Math.Max(0, duration.TotalMilliseconds);
            _lastTimeToFirstDataMilliseconds = timeToFirstData is { } first
                ? Math.Max(0, first.TotalMilliseconds)
                : null;
            _lastResponseTokensPerSecond = responseTokensPerSecond is { } rate && double.IsFinite(rate) && rate >= 0
                ? rate
                : null;
            _capturedAt = DateTimeOffset.UtcNow;
        }
    }

    public GatewayPerformanceSnapshot Snapshot()
    {
        lock (_gate)
            return new(_requestCount, _failureCount, _lastTimeToFirstDataMilliseconds,
                _lastRequestDurationMilliseconds, _lastResponseTokensPerSecond, _capturedAt);
    }
}
