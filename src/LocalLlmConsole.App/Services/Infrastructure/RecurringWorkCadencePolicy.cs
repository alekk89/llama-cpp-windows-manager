namespace LocalLlmConsole.Services;

public sealed record GpuEnergySamplingPlan(TimeSpan Interval, bool PersistHistory);

public static class GpuEnergySamplingPolicy
{
    public static readonly TimeSpan ActiveInterval = TimeSpan.FromSeconds(10);
    public static readonly TimeSpan IdleInterval = TimeSpan.FromMinutes(5);

    public static GpuEnergySamplingPlan Decide(bool hasRunningSessions, bool trackWhileIdle)
    {
        var persistHistory = hasRunningSessions || trackWhileIdle;
        return new GpuEnergySamplingPlan(
            persistHistory ? ActiveInterval : IdleInterval,
            persistHistory);
    }
}

public static class LifetimeMetricsRefreshPolicy
{
    public static bool ShouldRefresh(
        bool renderUiFrame,
        bool metricsPageVisible,
        long dataVersion,
        long lastRenderedDataVersion,
        DateTimeOffset now,
        DateTimeOffset nextRefreshAt)
        => renderUiFrame
           && metricsPageVisible
           && dataVersion != lastRenderedDataVersion
           && now >= nextRefreshAt;
}

public sealed class MinimizedUiRefreshPolicy
{
    public static readonly TimeSpan RenderInterval = TimeSpan.FromSeconds(5);
    private readonly object _gate = new();
    private DateTimeOffset _nextRenderAt = DateTimeOffset.MinValue;

    public bool ShouldRender(bool minimized, DateTimeOffset now)
    {
        lock (_gate)
        {
            if (!minimized)
            {
                _nextRenderAt = DateTimeOffset.MinValue;
                return true;
            }

            if (now < _nextRenderAt) return false;
            _nextRenderAt = now.Add(RenderInterval);
            return true;
        }
    }

    public void Reset()
    {
        lock (_gate)
            _nextRenderAt = DateTimeOffset.MinValue;
    }
}
