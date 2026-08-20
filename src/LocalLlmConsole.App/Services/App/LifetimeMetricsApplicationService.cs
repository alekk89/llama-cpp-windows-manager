namespace LocalLlmConsole.Services;

public sealed class LifetimeMetricsApplicationService
{
    private readonly StateStore _stateStore;
    private readonly UsageMetricsService _usageMetrics;

    public LifetimeMetricsApplicationService(StateStore stateStore, UsageMetricsService? usageMetrics = null)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _usageMetrics = usageMetrics ?? new UsageMetricsService();
    }

    public Task<IReadOnlyList<TokenUsageRecord>> ListAsync()
        => _stateStore.ListTokenUsageAsync();

    public Task AddUsageAsync(TokenUsageDelta delta)
    {
        ArgumentNullException.ThrowIfNull(delta);

        return delta.HasTokens
            ? _stateStore.RecordTokenUsageAsync(delta)
            : Task.CompletedTask;
    }

    public async Task<UsageMetricsReport> GetReportAsync(
        UsageMetricsQuery query,
        TimeZoneInfo? timeZone = null,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        timeZone ??= TimeZoneInfo.Local;
        var capturedAt = now ?? DateTimeOffset.UtcNow;
        var reportWindow = _usageMetrics.ResolveWindow(query, capturedAt, timeZone);
        var calendarWindow = _usageMetrics.ResolveCalendarWindow(capturedAt, timeZone);
        var fromUtc = reportWindow.FromUtc is null
            ? null
            : reportWindow.FromUtc < calendarWindow.FromUtc
                ? reportWindow.FromUtc
                : calendarWindow.FromUtc;
        var toUtc = reportWindow.ToUtc > calendarWindow.ToUtc
            ? reportWindow.ToUtc
            : calendarWindow.ToUtc;
        var buckets = await _stateStore.ListTokenUsageBucketsAsync(fromUtc, toUtc);
        var lifetime = await _stateStore.ListTokenUsageAsync();
        var dimensions = await _stateStore.ListTokenUsageDimensionsAsync();
        var trackingStartedAt = await _stateStore.GetTokenUsageTrackingStartedAtAsync();
        return _usageMetrics.BuildReport(query, buckets, lifetime, dimensions, capturedAt, timeZone, trackingStartedAt);
    }

    public Task DeleteModelUsageAsync(string modelId)
        => _stateStore.DeleteTokenUsageAsync(modelId);

    public Task DeleteAllUsageAsync()
        => _stateStore.DeleteAllTokenUsageAsync();
}
