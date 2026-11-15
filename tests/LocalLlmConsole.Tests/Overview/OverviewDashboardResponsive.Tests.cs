using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class OverviewDashboardResponsiveTests : ManagerRegressionTestBase
{
    private const double Gap = OverviewDashboardLayoutPolicy.CardGap;

    private static OverviewDashboardFlowItem Item(string id, int span, double height, double minimumWidth = 180)
        => new(id, span, minimumWidth, _ => height);

    [Fact]
    public void FlowFillsRowsInOrderAndSpansEndAtTheSurfaceEdge()
    {
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("a", 4, 100), Item("b", 4, 100), Item("c", 4, 100), Item("d", 6, 100)],
            1190);

        Assert.Equal(["a", "b", "c", "d"], placements.Select(item => item.CardId));
        Assert.Equal([0d, 400, 800], placements.Take(3).Select(item => item.Left));
        Assert.All(placements.Take(3), item => Assert.Equal(390, item.Width));
        Assert.Equal(1190, placements[2].Left + placements[2].Width);
        Assert.Equal(0, placements[3].Left);
        Assert.Equal(100 + Gap, placements[3].Top);
    }

    [Fact]
    public void FlowPacksLaterCardsIntoTheHighestFreeSlot()
    {
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("tall", 4, 300), Item("short", 4, 100), Item("other", 4, 100), Item("next", 4, 100)],
            1190);

        var next = placements.Single(item => item.CardId == "next");
        Assert.Equal(400, next.Left);
        Assert.Equal(100 + Gap, next.Top);
    }

    [Fact]
    public void FlowTreatsNearlyEqualSlotsAsOneRowAndReadsLeftToRight()
    {
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("a", 4, 206), Item("b", 4, 202), Item("c", 4, 202), Item("d", 4, 100)],
            1190);

        var next = placements.Single(item => item.CardId == "d");
        Assert.Equal(0, next.Left);
        Assert.Equal(206 + Gap, next.Top);
    }

    [Fact]
    public void FlowHeightsFollowContentAtTheRenderedWidth()
    {
        var placements = OverviewDashboardLayoutEngine.Flow(
            [new OverviewDashboardFlowItem("wraps", 4, 180, width => width < 300 ? 220 : 120)],
            1190);
        var narrow = OverviewDashboardLayoutEngine.Flow(
            [new OverviewDashboardFlowItem("wraps", 4, 180, width => width < 300 ? 220 : 120)],
            850);

        Assert.Equal(120, placements[0].Height);
        Assert.Equal(220, narrow[0].Height);
    }

    [Fact]
    public void FlowWidensCardsThatWouldBeNarrowerThanTheirContent()
    {
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("a", 4, 100), Item("b", 4, 100), Item("c", 4, 100)],
            500);

        Assert.All(placements, item => Assert.True(item.Width >= 180, $"{item.CardId} is {item.Width} wide."));
        Assert.All(placements, item => Assert.True(item.Left + item.Width <= 500 + .5));
        Assert.Equal(0, placements[0].Top);
        Assert.True(placements[2].Top > 0, "The third card wraps to the next row.");
    }

    [Fact]
    public void FlowWidensToEvenColumnsSoNarrowRowsShareTheWidthEqually()
    {
        // At 528 px a 4-column card is under 180 px; five columns would leave an
        // unusable remainder, so cards take half the row each.
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("a", 3, 100), Item("b", 3, 100), Item("c", 3, 100)],
            528);

        Assert.Equal(placements[0].Width, placements[1].Width, precision: 3);
        Assert.Equal(528, placements[1].Left + placements[1].Width, precision: 3);
        Assert.Equal(0, placements[2].Left);
        Assert.Equal(6, OverviewDashboardLayoutEngine.ResponsiveSpan(3, 5));
        Assert.Equal(7, OverviewDashboardLayoutEngine.ResponsiveSpan(7, 5));
        Assert.Equal(12, OverviewDashboardLayoutEngine.ResponsiveSpan(4, 7));
    }

    [Fact]
    public void FlowStacksEveryCardWhenOnlyOneFitsPerRow()
    {
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("a", 4, 100), Item("b", 4, 100)],
            300);

        Assert.All(placements, item => Assert.Equal(0, item.Left));
        Assert.All(placements, item => Assert.Equal(300, item.Width));
        Assert.Equal(100 + Gap, placements[1].Top);
    }

    [Fact]
    public void FlowGivesUnusableSliversToTheNeighbouringCard()
    {
        // Two 5-column cards leave two columns that no 180 px card can use.
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("a", 5, 100, 300), Item("b", 5, 100, 300)],
            1190);

        Assert.Equal(1190, placements[1].Left + placements[1].Width, precision: 3);
        Assert.Equal(500 - Gap, placements[0].Width, precision: 3);
    }

    [Fact]
    public void FlowKeepsSpaceThatAnotherCardCouldUse()
    {
        var placements = OverviewDashboardLayoutEngine.Flow(
            [Item("a", 4, 100), Item("b", 4, 100)],
            1190);

        Assert.Equal(790, placements[1].Left + placements[1].Width);
    }

    [Fact]
    public void LockedWidthsStayFixedAndWrapInsteadOfShrinking()
    {
        var items = new[] { Item("a", 4, 100), Item("b", 4, 100) };

        var atCapture = OverviewDashboardLayoutEngine.Flow(items, 890, 890);
        var wider = OverviewDashboardLayoutEngine.Flow(items, 1400, 890);
        var narrower = OverviewDashboardLayoutEngine.Flow(items, 500, 890);

        Assert.Equal(atCapture.Select(item => item.Width), wider.Select(item => item.Width));
        Assert.Equal(atCapture.Select(item => item.Width), narrower.Select(item => item.Width));
        Assert.Equal(0, narrower[1].Left);
        Assert.Equal(100 + Gap, narrower[1].Top);
    }

    [Theory]
    [InlineData(390, 4)]
    [InlineData(400, 4)]
    [InlineData(1190, 12)]
    [InlineData(10, 1)]
    public void SpanForWidthRoundsToTheNearestColumn(double width, int expected)
        => Assert.Equal(expected, OverviewDashboardLayoutEngine.SpanForWidth(width, OverviewDashboardLayoutEngine.ColumnPitch(1190)));

    [Fact]
    public void BoundsRecordColumnPositionSpanAndPixelHeight()
    {
        var bounds = OverviewDashboardLayoutEngine.ToBounds(new OverviewDashboardPlacement("a", 400, 110, 390, 152.4), 1190);

        Assert.Equal(new OverviewDashboardCardBounds(4, 110, 4, 152), bounds);
    }

    [Fact]
    public void LayoutsSavedBeforeFlowKeepTheirVisualOrder()
    {
        var legacy = new OverviewDashboardLayout(
            OverviewDashboardLayoutPolicy.CompactDefaultLayoutVersion,
            [
                new("lower", [OverviewDashboardMetricIds.Ram], Bounds: new(0, 300, 4, 112)),
                new("right", [OverviewDashboardMetricIds.Cpu], Bounds: new(8, 0, 4, 112)),
                new("left", [OverviewDashboardMetricIds.Gpu(0)], Bounds: new(0, 6, 4, 112)),
                new("unplaced", [OverviewDashboardMetricIds.Gpu(1)])
            ]);

        var migrated = OverviewDashboardLayoutPolicy.Normalize(legacy);

        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, migrated.Version);
        Assert.Equal(["left", "right", "lower", "unplaced"], migrated.Cards.Select(card => card.Id));
        Assert.Equal(migrated.Cards.Select(card => card.Id),
            OverviewDashboardLayoutPolicy.Normalize(migrated).Cards.Select(card => card.Id));
    }

    [Fact]
    public void SpanChangesAndRecordedPositionsKeepTheCardOrder()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var first = layout.Cards[0].Id;

        var widened = OverviewDashboardLayoutPolicy.SetCardSpan(layout, first, 7);
        Assert.Equal(7, OverviewDashboardLayoutPolicy.CardSpan(widened.Cards[0]));

        var positioned = OverviewDashboardLayoutPolicy.SetCardBounds(widened,
            new Dictionary<string, OverviewDashboardCardBounds> { [first] = new(0, 0, 7, 150) });
        Assert.Equal(layout.Cards.Select(card => card.Id), positioned.Cards.Select(card => card.Id));
        Assert.Equal(new OverviewDashboardCardBounds(0, 0, 7, 150), positioned.Cards[0].Bounds);
    }
}
