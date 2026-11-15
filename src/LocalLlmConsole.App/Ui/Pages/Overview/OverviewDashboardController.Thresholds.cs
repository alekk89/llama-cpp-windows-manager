using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    private readonly OverviewDashboardAlertTracker _alertTracker = new();

    /// <summary>Saves a metric's threshold, or restores its default when null.</summary>
    public Task SetMetricThresholdAsync(string metricId, OverviewDashboardMetricThreshold? threshold)
        => MutateAsync(layout => OverviewDashboardLayoutPolicy.SetMetricThreshold(layout, metricId, threshold));

    private IReadOnlyDictionary<string, OverviewDashboardMetricAlert> EvaluateAlerts(DateTimeOffset now)
    {
        var alerts = new Dictionary<string, OverviewDashboardMetricAlert>(StringComparer.Ordinal);
        foreach (var reading in _readings.Values)
        {
            if (OverviewDashboardThresholdPolicy.Rule(reading.MetricId) is not { } rule) continue;
            var level = _alertTracker.Evaluate(
                reading.MetricId,
                reading.Primary,
                reading.Secondary,
                reading.RuntimeKey,
                _layout.MetricThresholds,
                now);
            if (level == OverviewDashboardAlertLevel.Normal) continue;
            var threshold = OverviewDashboardThresholdPolicy.Effective(reading.MetricId, _layout.MetricThresholds)!;
            alerts[reading.MetricId] = new OverviewDashboardMetricAlert(level, AlertStatus(rule, threshold, level));
        }
        return alerts;
    }

    internal static string AlertStatus(
        OverviewDashboardThresholdRule rule,
        OverviewDashboardMetricThreshold threshold,
        OverviewDashboardAlertLevel level)
    {
        var limit = level == OverviewDashboardAlertLevel.Critical ? threshold.Critical : threshold.Warning;
        var condition = rule.Kind switch
        {
            OverviewDashboardThresholdKind.Increasing => Loc.T("Dashboard.Alert.Increasing"),
            OverviewDashboardThresholdKind.Below => Loc.T("Dashboard.Alert.Below", FormatThreshold(limit, rule.Unit)),
            _ => Loc.T("Dashboard.Alert.Above", FormatThreshold(limit, rule.Unit))
        };
        return Loc.T(level == OverviewDashboardAlertLevel.Critical ? "Dashboard.Alert.Critical" : "Dashboard.Alert.Warning",
            condition);
    }

    internal static string FormatThreshold(double? value, string unit)
    {
        var number = value?.ToString("0.##", CultureInfo.CurrentCulture) ?? "";
        return unit switch
        {
            "" => number,
            "%" => $"{number}%",
            _ => $"{number} {unit}"
        };
    }

    private MenuItem ThresholdMenu(ContextMenu owner, OverviewDashboardCardLayout card)
    {
        var menu = Submenu(Loc.T("Dashboard.Thresholds"));
        foreach (var metricId in card.MetricIds.Where(OverviewDashboardThresholdPolicy.SupportsThresholds))
        {
            var definition = _registry.Definition(metricId);
            menu.Items.Add(AsyncItem(owner, definition.DisplayName, () => EditThresholdAsync(definition)));
        }
        menu.IsEnabled = menu.Items.Count > 0;
        return menu;
    }

    private async Task EditThresholdAsync(OverviewDashboardMetricDefinition definition)
    {
        var rule = OverviewDashboardThresholdPolicy.Rule(definition.Id);
        var current = OverviewDashboardThresholdPolicy.Effective(definition.Id, _layout.MetricThresholds);
        if (rule is null || current is null) return;
        var result = OverviewDashboardThresholdDialog.Show(definition.DisplayName, rule, current);
        if (result is not null)
            await SetMetricThresholdAsync(definition.Id, result == rule.Default ? null : result);
    }
}
