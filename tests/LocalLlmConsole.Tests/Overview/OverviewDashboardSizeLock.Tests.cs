using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class OverviewDashboardSizeLockTests : ManagerRegressionTestBase
{
    [Fact]
    public void OverviewDashboardSizeLockCapturesGeometryAndMigratesSafely()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var first = layout.Cards[0];
        var captured = new OverviewDashboardCardBounds(1, 24, 5, 140);

        var locked = OverviewDashboardLayoutPolicy.SetCardSizesLocked(
            layout,
            true,
            960,
            new Dictionary<string, OverviewDashboardCardBounds>
            {
                [first.Id] = captured
            });

        Assert.True(locked.CardSizesLocked);
        Assert.Equal(960, locked.LockedSurfaceWidth);
        Assert.Equal(captured, locked.Cards[0].Bounds);
        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, locked.Version);
        var renormalized = OverviewDashboardLayoutPolicy.Normalize(locked);
        Assert.True(renormalized.CardSizesLocked);
        Assert.Equal(locked.LockedSurfaceWidth, renormalized.LockedSurfaceWidth);
        Assert.Equal(locked.Cards.Count, renormalized.Cards.Count);
        for (var index = 0; index < locked.Cards.Count; index++)
        {
            Assert.Equal(locked.Cards[index].Id, renormalized.Cards[index].Id);
            Assert.Equal(locked.Cards[index].Bounds, renormalized.Cards[index].Bounds);
            Assert.Equal(locked.Cards[index].MetricIds, renormalized.Cards[index].MetricIds);
            Assert.Equal(locked.Cards[index].ChartMetricIds, renormalized.Cards[index].ChartMetricIds);
        }

        var unlocked = OverviewDashboardLayoutPolicy.SetCardSizesLocked(
            locked,
            false,
            720,
            new Dictionary<string, OverviewDashboardCardBounds>
            {
                [first.Id] = captured with { Width = 6 }
            });
        Assert.False(unlocked.CardSizesLocked);
        Assert.Equal(0, unlocked.LockedSurfaceWidth);
        Assert.Equal(6, unlocked.Cards[0].Bounds!.Width);

        var legacy = new OverviewDashboardLayout(
            OverviewDashboardLayoutPolicy.ObservedEnergyLayoutVersion,
            layout.Cards,
            CardSizesLocked: true,
            LockedSurfaceWidth: 960);
        var migrated = OverviewDashboardLayoutPolicy.Normalize(legacy);
        Assert.False(migrated.CardSizesLocked);
        Assert.Equal(0, migrated.LockedSurfaceWidth);
    }

    [Fact]
    public void OverviewDashboardLockedCardWidthsStayFixedAndWrapBeforeShrinking()
    {
        var cards = new[]
        {
            new OverviewDashboardCardLayout(
                "first",
                [OverviewDashboardMetricIds.Cpu],
                Bounds: new OverviewDashboardCardBounds(0, 0, 4, 112)),
            new OverviewDashboardCardLayout(
                "second",
                [OverviewDashboardMetricIds.Ram],
                Bounds: new OverviewDashboardCardBounds(4, 0, 4, 112))
        };

        var atCaptureWidth = OverviewDashboardLayoutEngine.Place(cards, 900, 900);
        var atLargerWidth = OverviewDashboardLayoutEngine.Place(cards, 1200, 900);
        Assert.Equal(atCaptureWidth.Select(item => item.Width), atLargerWidth.Select(item => item.Width));
        Assert.NotEqual(atCaptureWidth[1].Left, atLargerWidth[1].Left);

        var desired = OverviewDashboardLayoutEngine.Place(cards, 550, 900);
        var minimums = desired.ToDictionary(
            placement => placement.CardId,
            placement => new OverviewDashboardMinimumSize(placement.Width, placement.Height),
            StringComparer.OrdinalIgnoreCase);
        var resolved = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(desired, minimums, 550);

        Assert.Equal(desired.Select(item => item.Width), resolved.Select(item => item.Width));
        Assert.Equal(0, resolved[0].Top);
        Assert.True(resolved[1].Top >= resolved[0].Height + OverviewDashboardLayoutPolicy.CardGap);
    }

    [Fact]
    public async Task StateStorePersistsDashboardSizeLockReference()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var layout = OverviewDashboardLayoutPolicy.SetCardSizesLocked(
            OverviewDashboardLayoutPolicy.Default,
            true,
            912.5,
            new Dictionary<string, OverviewDashboardCardBounds>());
        var settings = AppSettings.CreateDefault(root) with { OverviewDashboardLayout = layout };

        await store.SaveAppSettingsAsync(settings);
        var reloaded = await store.GetAppSettingsAsync(root);

        Assert.NotNull(reloaded.OverviewDashboardLayout);
        Assert.True(reloaded.OverviewDashboardLayout.CardSizesLocked);
        Assert.Equal(912.5, reloaded.OverviewDashboardLayout.LockedSurfaceWidth);
        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, reloaded.OverviewDashboardLayout.Version);
    }
}
