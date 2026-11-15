using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewDashboardUnreportedMetricsTests : WpfUiTestBase
{
    [Fact]
    public async Task UserAddedUnreportedMetricsStayVisibleWithReasonUntilReported()
    {
        await RunStaAsync(() =>
        {
            var dashboard = SingleCardDashboard(
            [
                OverviewDashboardMetricIds.AverageGenerationRate,
                OverviewDashboardMetricIds.ContextShifts,
                OverviewDashboardMetricIds.GpuTemperature(0),
                OverviewDashboardMetricIds.GatewayFailureRate
            ]);
            dashboard.ApplyHardwareSummary("CPU: Test CPU\nTelemetry: 20% load\nGPU 0: Test GPU | 40%");
            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic()));

            var card = Assert.Single(dashboard.Cards);
            Assert.Equal(Visibility.Visible, card.Root.Visibility);
            AssertUnreported(DashboardRow(dashboard, OverviewDashboardMetricIds.ContextShifts), "Dashboard.NotReported.Runtime");
            AssertUnreported(DashboardRow(dashboard, OverviewDashboardMetricIds.GpuTemperature(0)), "Dashboard.NotReported.Hardware");
            AssertUnreported(DashboardRow(dashboard, OverviewDashboardMetricIds.GatewayFailureRate), "Dashboard.NotReported.Gateway");

            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(contextShiftCount: 3)));

            var shifts = DashboardRow(dashboard, OverviewDashboardMetricIds.ContextShifts);
            Assert.False(shifts.IsUnreported);
            Assert.Contains("3", DashboardRowTexts(shifts));
            Assert.DoesNotContain(Loc.T("Dashboard.NotReported.Runtime"), DashboardRowTexts(shifts));
            Assert.Equal("", AutomationProperties.GetItemStatus(shifts.Root));
        });
    }

    [Fact]
    public async Task UnreportedMetricCanBeRemovedFromTheCardMenu()
    {
        await RunStaAsync(() =>
        {
            var dashboard = SingleCardDashboard(
                [OverviewDashboardMetricIds.AverageGenerationRate, OverviewDashboardMetricIds.ContextShifts]);
            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic()));
            var card = Assert.Single(dashboard.Cards);
            var menu = OpenContextMenu(card.Root);
            var remove = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Loc.T("Dashboard.RemoveMetric")));
            var shiftsItem = remove.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Loc.T("Dashboard.Metric.ContextShifts")));

            shiftsItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;

            Assert.DoesNotContain(OverviewDashboardMetricIds.ContextShifts, dashboard.Layout.Cards.Single().MetricIds);
        });
    }

    private static void AssertUnreported(OverviewDashboardMetricRowView row, string reasonKey)
    {
        Assert.True(row.IsUnreported);
        Assert.Equal(Visibility.Visible, row.Root.Visibility);
        Assert.Contains(Loc.T(reasonKey), DashboardRowTexts(row));
        Assert.Equal(Loc.T(reasonKey), AutomationProperties.GetItemStatus(row.Root));
    }
}
