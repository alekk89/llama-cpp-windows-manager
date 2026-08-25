namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void OverviewDashboardVisiblePlacementAccountsForMeasuredContentHeight()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("first", 0, 0, 290, 112),
            new OverviewDashboardPlacement("second", 0, 122, 290, 112)
        };
        var minimums = new Dictionary<string, OverviewDashboardMinimumSize>
        {
            ["first"] = new(180, 160),
            ["second"] = new(180, 112)
        };

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 900);

        Assert.Equal(160, resolved[0].Height);
        Assert.Equal(170, resolved[1].Top);
    }

    [Fact]
    public void OverviewDashboardVisiblePlacementMovesExpandedCardsOutOfOverlap()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("first", 0, 0, 290, 112),
            new OverviewDashboardPlacement("second", 300, 0, 290, 112)
        };
        var minimums = new Dictionary<string, OverviewDashboardMinimumSize>
        {
            ["first"] = new(310, 112),
            ["second"] = new(290, 112)
        };

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 600);

        Assert.Equal(0, resolved[0].Top);
        Assert.Equal(122, resolved[1].Top);
    }

    [Fact]
    public void OverviewDashboardVisiblePlacementCompressesWideCardsBeforeWrappingTheRow()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("wide", 0, 0, 290, 112),
            new OverviewDashboardPlacement("second", 300, 0, 180, 112),
            new OverviewDashboardPlacement("third", 490, 0, 180, 112)
        };
        var minimums = desired.ToDictionary(
            placement => placement.CardId,
            _ => new OverviewDashboardMinimumSize(180, 112));

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 600);

        Assert.Equal([0, 0, 0], resolved.Select(placement => placement.Top));
        Assert.Equal([220, 180, 180], resolved.Select(placement => placement.Width));
        Assert.Equal([0, 230, 420], resolved.Select(placement => placement.Left));
    }

    [Fact]
    public void OverviewDashboardVisiblePlacementReclaimsRowGapsBeforeWrapping()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("first", 0, 0, 180, 112),
            new OverviewDashboardPlacement("second", 250, 0, 180, 112),
            new OverviewDashboardPlacement("third", 500, 0, 180, 112)
        };
        var minimums = desired.ToDictionary(
            placement => placement.CardId,
            _ => new OverviewDashboardMinimumSize(180, 112));

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 600);

        Assert.Equal([0, 0, 0], resolved.Select(placement => placement.Top));
        Assert.Equal([180, 180, 180], resolved.Select(placement => placement.Width));
        Assert.Equal([0, 190, 380], resolved.Select(placement => placement.Left));
    }

    [Fact]
    public void OverviewDashboardVisiblePlacementFillsEachResponsiveRowBeforeWrappingAnotherCard()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("gpu-0", 329, 0, 153, 316),
            new OverviewDashboardPlacement("status", 0, 0, 153, 358),
            new OverviewDashboardPlacement("gpu-2", 654, 0, 153, 329),
            new OverviewDashboardPlacement("gpu-1", 491, 0, 153, 316),
            new OverviewDashboardPlacement("cpu", 163, 0, 156, 316)
        };
        var minimums = desired.ToDictionary(
            placement => placement.CardId,
            _ => new OverviewDashboardMinimumSize(180, 112));

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 890)
            .ToDictionary(placement => placement.CardId, StringComparer.Ordinal);

        Assert.Equal((0d, 0d), (resolved["status"].Left, resolved["status"].Top));
        Assert.Equal((190d, 0d), (resolved["cpu"].Left, resolved["cpu"].Top));
        Assert.Equal((380d, 0d), (resolved["gpu-0"].Left, resolved["gpu-0"].Top));
        Assert.Equal((570d, 0d), (resolved["gpu-1"].Left, resolved["gpu-1"].Top));
        Assert.Equal((0d, 368d), (resolved["gpu-2"].Left, resolved["gpu-2"].Top));
    }

    [Fact]
    public void OverviewDashboardVisiblePlacementReclaimsRightSpaceAfterMinimumWidthExpansion()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("gpu-0", 364, 0, 170, 316),
            new OverviewDashboardPlacement("status", 0, 0, 170, 358),
            new OverviewDashboardPlacement("gpu-2", 723, 0, 170, 329),
            new OverviewDashboardPlacement("gpu-1", 543, 0, 170, 316),
            new OverviewDashboardPlacement("cpu", 180, 0, 174, 316)
        };
        var minimums = desired.ToDictionary(
            placement => placement.CardId,
            _ => new OverviewDashboardMinimumSize(180, 112));

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 983)
            .ToDictionary(placement => placement.CardId, StringComparer.Ordinal);

        Assert.Equal((0d, 0d), (resolved["status"].Left, resolved["status"].Top));
        Assert.Equal((190d, 0d), (resolved["cpu"].Left, resolved["cpu"].Top));
        Assert.Equal((380d, 0d), (resolved["gpu-0"].Left, resolved["gpu-0"].Top));
        Assert.Equal((570d, 0d), (resolved["gpu-1"].Left, resolved["gpu-1"].Top));
        Assert.Equal((760d, 0d), (resolved["gpu-2"].Left, resolved["gpu-2"].Top));
    }

    [Fact]
    public void OverviewDashboardVisiblePlacementBackfillsWrappedRowSpaceBeforeUsingAnotherRow()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("wide", 0, 0, 400, 112),
            new OverviewDashboardPlacement("medium", 410, 0, 300, 112),
            new OverviewDashboardPlacement("small", 720, 0, 180, 112)
        };
        var minimums = desired.ToDictionary(
            placement => placement.CardId,
            placement => new OverviewDashboardMinimumSize(placement.Width, 112));

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 600)
            .ToDictionary(placement => placement.CardId, StringComparer.Ordinal);

        Assert.Equal((0d, 0d), (resolved["wide"].Left, resolved["wide"].Top));
        Assert.Equal((410d, 0d), (resolved["small"].Left, resolved["small"].Top));
        Assert.Equal((0d, 122d), (resolved["medium"].Left, resolved["medium"].Top));
    }

    [Fact]
    public void OverviewDashboardVisiblePlacementDoesNotMergeRowsAfterContentHeightExpansion()
    {
        var desired = new[]
        {
            new OverviewDashboardPlacement("first", 0, 0, 290, 112),
            new OverviewDashboardPlacement("peer", 300, 0, 290, 112),
            new OverviewDashboardPlacement("next-row", 0, 122, 290, 112)
        };
        var minimums = new Dictionary<string, OverviewDashboardMinimumSize>
        {
            ["first"] = new(180, 160),
            ["peer"] = new(180, 112),
            ["next-row"] = new(180, 112)
        };

        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 600);

        Assert.Equal(290, resolved.Single(item => item.CardId == "next-row").Width);
        Assert.Equal(170, resolved.Single(item => item.CardId == "next-row").Top);
    }
}
