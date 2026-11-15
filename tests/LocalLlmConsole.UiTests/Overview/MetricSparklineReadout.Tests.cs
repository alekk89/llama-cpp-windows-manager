using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfMetricSparklineReadoutTests : WpfUiTestBase
{
    [Fact]
    public async Task SparklineShowsSpanAndRevealsReadoutWhenItsRowHasKeyboardFocus()
    {
        await RunStaAsync(() =>
        {
            var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var graph = new MetricSparkline
            {
                Height = 30 + MetricSparkline.CaptionHeight,
                MetricName = "Average generation rate"
            };
            for (var index = 0; index < MetricSparkline.MaximumSamples; index++)
                graph.Push("runtime", index % 2 == 0 ? 60 : 55, sampledAt: start.AddSeconds(index * 2), unit: "t/s");
            var row = new Grid { Focusable = true, Width = 360 };
            row.Children.Add(graph);
            var other = new System.Windows.Controls.Button { Content = "Other" };
            var host = new StackPanel();
            host.Children.Add(row);
            host.Children.Add(other);
            var window = new Window { Content = host, Width = 420, Height = 200, ShowInTaskbar = false };
            try
            {
                window.Show();
                window.Dispatcher.Invoke(DispatcherPriority.ApplicationIdle, new Action(() => { }));

                Assert.Equal(Loc.T("Dashboard.ChartSpanMinutes", 2), graph.SpanLabel);
                Assert.Equal(new MetricSparklineSummary(55, 60, 55), graph.Summary);
                Assert.Equal(MetricSparkline.FormatReadout(graph.Summary!, "t/s"), graph.ReadoutText);
                Assert.Equal(new MetricSparklineRange(0, 80), graph.Range);
                Assert.Equal(Loc.T("Dashboard.ChartTooltip", "Average generation rate"), AutomationProperties.GetName(graph));
                var help = AutomationProperties.GetHelpText(graph);
                Assert.Contains(graph.SpanLabel, help, StringComparison.Ordinal);
                Assert.Contains(graph.ReadoutText, help, StringComparison.Ordinal);

                Keyboard.Focus(other);
                Assert.False(graph.IsReadoutVisible);
                var idleCaption = CaptionInk(graph);
                Assert.True(idleCaption > 0, "The span label should be drawn without hover or focus.");

                Keyboard.Focus(row);
                Assert.True(row.IsKeyboardFocused);
                Assert.True(graph.IsReadoutVisible);
                Assert.True(CaptionInk(graph) > idleCaption, "Focusing the metric row should draw the readout.");

                Keyboard.Focus(other);
                Assert.False(graph.IsReadoutVisible);
                Assert.Equal(idleCaption, CaptionInk(graph));
            }
            finally
            {
                window.Close();
            }
        });
    }

    [Fact]
    public async Task UnboundedChartsUseTheCardScaleAndPercentagesKeepTheirFixedRange()
    {
        await RunStaAsync(() =>
        {
            var start = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
            var stable = new MetricSparkline();
            var fitted = new MetricSparkline { Scale = OverviewDashboardChartScale.FitToData };
            var percent = new MetricSparkline { Scale = OverviewDashboardChartScale.FitToData, FixedMaximum = 100 };
            foreach (var graph in new[] { stable, fitted, percent })
            {
                graph.Push("runtime", 60, sampledAt: start);
                graph.Push("runtime", 55, sampledAt: start.AddSeconds(30));
            }

            Assert.Equal(0, stable.Range.Minimum);
            Assert.Equal(80, stable.Range.Maximum);
            Assert.True(fitted.Range.Minimum > 50);
            Assert.Equal(new MetricSparklineRange(0, 100), percent.Range);
            Assert.Equal(Loc.T("Dashboard.ChartSpanSeconds", 30), stable.SpanLabel);

            stable.Push("other-runtime", 5, sampledAt: start.AddMinutes(1));
            Assert.Equal(1, stable.SampleCount);
            Assert.Equal("", stable.SpanLabel);
            Assert.Equal(6, stable.Range.Maximum);
        });
    }

    [Fact]
    public async Task ChartMenuPersistsTheCardScaleForUnboundedCharts()
    {
        await RunStaAsync(() =>
        {
            OverviewDashboardLayout? persisted = null;
            var layout = OverviewDashboardLayoutPolicy.SetChartVisibility(
                new OverviewDashboardLayout(
                    OverviewDashboardLayoutPolicy.CurrentVersion,
                    [new OverviewDashboardCardLayout(
                        "rates",
                        [OverviewDashboardMetricIds.AverageGenerationRate],
                        Bounds: new OverviewDashboardCardBounds(0, 0, 4, 140))]),
                "rates",
                OverviewDashboardMetricIds.AverageGenerationRate,
                true);
            var dashboard = new OverviewDashboardController(layout, new OverviewDashboardControllerActions(
                next =>
                {
                    persisted = next;
                    return Task.CompletedTask;
                },
                action => action(),
                action => action()));
            var card = Assert.Single(dashboard.Cards);
            Assert.Equal(OverviewDashboardChartScale.Stable, card.Graphs[OverviewDashboardMetricIds.AverageGenerationRate].Scale);

            var menu = OpenContextMenu(card.Root);
            var chartMenu = menu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Loc.T("Dashboard.Chart")));
            var stableItem = chartMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Loc.T("Dashboard.ChartScaleStable")));
            var fitItem = chartMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, Loc.T("Dashboard.ChartScaleFit")));
            Assert.True(chartMenu.IsEnabled);
            Assert.True(stableItem.IsChecked);
            Assert.False(fitItem.IsChecked);
            fitItem.IsChecked = true;
            fitItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            menu.IsOpen = false;

            Assert.Equal(OverviewDashboardChartScale.FitToData, Assert.Single(persisted!.Cards).ChartScale);
            card = Assert.Single(dashboard.Cards);
            Assert.Equal(OverviewDashboardChartScale.FitToData, card.Graphs[OverviewDashboardMetricIds.AverageGenerationRate].Scale);
            Assert.False(stableItem.IsChecked);
        });
    }

    private static int CaptionInk(MetricSparkline graph)
    {
        graph.InvalidateVisual();
        graph.UpdateLayout();
        var width = (int)Math.Ceiling(graph.ActualWidth);
        var height = (int)Math.Ceiling(MetricSparkline.CaptionHeight);
        var bitmap = new RenderTargetBitmap(width, (int)Math.Ceiling(graph.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(graph);
        var pixels = new byte[width * height * 4];
        bitmap.CopyPixels(new Int32Rect(0, 0, width, height), pixels, width * 4, 0);
        var ink = 0;
        for (var index = 3; index < pixels.Length; index += 4)
        {
            if (pixels[index] > 0) ink++;
        }
        return ink;
    }
}
