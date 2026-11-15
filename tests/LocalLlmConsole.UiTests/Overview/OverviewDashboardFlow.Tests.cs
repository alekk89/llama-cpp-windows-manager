using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewDashboardFlowTests : WpfUiTestBase
{
    [Fact]
    public async Task DroppingACardOnAnotherSlotReordersAndSavesTheFlow()
    {
        await RunStaAsync(async () =>
        {
            OverviewDashboardLayout? persisted = null;
            var dashboard = Dashboard(900, layout => persisted = layout);
            var pitch = OverviewDashboardLayoutEngine.ColumnPitch(900);
            Assert.Equal(["cpu", "gpu-0", "status"], VisibleIds(dashboard));
            var status = Card(dashboard, "status");

            var target = dashboard.DropIndexAt("cpu", new Point(Canvas.GetLeft(status.Root) + 6, Canvas.GetTop(status.Root) + 4));
            Assert.Equal(2, target);
            await dashboard.MoveCardAsync("cpu", target);
            Arrange(dashboard, 900);

            Assert.Equal(["gpu-0", "status", "cpu"], VisibleIds(dashboard));
            Assert.Equal([0, 4 * pitch, 8 * pitch], VisibleIds(dashboard).Select(id => Canvas.GetLeft(Card(dashboard, id).Root)));
            // The hidden card keeps its place relative to its visible neighbours.
            Assert.Equal(["gpu-0", "default-gpu-2", "status", "cpu"], dashboard.Layout.Cards.Select(card => card.Id));
            Assert.NotNull(persisted);
            Assert.Equal(dashboard.Layout.Cards.Select(card => card.Id), persisted!.Cards.Select(card => card.Id));
            var cpuBounds = persisted.Cards.Single(card => card.Id == "cpu").Bounds!;
            Assert.Equal(8, cpuBounds.X);
            Assert.Equal(0, cpuBounds.Y);
            Assert.Equal(4, cpuBounds.Width);
        });
    }

    [Fact]
    public async Task WideningACardSnapsToColumnsAndWrapsTheCardsAfterIt()
    {
        await RunStaAsync(async () =>
        {
            var dashboard = Dashboard(900, _ => { });
            var pitch = OverviewDashboardLayoutEngine.ColumnPitch(900);

            await dashboard.SetCardSpanAsync("gpu-0", 8);
            Arrange(dashboard, 900);

            var gpu = Card(dashboard, "gpu-0");
            var status = Card(dashboard, "status");
            Assert.Equal(OverviewDashboardLayoutEngine.SpanWidth(8, pitch), gpu.Root.Width, precision: 3);
            Assert.Equal(8, OverviewDashboardLayoutPolicy.CardSpan(gpu.Layout));
            Assert.Equal(0, Canvas.GetLeft(status.Root));
            Assert.True(Canvas.GetTop(status.Root) >= gpu.Root.Height + OverviewDashboardLayoutPolicy.CardGap - .5);
        });
    }

    [Fact]
    public async Task NarrowWindowsStackCardsAtFullWidthWithoutOverlap()
    {
        await RunStaAsync(() =>
        {
            var dashboard = Dashboard(360, _ => { });
            var visible = VisibleIds(dashboard).Select(id => Card(dashboard, id)).ToArray();

            Assert.All(visible, card => Assert.Equal(0, Canvas.GetLeft(card.Root)));
            Assert.All(visible, card => Assert.Equal(360, card.Root.Width));
            for (var index = 1; index < visible.Length; index++)
                Assert.True(Canvas.GetTop(visible[index].Root)
                            >= Canvas.GetTop(visible[index - 1].Root) + visible[index - 1].Root.Height + OverviewDashboardLayoutPolicy.CardGap - .5);
            return Task.CompletedTask;
        });
    }

    private static OverviewDashboardController Dashboard(double width, Action<OverviewDashboardLayout> persist)
    {
        var layout = OverviewDashboardLayoutPolicy.Normalize(new OverviewDashboardLayout(
            OverviewDashboardLayoutPolicy.CurrentVersion,
            [
                new("cpu", [OverviewDashboardMetricIds.Cpu], Bounds: new(0, 0, 4, 112)),
                new("gpu-0", [OverviewDashboardMetricIds.Gpu(0)], Bounds: new(4, 0, 4, 112)),
                new("default-gpu-2", [OverviewDashboardMetricIds.Gpu(2)], Bounds: new(8, 0, 4, 112)),
                new("status", [OverviewDashboardMetricIds.ModelStatus], Bounds: new(8, 0, 4, 112))
            ]));
        var dashboard = new OverviewDashboardController(layout,
            new OverviewDashboardControllerActions(next =>
            {
                persist(next);
                return Task.CompletedTask;
            }, action => action()));
        dashboard.SetMetricValue(OverviewDashboardMetricIds.ModelStatus, "Ready");
        dashboard.ApplyHardwareSummary(
            "CPU: AMD Ryzen 9 7950X\nTelemetry: 24.0% load | 16C/32T\n" +
            "GPU 0: AMD Radeon RX 7900 XTX | 61.0% | 8.0/24.0 GiB");
        Arrange(dashboard, width);
        return dashboard;
    }

    private static void Arrange(OverviewDashboardController dashboard, double width)
    {
        dashboard.DashboardGrid.Measure(new Size(width, 2000));
        dashboard.DashboardGrid.Arrange(new Rect(0, 0, width, 2000));
        dashboard.DashboardGrid.UpdateLayout();
    }

    private static string[] VisibleIds(OverviewDashboardController dashboard)
        => dashboard.Cards.Where(card => card.Root.Visibility == Visibility.Visible)
            .Select(card => card.Layout.Id)
            .ToArray();

    private static OverviewDashboardCardView Card(OverviewDashboardController dashboard, string id)
        => dashboard.Cards.Single(card => card.Layout.Id == id);
}
