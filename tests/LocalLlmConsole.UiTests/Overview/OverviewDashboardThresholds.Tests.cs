using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewDashboardThresholdsTests : WpfUiTestBase
{
    [Fact]
    public async Task KvCacheThresholdsShowIconBrushAndAccessibleStatus()
    {
        await RunStaAsync(() =>
        {
            var dashboard = SingleCardDashboard(
                [OverviewDashboardMetricIds.KvCacheUsage, OverviewDashboardMetricIds.AverageGenerationRate]);
            var window = new Window { Content = dashboard.Root, Width = 900, Height = 600, ShowInTaskbar = false };
            window.Show();
            try
            {
                AssertKvCacheStates(dashboard);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertKvCacheStates(OverviewDashboardController dashboard)
    {
        var kv = DashboardRow(dashboard, OverviewDashboardMetricIds.KvCacheUsage);

        dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(kvCacheUsagePercent: 50)));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, kv.AlertLevel);
        Assert.Null(AlertGlyph(kv));
        Assert.Equal("", AutomationProperties.GetItemStatus(kv.Root));

        dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(kvCacheUsagePercent: 90)));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, kv.AlertLevel);
        Assert.Equal(OverviewDashboardMetricRowView.WarningGlyph, AlertGlyph(kv)!.Text);
        Assert.Equal("Warning: above 85%", AutomationProperties.GetItemStatus(kv.Root));
        AssertFollowsThemeBrush(ValueText(kv, "90"), "Warning");

        dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(kvCacheUsagePercent: 98)));
        Assert.Equal(OverviewDashboardAlertLevel.Critical, kv.AlertLevel);
        Assert.Equal(OverviewDashboardMetricRowView.CriticalGlyph, AlertGlyph(kv)!.Text);
        Assert.Equal("Critical: above 95%", AutomationProperties.GetItemStatus(kv.Root));
        AssertFollowsThemeBrush(ValueText(kv, "98"), "Danger");

        // Hysteresis: just below the critical level keeps the state.
        dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(kvCacheUsagePercent: 94)));
        Assert.Equal(OverviewDashboardAlertLevel.Critical, kv.AlertLevel);

        dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(kvCacheUsagePercent: 40)));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, kv.AlertLevel);
        Assert.Null(AlertGlyph(kv));
        AssertFollowsThemeBrush(ValueText(kv, "40"), "TextMain");
        Assert.Equal(OverviewDashboardAlertLevel.Normal, DashboardRow(dashboard, OverviewDashboardMetricIds.AverageGenerationRate).AlertLevel);
    }

    [Fact]
    public async Task AdjustedAndDisabledThresholdsPersistAndApply()
    {
        await RunStaAsync(async () =>
        {
            OverviewDashboardLayout? persisted = null;
            var layout = new OverviewDashboardLayout(
                OverviewDashboardLayoutPolicy.CurrentVersion,
                [new OverviewDashboardCardLayout("card", [OverviewDashboardMetricIds.QueuedRequests],
                    Bounds: new OverviewDashboardCardBounds(0, 0, 6, 200))]);
            var dashboard = new OverviewDashboardController(layout, new OverviewDashboardControllerActions(
                saved => { persisted = saved; return Task.CompletedTask; }, action => action(), action => action()));
            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(queuedRequests: 2)));
            Assert.Equal(OverviewDashboardAlertLevel.Warning, DashboardRow(dashboard, OverviewDashboardMetricIds.QueuedRequests).AlertLevel);

            await dashboard.SetMetricThresholdAsync(OverviewDashboardMetricIds.QueuedRequests,
                new OverviewDashboardMetricThreshold(true, 3, 10));
            Assert.Equal(new OverviewDashboardMetricThreshold(true, 3, 10),
                persisted!.MetricThresholds![OverviewDashboardMetricIds.QueuedRequests]);
            Assert.Equal(OverviewDashboardAlertLevel.Normal, DashboardRow(dashboard, OverviewDashboardMetricIds.QueuedRequests).AlertLevel);

            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(queuedRequests: 12)));
            Assert.Equal(OverviewDashboardAlertLevel.Critical, DashboardRow(dashboard, OverviewDashboardMetricIds.QueuedRequests).AlertLevel);

            await dashboard.SetMetricThresholdAsync(OverviewDashboardMetricIds.QueuedRequests,
                new OverviewDashboardMetricThreshold(false, 3, 10));
            Assert.Equal(OverviewDashboardAlertLevel.Normal, DashboardRow(dashboard, OverviewDashboardMetricIds.QueuedRequests).AlertLevel);

            await dashboard.SetMetricThresholdAsync(OverviewDashboardMetricIds.QueuedRequests, null);
            Assert.Null(persisted.MetricThresholds);
            Assert.Equal(OverviewDashboardAlertLevel.Warning, DashboardRow(dashboard, OverviewDashboardMetricIds.QueuedRequests).AlertLevel);
        });
    }

    [Fact]
    public async Task CardMenuOffersThresholdsOnlyForSupportedMetrics()
    {
        await RunStaAsync(() =>
        {
            var dashboard = SingleCardDashboard(
                [OverviewDashboardMetricIds.KvCacheUsage, OverviewDashboardMetricIds.AverageGenerationRate]);
            var menu = OpenContextMenu(Assert.Single(dashboard.Cards).Root);
            var thresholds = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Loc.T("Dashboard.Thresholds")));
            menu.IsOpen = false;

            Assert.True(thresholds.IsEnabled);
            Assert.Equal([Loc.T("Dashboard.Metric.KvCacheUsage")],
                thresholds.Items.OfType<MenuItem>().Select(item => (string)item.Header).ToArray());

            var unsupported = SingleCardDashboard([OverviewDashboardMetricIds.AverageGenerationRate]);
            var unsupportedMenu = OpenContextMenu(Assert.Single(unsupported.Cards).Root);
            Assert.False(unsupportedMenu.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, Loc.T("Dashboard.Thresholds"))).IsEnabled);
            unsupportedMenu.IsOpen = false;
        });
    }

    [Fact]
    public async Task ThresholdEditorValidatesAndRestoresDefaults()
    {
        await RunStaAsync(() =>
        {
            var rule = OverviewDashboardThresholdPolicy.Rule(OverviewDashboardMetricIds.KvCacheUsage)!;
            var editor = new OverviewDashboardThresholdEditor(rule, new OverviewDashboardMetricThreshold(false, 70, 80));
            Assert.False(editor.Enabled.IsChecked);
            Assert.False(editor.Warning.IsEnabled);
            Assert.Equal("70", editor.Warning.Text);
            Assert.Contains(VisualDescendants<Label>(editor.Root), label => ReferenceEquals(label.Target, editor.Warning));
            Assert.Contains(VisualDescendants<Label>(editor.Root), label => ReferenceEquals(label.Target, editor.Critical));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(editor.Warning)));

            editor.Enabled.IsChecked = true;
            editor.Warning.Text = "90";
            editor.Critical.Text = "80";
            Assert.False(editor.TryRead(out _));
            Assert.Equal(Visibility.Visible, editor.Error.Visibility);

            editor.Critical.Text = "";
            Assert.True(editor.TryRead(out var threshold));
            Assert.Equal(new OverviewDashboardMetricThreshold(true, 90, null), threshold);
            Assert.Equal(Visibility.Collapsed, editor.Error.Visibility);

            editor.Warning.Text = "abc";
            Assert.False(editor.TryRead(out _));

            editor.RestoreDefaults();
            Assert.True(editor.TryRead(out threshold));
            Assert.Equal(rule.Default, threshold);
        });
    }

    private static TextBlock? AlertGlyph(OverviewDashboardMetricRowView row)
        => DashboardValueLine(row).Children.OfType<TextBlock>()
            .SingleOrDefault(text => Grid.GetColumn(text) == 2 && text.Visibility == Visibility.Visible);

    private static TextBlock ValueText(OverviewDashboardMetricRowView row, string value)
        => DashboardValueLine(row).Children.OfType<TextBlock>().Single(text => text.Text == value);

    /// <summary>
    /// Swaps the theme resource for a marker brush to prove the value follows
    /// the active theme (light, dark, or high contrast), then restores it.
    /// </summary>
    private static void AssertFollowsThemeBrush(TextBlock text, string key)
    {
        var resources = Application.Current.Resources;
        var original = resources[key];
        var marker = new SolidColorBrush(Color.FromRgb(1, 2, 3));
        try
        {
            resources[key] = marker;
            Assert.Same(marker, text.Foreground);
        }
        finally
        {
            resources[key] = original;
        }
        Assert.Same(original, text.Foreground);
    }
}
