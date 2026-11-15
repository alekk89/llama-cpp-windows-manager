namespace LocalLlmConsole.Models;

public enum OverviewDashboardAlertLevel
{
    Normal,
    Warning,
    Critical
}

public enum OverviewDashboardThresholdKind
{
    /// <summary>The state applies while the value is above a level.</summary>
    Above,
    /// <summary>The state applies while the value is below a level.</summary>
    Below,
    /// <summary>A warning applies for a while after a counter increases.</summary>
    Increasing
}

public enum OverviewDashboardThresholdValue
{
    Primary,
    /// <summary>The reading's primary value as a percentage of its secondary value.</summary>
    PercentOfSecondary
}

/// <summary>
/// A persisted per-metric threshold. A null level means that level is not used.
/// Saved only when it differs from the metric's default.
/// </summary>
public sealed record OverviewDashboardMetricThreshold(
    bool Enabled = true,
    double? Warning = null,
    double? Critical = null);

/// <summary>The built-in evaluation rule and default levels for a metric.</summary>
public sealed record OverviewDashboardThresholdRule(
    OverviewDashboardThresholdKind Kind,
    OverviewDashboardThresholdValue Value,
    OverviewDashboardMetricThreshold Default,
    string Unit);
