namespace LocalLlmConsole.Services;

/// <summary>
/// Evaluates dashboard thresholds. It keeps the last state per metric so a
/// state clears only after the value moves back past the level by the
/// hysteresis margin, and so counters can warn after they increase.
/// </summary>
public sealed class OverviewDashboardAlertTracker
{
    private readonly Dictionary<string, MetricState> _states = new(StringComparer.Ordinal);

    public OverviewDashboardAlertLevel Evaluate(
        string metricId,
        double? primary,
        double? secondary,
        string runtimeKey,
        IReadOnlyDictionary<string, OverviewDashboardMetricThreshold>? overrides,
        DateTimeOffset now)
    {
        var rule = OverviewDashboardThresholdPolicy.Rule(metricId);
        var threshold = OverviewDashboardThresholdPolicy.Effective(metricId, overrides);
        var value = rule is null ? null : Value(rule, primary, secondary);
        if (rule is null || threshold is not { Enabled: true } || value is null)
        {
            _states.Remove(metricId);
            return OverviewDashboardAlertLevel.Normal;
        }

        if (!_states.TryGetValue(metricId, out var state)
            || !string.Equals(state.RuntimeKey, runtimeKey ?? "", StringComparison.Ordinal))
        {
            state = new MetricState(runtimeKey ?? "");
            _states[metricId] = state;
        }

        var level = rule.Kind switch
        {
            OverviewDashboardThresholdKind.Increasing => Increasing(state, value.Value, now),
            _ => Level(rule.Kind, threshold, value.Value, state.Level)
        };
        state.Level = level;
        state.LastValue = value.Value;
        return level;
    }

    /// <summary>
    /// The value a rule compares, or null when the reading has no usable value.
    /// </summary>
    public static double? Value(OverviewDashboardThresholdRule rule, double? primary, double? secondary)
    {
        ArgumentNullException.ThrowIfNull(rule);
        if (primary is not { } value || !double.IsFinite(value)) return null;
        if (rule.Value == OverviewDashboardThresholdValue.Primary) return value;
        return secondary is { } total && double.IsFinite(total) && total > 0 ? 100 * value / total : null;
    }

    private static OverviewDashboardAlertLevel Level(
        OverviewDashboardThresholdKind kind,
        OverviewDashboardMetricThreshold threshold,
        double value,
        OverviewDashboardAlertLevel previous)
    {
        if (Reached(kind, threshold.Critical, value, previous >= OverviewDashboardAlertLevel.Critical))
            return OverviewDashboardAlertLevel.Critical;
        return Reached(kind, threshold.Warning, value, previous >= OverviewDashboardAlertLevel.Warning)
            ? OverviewDashboardAlertLevel.Warning
            : OverviewDashboardAlertLevel.Normal;
    }

    private static bool Reached(OverviewDashboardThresholdKind kind, double? level, double value, bool active)
    {
        if (level is not { } limit) return false;
        var margin = active ? Math.Abs(limit) * OverviewDashboardThresholdPolicy.HysteresisFraction : 0;
        return kind == OverviewDashboardThresholdKind.Below
            ? active ? value < limit + margin : value < limit
            : active ? value > limit - margin : value > limit;
    }

    private static OverviewDashboardAlertLevel Increasing(MetricState state, double value, DateTimeOffset now)
    {
        if (state.LastValue is { } previous && value > previous)
            state.LastIncrease = now;
        return state.LastIncrease is { } increased && now - increased < OverviewDashboardThresholdPolicy.IncreasingWarningHold
            ? OverviewDashboardAlertLevel.Warning
            : OverviewDashboardAlertLevel.Normal;
    }

    private sealed class MetricState(string runtimeKey)
    {
        public string RuntimeKey { get; } = runtimeKey;
        public OverviewDashboardAlertLevel Level { get; set; }
        public double? LastValue { get; set; }
        public DateTimeOffset? LastIncrease { get; set; }
    }
}
