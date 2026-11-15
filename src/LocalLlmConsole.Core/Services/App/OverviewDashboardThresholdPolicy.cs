namespace LocalLlmConsole.Services;

/// <summary>
/// Default warning and critical thresholds for dashboard metrics, user
/// overrides, and validation. Evaluation lives in
/// <see cref="OverviewDashboardAlertTracker"/>.
/// </summary>
public static class OverviewDashboardThresholdPolicy
{
    /// <summary>How long an increasing counter keeps its warning.</summary>
    public static readonly TimeSpan IncreasingWarningHold = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Fraction of a level a value must move back past before its state clears,
    /// so readings hovering at a threshold do not flicker.
    /// </summary>
    public const double HysteresisFraction = .03;

    public const int MaximumOverrides = 256;

    private static readonly OverviewDashboardThresholdRule KvCache = Above(85, 95, "%");
    private static readonly OverviewDashboardThresholdRule Queue = Above(0, null, "");
    private static readonly OverviewDashboardThresholdRule FailureRate = Above(1, 5, "%");
    private static readonly OverviewDashboardThresholdRule Temperature = Above(85, 95, "°C");
    private static readonly OverviewDashboardThresholdRule Vram = new(
        OverviewDashboardThresholdKind.Above,
        OverviewDashboardThresholdValue.PercentOfSecondary,
        new OverviewDashboardMetricThreshold(true, 90, 97),
        "%");
    private static readonly OverviewDashboardThresholdRule ContextShifts = new(
        OverviewDashboardThresholdKind.Increasing,
        OverviewDashboardThresholdValue.Primary,
        new OverviewDashboardMetricThreshold(),
        "");
    private static readonly OverviewDashboardThresholdRule DraftAcceptance = new(
        OverviewDashboardThresholdKind.Below,
        OverviewDashboardThresholdValue.Primary,
        new OverviewDashboardMetricThreshold(true, 40, null),
        "%");

    public static OverviewDashboardThresholdRule? Rule(string metricId)
    {
        if (string.IsNullOrWhiteSpace(metricId)) return null;
        return metricId switch
        {
            OverviewDashboardMetricIds.KvCacheUsage => KvCache,
            OverviewDashboardMetricIds.QueuedRequests => Queue,
            OverviewDashboardMetricIds.GatewayFailureRate => FailureRate,
            OverviewDashboardMetricIds.CpuTemperature => Temperature,
            OverviewDashboardMetricIds.ContextShifts => ContextShifts,
            OverviewDashboardMetricIds.DraftAcceptance => DraftAcceptance,
            _ when OverviewDashboardMetricIds.TryParseGpuTemperature(metricId, out _) => Temperature,
            _ when OverviewDashboardMetricIds.TryParseGpuVram(metricId, out _) => Vram,
            _ => null
        };
    }

    public static bool SupportsThresholds(string metricId) => Rule(metricId) is not null;

    /// <summary>The user's override when present, otherwise the default.</summary>
    public static OverviewDashboardMetricThreshold? Effective(
        string metricId,
        IReadOnlyDictionary<string, OverviewDashboardMetricThreshold>? overrides)
    {
        var rule = Rule(metricId);
        if (rule is null) return null;
        return overrides is not null && overrides.TryGetValue(metricId, out var saved) && IsValid(rule, saved)
            ? saved
            : rule.Default;
    }

    /// <summary>
    /// A threshold is valid when its levels are finite and the critical level is
    /// at least as severe as the warning level. Increasing rules use no levels.
    /// </summary>
    public static bool IsValid(OverviewDashboardThresholdRule rule, OverviewDashboardMetricThreshold? threshold)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (threshold is null) return false;
        if (rule.Kind == OverviewDashboardThresholdKind.Increasing)
            return threshold.Warning is null && threshold.Critical is null;
        if (threshold.Warning is { } warning && !double.IsFinite(warning)) return false;
        if (threshold.Critical is { } critical && !double.IsFinite(critical)) return false;
        if (threshold.Warning is not { } low || threshold.Critical is not { } high) return true;
        return rule.Kind == OverviewDashboardThresholdKind.Above ? high >= low : high <= low;
    }

    /// <summary>
    /// Keeps valid overrides for supported metrics that differ from their
    /// default. Returns null when nothing remains, so untouched layouts persist
    /// exactly as before.
    /// </summary>
    public static IReadOnlyDictionary<string, OverviewDashboardMetricThreshold>? Normalize(
        IReadOnlyDictionary<string, OverviewDashboardMetricThreshold>? overrides)
    {
        if (overrides is null || overrides.Count == 0) return null;
        var result = new SortedDictionary<string, OverviewDashboardMetricThreshold>(StringComparer.Ordinal);
        foreach (var (metricId, threshold) in overrides)
        {
            if (result.Count >= MaximumOverrides) break;
            if (Rule(metricId) is not { } rule || !IsValid(rule, threshold) || threshold == rule.Default) continue;
            result[metricId] = threshold;
        }
        return result.Count == 0 ? null : new Dictionary<string, OverviewDashboardMetricThreshold>(result, StringComparer.Ordinal);
    }

    public static bool SameOverrides(
        IReadOnlyDictionary<string, OverviewDashboardMetricThreshold>? first,
        IReadOnlyDictionary<string, OverviewDashboardMetricThreshold>? second)
    {
        var left = first ?? new Dictionary<string, OverviewDashboardMetricThreshold>();
        var right = second ?? new Dictionary<string, OverviewDashboardMetricThreshold>();
        return left.Count == right.Count
               && left.All(pair => right.TryGetValue(pair.Key, out var other) && other == pair.Value);
    }

    private static OverviewDashboardThresholdRule Above(double warning, double? critical, string unit)
        => new(
            OverviewDashboardThresholdKind.Above,
            OverviewDashboardThresholdValue.Primary,
            new OverviewDashboardMetricThreshold(true, warning, critical),
            unit);
}

public static partial class OverviewDashboardLayoutPolicy
{
    /// <summary>
    /// Saves a metric's threshold, or restores its default when
    /// <paramref name="threshold"/> is null.
    /// </summary>
    public static OverviewDashboardLayout SetMetricThreshold(
        OverviewDashboardLayout layout,
        string metricId,
        OverviewDashboardMetricThreshold? threshold)
    {
        ArgumentNullException.ThrowIfNull(layout);
        var normalized = Normalize(layout);
        if (!OverviewDashboardThresholdPolicy.SupportsThresholds(metricId)) return normalized;
        var overrides = new Dictionary<string, OverviewDashboardMetricThreshold>(
            normalized.MetricThresholds ?? new Dictionary<string, OverviewDashboardMetricThreshold>(),
            StringComparer.Ordinal);
        if (threshold is null)
            overrides.Remove(metricId);
        else
            overrides[metricId] = threshold;
        return Normalize(normalized with { MetricThresholds = overrides });
    }
}
