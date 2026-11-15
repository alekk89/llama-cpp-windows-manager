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
