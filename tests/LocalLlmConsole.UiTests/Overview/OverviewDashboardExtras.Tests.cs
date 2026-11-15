using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewDashboardExtrasTests : WpfUiTestBase
{
    [Fact]
    public async Task EfficiencyReadingsRenderBesideTheirSourceMetrics()
    {
        await RunStaAsync(() =>
        {
            var dashboard = Dashboard(
                [OverviewDashboardMetricIds.DraftAcceptance, OverviewDashboardMetricIds.SpeculativeSpeedup,
                 OverviewDashboardMetricIds.PromptCacheReuse, OverviewDashboardMetricIds.PromptCacheHitRate], _ => { });
            var card = Assert.Single(dashboard.Cards);
            Assert.False(card.MetricRows[OverviewDashboardMetricIds.SpeculativeSpeedup].IsAvailable);

            dashboard.ApplyMetricSummary(Summary(RuntimeMetricAtomicSnapshot.Empty with
            {
                DraftAcceptancePercent = 50,
                SpeculativeTokensPerStep = 2.5,
                PromptCachedTokens = 800,
                PromptCacheReusePercent = 40,
                RecentPromptCacheHitPercent = 80,
                RecentPromptCachedTokens = 80,
                RecentPromptTokens = 100
            }));
            card.Root.UpdateLayout();

            var texts = VisualDescendants<TextBlock>(card.Root).Select(text => text.Text).ToArray();
            Assert.True(card.MetricRows[OverviewDashboardMetricIds.SpeculativeSpeedup].IsAvailable);
            Assert.Contains("2.50", texts);
            Assert.Contains("×", texts);
            Assert.Contains("2.50× tokens per verification step", texts);
            Assert.Contains("800 cached tokens · recent hit rate 80%", texts);
            Assert.Contains("80 of 100 recent prompt tokens cached", texts);

            dashboard.ApplyMetricSummary(Summary(RuntimeMetricAtomicSnapshot.Empty with { DraftAcceptancePercent = 50 }));
            Assert.False(card.MetricRows[OverviewDashboardMetricIds.SpeculativeSpeedup].IsAvailable);
            Assert.Contains("N/A", VisualDescendants<TextBlock>(card.Root).Select(text => text.Text));
        });
    }

    [Fact]
    public async Task CopyActionPlacesCardReadingsOnTheClipboardAndConfirms()
    {
        await RunStaAsync(() =>
        {
            var copied = new List<string>();
            var failClipboard = false;
            var dashboard = Dashboard([OverviewDashboardMetricIds.AverageGenerationRate, OverviewDashboardMetricIds.DraftAcceptance],
                text =>
                {
                    if (failClipboard) throw new ExternalException("Clipboard busy");
                    copied.Add(text);
                });
            dashboard.ApplyMetricSummary(Summary(RuntimeMetricAtomicSnapshot.Empty with
            {
                AverageGenerationRate = 31.5,
                DraftAcceptancePercent = 62
            }));
            var card = Assert.Single(dashboard.Cards);
            var copy = Assert.IsType<Button>(card.CopyButton);
            Assert.Equal(Visibility.Collapsed, copy.Visibility);
            Assert.Equal(LocalLlmConsole.Localization.Loc.T("Dashboard.CopyReadings"), AutomationProperties.GetName(copy));
            Assert.Contains("Ctrl+C", AutomationProperties.GetHelpText(card.Root), StringComparison.Ordinal);

            copy.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));

            var text = Assert.Single(copied);
            var lines = text.Split(Environment.NewLine);
            Assert.Equal("Average generation rate, Draft acceptance", lines[0]);
            Assert.StartsWith("Captured ", lines[1], StringComparison.Ordinal);
            Assert.Equal("Average generation rate: 31.5 t/s", lines[2]);
            Assert.Equal("Draft acceptance: 62%", lines[3]);
            Assert.True(card.IsCopyConfirmationVisible);
            Assert.Equal(Visibility.Visible, copy.Visibility);
            Assert.Equal(LocalLlmConsole.Localization.Loc.T("EndpointInspection.Copied"), AutomationProperties.GetName(copy));

            failClipboard = true;
            var menu = OpenContextMenu(card.Root);
            var copyItem = menu.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.CopyReadings")));
            copyItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Single(copied);
            Assert.Equal(LocalLlmConsole.Localization.Loc.T("EndpointInspection.CopyFailed"), AutomationProperties.GetName(copy));
            menu.IsOpen = false;
        });
    }

    private static OverviewDashboardController Dashboard(IReadOnlyList<string> metricIds, Action<string> copy)
    {
        var layout = new OverviewDashboardLayout(OverviewDashboardLayoutPolicy.CurrentVersion,
            [new OverviewDashboardCardLayout("card", metricIds, Bounds: new OverviewDashboardCardBounds(0, 0, 6, 112))]);
        var dashboard = new OverviewDashboardController(layout,
            new OverviewDashboardControllerActions(_ => Task.CompletedTask, action => action(), action => action(),
                CopyToClipboard: copy));
        dashboard.DashboardGrid.Measure(new Size(900, 800));
        dashboard.DashboardGrid.Arrange(new Rect(0, 0, 900, 800));
        dashboard.DashboardGrid.UpdateLayout();
        return dashboard;
    }

    private static RuntimeMetricSummaryPresentation Summary(RuntimeMetricAtomicSnapshot atomic)
        => RuntimeMetricSummaryPresentation.NoRuntime with
        {
            GraphSample = new RuntimeMetricGraphSample("model|runtime|8081", null, null, null, null, null),
            Atomic = atomic
        };
}
