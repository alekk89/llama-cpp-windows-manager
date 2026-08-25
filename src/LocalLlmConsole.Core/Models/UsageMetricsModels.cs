namespace LocalLlmConsole.Models;

public enum UsageMetricsRange
{
    SevenDays,
    CurrentMonth,
    ThirtyDays,
    NinetyDays,
    All,
    OneDay
}

public enum UsageDateSelectionMode
{
    Replace,
    Toggle,
    Range,
    AddRange
}

public sealed record UsageDateSelection(
    IReadOnlyList<DateOnly> Dates,
    DateOnly? Anchor)
{
    public static UsageDateSelection Empty { get; } = new([], null);
}

public sealed record TokenUsageDelta(
    string ModelId,
    string ModelName,
    long PromptTokens,
    long GeneratedTokens,
    long CachedPromptTokens = 0,
    bool CacheCounterObserved = false,
    DateTimeOffset? CapturedAt = null,
    string LaunchProfileId = "",
    string LaunchProfileName = "",
    string RuntimeId = "",
    string RuntimeName = "",
    RuntimeMode RuntimeMode = RuntimeMode.Native,
    RuntimeBackend RuntimeBackend = RuntimeBackend.Cpu,
    double PromptSeconds = 0,
    double GeneratedSeconds = 0,
    bool TimingCounterObserved = false,
    long RequestCount = 0,
    long FailedRequestCount = 0,
    bool RequestCounterObserved = false)
{
    public static TokenUsageDelta Empty { get; } = new("", "", 0, 0);

    public bool HasTokens => PromptTokens > 0 || CachedPromptTokens > 0 || GeneratedTokens > 0;

    public bool HasActivity => HasTokens || RequestCount > 0 || FailedRequestCount > 0;

    public DateTimeOffset EffectiveCapturedAt => CapturedAt ?? DateTimeOffset.UtcNow;
}

public sealed record UsageMetricBucket(
    DateTimeOffset BucketStartUtc,
    string ModelId,
    string ModelName,
    string LaunchProfileId,
    string LaunchProfileName,
    string RuntimeId,
    string RuntimeName,
    RuntimeMode RuntimeMode,
    RuntimeBackend RuntimeBackend,
    long PromptTokens,
    long CachedPromptTokens,
    long GeneratedTokens,
    bool CacheCounterObserved,
    DateTimeOffset UpdatedAt,
    double PromptSeconds = 0,
    double GeneratedSeconds = 0,
    bool TimingCounterObserved = false,
    long RequestCount = 0,
    long FailedRequestCount = 0,
    bool RequestCounterObserved = false);

public sealed record UsageMetricDimension(string Id, string Name);

public sealed record UsageMetricDimensions(
    IReadOnlyList<UsageMetricDimension> Models,
    IReadOnlyList<UsageMetricDimension> LaunchProfiles,
    IReadOnlyList<UsageMetricDimension> Runtimes)
{
    public static UsageMetricDimensions Empty { get; } = new([], [], []);
}

public sealed record UsageMetricTotals(
    long PromptTokens,
    long CachedPromptTokens,
    long GeneratedTokens,
    bool CacheCounterObserved,
    double PromptSeconds = 0,
    double GeneratedSeconds = 0,
    bool TimingCounterObserved = false,
    long RequestCount = 0,
    long FailedRequestCount = 0,
    bool RequestCounterObserved = false)
{
    public static UsageMetricTotals Empty { get; } = new(0, 0, 0, false);

    public long InputTokens => PromptTokens + CachedPromptTokens;

    public long TotalTokens => InputTokens + GeneratedTokens;

    public double? CacheHitRate => CacheCounterObserved && InputTokens > 0
        ? CachedPromptTokens / (double)InputTokens
        : null;

    public long SuccessfulRequestCount => Math.Max(0, RequestCount - FailedRequestCount);

    public double? AveragePromptTokensPerSecond => TimingCounterObserved && PromptSeconds > 0
        ? PromptTokens / PromptSeconds
        : null;

    public double? AverageGeneratedTokensPerSecond => TimingCounterObserved && GeneratedSeconds > 0
        ? GeneratedTokens / GeneratedSeconds
        : null;

    public double? AverageInputTokensPerRequest => RequestCounterObserved && RequestCount > 0
        ? InputTokens / (double)RequestCount
        : null;

    public double? AverageGeneratedTokensPerRequest => RequestCounterObserved && RequestCount > 0
        ? GeneratedTokens / (double)RequestCount
        : null;

    public double? RequestSuccessRate => RequestCounterObserved && RequestCount > 0
        ? SuccessfulRequestCount / (double)RequestCount
        : null;

    public static UsageMetricTotals Sum(IEnumerable<UsageMetricTotals> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        long prompt = 0;
        long cached = 0;
        long generated = 0;
        double promptSeconds = 0;
        double generatedSeconds = 0;
        long requests = 0;
        long failedRequests = 0;
        var cacheObserved = false;
        var timingObserved = false;
        var requestsObserved = false;
        foreach (var value in values)
        {
            prompt += Math.Max(0, value.PromptTokens);
            cached += Math.Max(0, value.CachedPromptTokens);
            generated += Math.Max(0, value.GeneratedTokens);
            promptSeconds += Math.Max(0, value.PromptSeconds);
            generatedSeconds += Math.Max(0, value.GeneratedSeconds);
            requests += Math.Max(0, value.RequestCount);
            failedRequests += Math.Max(0, value.FailedRequestCount);
            cacheObserved |= value.CacheCounterObserved;
            timingObserved |= value.TimingCounterObserved;
            requestsObserved |= value.RequestCounterObserved;
        }
        return new UsageMetricTotals(
            prompt,
            cached,
            generated,
            cacheObserved,
            promptSeconds,
            generatedSeconds,
            timingObserved,
            requests,
            failedRequests,
            requestsObserved);
    }
}

public sealed record GpuPowerSensorReading(
    string Key,
    int GpuIndex,
    string GpuName,
    double Watts);

public sealed record GpuPowerObservation(
    DateTimeOffset CapturedAt,
    double TotalWatts,
    IReadOnlyList<string> SensorKeys,
    int DetectedGpuCount)
{
    public IReadOnlyList<GpuPowerSensorReading> Sensors { get; init; } = [];

    public int ObservedGpuCount => SensorKeys?.Count ?? 0;

    public bool HasPower => double.IsFinite(TotalWatts) && TotalWatts >= 0 && ObservedGpuCount > 0;

    public bool HasCompleteCoverage => HasPower && ObservedGpuCount == DetectedGpuCount;
}

public sealed record GpuEnergyDelta(
    DateTimeOffset BucketStartUtc,
    double WattHours,
    double SampledSeconds,
    bool CompleteCoverage,
    int ObservedGpuCount,
    int DetectedGpuCount,
    DateTimeOffset CapturedAt);

public sealed record GpuEnergyBucket(
    DateTimeOffset BucketStartUtc,
    double WattHours,
    double SampledSeconds,
    bool CompleteCoverage,
    int ObservedGpuCount,
    int DetectedGpuCount,
    DateTimeOffset UpdatedAt);

public sealed record GpuEnergyDeviceDelta(
    DateTimeOffset BucketStartUtc,
    string SensorKey,
    int GpuIndex,
    string GpuName,
    double WattHours,
    double SampledSeconds,
    DateTimeOffset CapturedAt);

public sealed record GpuEnergyDeviceBucket(
    DateTimeOffset BucketStartUtc,
    string SensorKey,
    int GpuIndex,
    string GpuName,
    double WattHours,
    double SampledSeconds,
    DateTimeOffset UpdatedAt);

public sealed record GpuEnergyDeviceTotals(
    string SensorKey,
    int GpuIndex,
    string GpuName,
    double WattHours,
    double SampledSeconds)
{
    public double KilowattHours => WattHours / 1000;
}

public sealed record GpuEnergySampleResult(
    IReadOnlyList<GpuEnergyDelta> TotalDeltas,
    IReadOnlyList<GpuEnergyDeviceDelta> DeviceDeltas)
{
    public static GpuEnergySampleResult Empty { get; } = new([], []);
}

public sealed record GpuEnergyTotals(
    double WattHours,
    double SampledSeconds,
    bool PowerObserved,
    bool CompleteCoverage,
    int ObservedGpuCount,
    int DetectedGpuCount)
{
    public static GpuEnergyTotals Empty { get; } = new(0, 0, false, false, 0, 0);

    public double KilowattHours => WattHours / 1000;

    public static GpuEnergyTotals Sum(IEnumerable<GpuEnergyBucket> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var buckets = values.Where(value => value.SampledSeconds > 0).ToArray();
        if (buckets.Length == 0) return Empty;
        return new GpuEnergyTotals(
            buckets.Sum(value => Math.Max(0, value.WattHours)),
            buckets.Sum(value => Math.Max(0, value.SampledSeconds)),
            true,
            buckets.All(value => value.CompleteCoverage),
            buckets.Min(value => value.ObservedGpuCount),
            buckets.Max(value => value.DetectedGpuCount));
    }
}

public sealed record UsageMetricsQuery(
    UsageMetricsRange Range = UsageMetricsRange.All,
    string ModelId = "",
    string LaunchProfileId = "",
    string RuntimeId = "",
    IReadOnlyList<DateOnly>? Dates = null);

public sealed record UsageMetricsWindow(
    DateTimeOffset? FromUtc,
    DateTimeOffset ToUtc,
    DateOnly? FirstLocalDate,
    DateOnly LastLocalDate);

public sealed record UsageMetricDay(
    DateOnly Date,
    UsageMetricTotals Totals,
    bool IsTracked = true,
    GpuEnergyTotals? GpuEnergy = null);

public sealed record UsageMetricModelBreakdown(
    string ModelId,
    string ModelName,
    UsageMetricTotals Totals,
    UsageMetricTotals CacheStatistics,
    DateTimeOffset UpdatedAt);

public sealed record UsageMetricsInsights(
    int ActiveDays,
    DateOnly? PeakDate,
    UsageMetricTotals PeakTotals,
    double AverageTokensPerActiveDay)
{
    public static UsageMetricsInsights Empty { get; } = new(0, null, UsageMetricTotals.Empty, 0);
}

public sealed record UsageMetricsReport(
    UsageMetricsQuery Query,
    UsageMetricsWindow Window,
    UsageMetricTotals Summary,
    UsageMetricTotals TrackedSummary,
    IReadOnlyList<UsageMetricDay> Days,
    UsageMetricsWindow CalendarWindow,
    IReadOnlyList<UsageMetricDay> CalendarDays,
    IReadOnlyList<UsageMetricModelBreakdown> Models,
    UsageMetricDimensions Dimensions,
    UsageMetricsInsights Insights,
    DateTimeOffset? TrackingStartedAt,
    bool IncludesLegacyTotals,
    GpuEnergyTotals? GpuEnergy = null,
    DateTimeOffset? GpuEnergyTrackingStartedAt = null,
    IReadOnlyList<GpuEnergyDeviceTotals>? GpuEnergyDevices = null,
    ElectricityCostTotals? GpuElectricityCost = null);
