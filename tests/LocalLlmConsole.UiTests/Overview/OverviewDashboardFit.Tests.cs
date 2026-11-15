using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewDashboardFitTests : WpfUiTestBase
{
    private const string FourGpuHost =
        "CPU: Intel(R) Core(TM) Ultra 7 270K Plus\nTelemetry: 21.0% load | 24C/24T\n" +
        "GPU 0: NVIDIA GeForce RTX 3090 | 0.0% | 0.0/24.0 GiB\n" +
        "GPU 1: NVIDIA GeForce RTX 3090 | 17.0% | 2.9/24.0 GiB\n" +
        "GPU 2: NVIDIA GeForce RTX 3090 | 0.0% | 0.0/24.0 GiB\n" +
        "GPU 3: NVIDIA GeForce RTX 3090 | 0.0% | 0.0/24.0 GiB";

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    public async Task NarrowCardsShowEveryRowAtEveryDisplayScale(double scale)
    {
        await RunStaAsync(async () =>
        {
            var dashboard = Dashboard();
            var host = new Border { UseLayoutRounding = true, SnapsToDevicePixels = true, Child = dashboard.Root };
            TextOptions.SetTextFormattingMode(host, TextFormattingMode.Display);
            VisualTreeHelper.SetRootDpi(host, new DpiScale(scale, scale));
            await FillAsync(dashboard);
            for (var pass = 0; pass < 3; pass++)
            {
                host.Measure(new Size(1210 / scale, 2000));
                host.Arrange(new Rect(0, 0, 1210 / scale, 2000));
                host.UpdateLayout();
                DrainDispatcher();
            }

            AssertEveryCardFits(dashboard);
        });
    }

    [Fact]
    public async Task CardsGrowWhenTheirTextGrowsAfterTheyWerePlaced()
    {
        await RunStaAsync(async () =>
        {
            var dashboard = Dashboard();
            var window = new Window
            {
                Width = 1210,
                Height = 1000,
                Left = -20000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                Content = dashboard.Root
            };
            try
            {
                window.Show();
                await FillAsync(dashboard);
                Settle(window);
                AssertEveryCardFits(dashboard);

                // Late font, DPI or style changes re-measure text without any
                // reading changing; the card must notice that its rows grew.
                var runtime = dashboard.Cards.Single(card => card.Layout.Id == "default-runtime");
                var before = runtime.Root.ActualHeight;
                foreach (var text in VisualDescendants<TextBlock>(runtime.Root))
                    text.FontSize *= 1.3;
                Settle(window);

                Assert.True(runtime.Root.ActualHeight > before + 10,
                    $"Card stayed {runtime.Root.ActualHeight} high (was {before}).");
                AssertEveryCardFits(dashboard);
            }
            finally
            {
                window.Content = null;
                window.Close();
            }
        });
    }

    private static OverviewDashboardController Dashboard()
        => new(OverviewDashboardLayoutPolicy.Normalize(OverviewDashboardLayoutPolicy.Default),
            new OverviewDashboardControllerActions(_ => Task.CompletedTask, action => action()));

    private static async Task FillAsync(OverviewDashboardController dashboard)
    {
        dashboard.ApplyHardwareSummary(FourGpuHost);
        foreach (var metricId in dashboard.Layout.Cards.Single(card => card.Id == "default-runtime").MetricIds)
            dashboard.SetMetricValue(metricId, "N/A");
        foreach (var id in dashboard.Layout.Cards.Select(card => card.Id).ToArray())
            await dashboard.SetCardSpanAsync(id, 2);
    }

    private static void AssertEveryCardFits(OverviewDashboardController dashboard)
    {
        var problems = new List<string>();
        foreach (var card in dashboard.Cards.Where(card => card.Root.Visibility == Visibility.Visible))
        {
            var bottom = LastRowBottom((FrameworkElement)card.Root.Child);
            var inner = card.Root.ActualHeight - card.Root.Padding.Bottom - card.Root.BorderThickness.Bottom;
            if (bottom > inner + .5)
                problems.Add($"{card.Layout.Id}: rows end at {bottom:0.##} but the card ends at {inner:0.##}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    /// <summary>Bottom of the lowest shown element, relative to the card border.</summary>
    private static double LastRowBottom(FrameworkElement content)
    {
        var border = (FrameworkElement)VisualTreeHelper.GetParent(content);
        return VisualDescendants<FrameworkElement>(content)
            .Where(element => element.ActualHeight > 0 && Shown(element, content))
            .Select(element => element.TransformToAncestor(border).TransformBounds(
                new Rect(0, 0, element.ActualWidth, element.ActualHeight)).Bottom)
            .DefaultIfEmpty(0)
            .Max();
    }

    private static bool Shown(FrameworkElement element, FrameworkElement root)
    {
        for (DependencyObject? current = element;
             current is not null && !ReferenceEquals(current, root);
             current = VisualTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible }) return false;
        }
        return true;
    }

    private static void Settle(Window window)
    {
        for (var pass = 0; pass < 3; pass++)
        {
            window.UpdateLayout();
            DrainDispatcher();
        }
    }

    private static void DrainDispatcher()
    {
        var frame = new System.Windows.Threading.DispatcherFrame();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            () => frame.Continue = false);
        System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
}
