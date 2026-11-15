using System.Windows.Automation;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewDashboardFreshnessTests : WpfUiTestBase
{
    [Fact]
    public async Task StaleRuntimeReadingsAreDimmedAndAgeWithoutNewSamples()
    {
        await RunStaAsync(() =>
        {
            var clock = new ManualTimeProvider(new DateTimeOffset(2026, 11, 15, 12, 0, 0, TimeSpan.Zero));
            var dashboard = SingleCardDashboard(
                [OverviewDashboardMetricIds.AverageGenerationRate, OverviewDashboardMetricIds.Cpu],
                clock);
            dashboard.ApplyHardwareSummary("CPU: Test CPU\nTelemetry: 20% load");
            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic()));
            var card = Assert.Single(dashboard.Cards);
            var rate = DashboardRow(dashboard, OverviewDashboardMetricIds.AverageGenerationRate);
            var cpu = DashboardRow(dashboard, OverviewDashboardMetricIds.Cpu);
            Assert.False(rate.IsStale);
            Assert.Equal("", card.FreshnessText);
            Assert.Equal(1, DashboardValueLine(rate).Opacity);

            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(), clock.Now.AddMinutes(-2)));

            Assert.True(rate.IsStale);
            Assert.False(cpu.IsStale);
            Assert.True(DashboardValueLine(rate).Opacity < 1);
            Assert.Equal(1, DashboardValueLine(cpu).Opacity);
            Assert.Equal("Last updated 2m ago", card.FreshnessText);
            Assert.Equal("Last updated 2m ago", AutomationProperties.GetItemStatus(card.Root));
            Assert.Equal("Last updated 2m ago", AutomationProperties.GetItemStatus(rate.Root));
            Assert.Equal("", AutomationProperties.GetItemStatus(cpu.Root));

            // Only the hardware tick arrives; the stale age still advances.
            clock.Now = clock.Now.AddMinutes(3);
            dashboard.ApplyHardwareSummary("CPU: Test CPU\nTelemetry: 22% load");
            Assert.Equal("Last updated 5m ago", card.FreshnessText);

            dashboard.ApplyMetricSummary(DashboardSummary(DashboardAtomic(generationRate: 40)));

            Assert.False(rate.IsStale);
            Assert.Equal(1, DashboardValueLine(rate).Opacity);
            Assert.Equal("", card.FreshnessText);
            Assert.Equal("", AutomationProperties.GetItemStatus(card.Root));
            Assert.Equal("", AutomationProperties.GetItemStatus(rate.Root));
        });
    }
}
