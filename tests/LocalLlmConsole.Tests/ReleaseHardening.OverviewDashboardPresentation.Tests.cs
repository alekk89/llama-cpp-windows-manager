using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void OverviewDashboardDefaultUsesCurrentProductionCardLayout()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var defaults = AppSettings.CreateDefault(CreateTempRoot());

        Assert.Equal(["default-runtime", "default-host"], layout.Cards.Select(card => card.Id));
        Assert.Equal(12, layout.Cards.Sum(card => card.MetricIds.Count));
        Assert.False(layout.CardSizesLocked);
        Assert.Equal(0, layout.LockedSurfaceWidth);
        Assert.All(layout.Cards, card => Assert.Equal(6, card.Bounds!.Width));
        Assert.All(layout.Cards, card => Assert.Equal(231, card.Bounds!.Height));
        Assert.Equal([0d, 6d], layout.Cards.Select(card => card.Bounds!.X));
        Assert.False(defaults.ShowOverviewModelStatus);
        Assert.False(defaults.ShowOverviewSlots);
    }

    [Fact]
    public void OverviewDashboardReplicatesDefaultGpuCardForEveryDetectedGpu()
    {
        var expanded = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(
            OverviewDashboardLayoutPolicy.Default,
            [2, 0, 1, 1, -1, 16]);

        var gpuCards = expanded.Cards.Where(card => card.Id.StartsWith("default-gpu-", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["default-gpu-0", "default-gpu-1", "default-gpu-2"], gpuCards.Select(card => card.Id));
        Assert.All(gpuCards, card => Assert.Equal(5, card.MetricIds.Count));
        Assert.All(gpuCards, card => Assert.DoesNotContain(card.MetricIds,
            metricId => OverviewDashboardMetricIds.TryParseGpuCoreClock(metricId, out _)));
        Assert.All(gpuCards, card => Assert.Equal([card.MetricIds[0]], card.ChartMetricIds));
        Assert.Equal(0, gpuCards[0].Bounds!.Y);
        Assert.Equal(0, gpuCards[1].Bounds!.Y);
        Assert.Equal(0, gpuCards[2].Bounds!.Y);
        Assert.All(expanded.Cards, card => Assert.Equal(3, card.Bounds!.Width));
        Assert.All(expanded.Cards, card => Assert.Equal(231, card.Bounds!.Height));
        Assert.False(expanded.CardSizesLocked);
        var repeated = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(expanded, [0, 1, 2]);
        Assert.Equal(expanded.Cards.Select(card => card.Id), repeated.Cards.Select(card => card.Id));
        Assert.Equal(expanded.Cards.Select(card => card.Bounds), repeated.Cards.Select(card => card.Bounds));

        var custom = new OverviewDashboardLayout(
            OverviewDashboardLayoutPolicy.CurrentVersion,
            [new("custom", [OverviewDashboardMetricIds.GpuCoreClock(0)])]);
        var preserved = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(custom, [0, 1, 2]);
        Assert.Equal("custom", Assert.Single(preserved.Cards).Id);
        Assert.Equal([OverviewDashboardMetricIds.GpuCoreClock(0)], preserved.Cards[0].MetricIds);
    }

    [Fact]
    public void OverviewDashboardDefaultGpuCardsExcludeIntegratedGraphics()
    {
        var snapshot = HostHardwareSnapshotParser.Parse("""
            GPU 0: AMD Radeon(TM) Graphics
            GPU 1: NVIDIA GeForce RTX 4090
            GPU 2: Intel(R) UHD Graphics 770
            GPU 3: AMD Radeon RX 7900 XTX
            GPU 4: Intel(R) Arc(TM) A770 Graphics
            GPU 5: Intel(R) Arc(TM) Graphics
            GPU 6: Matrox D-Series
            """);

        var defaults = OverviewDashboardLayoutPolicy.DefaultGpuCardIndices(snapshot.Gpus);

        Assert.Equal([1, 3, 4, 6], defaults);
    }

    [Fact]
    public void OverviewDashboardMigratesLegacyDefaultCardsToUnlockedEqualWidthsWithoutGpuCoreClock()
    {
        var current = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(
            OverviewDashboardLayoutPolicy.Default,
            [0]);
        var legacyCards = current.Cards.Select(card => card.Id == "default-gpu-0"
            ? card with
            {
                MetricIds =
                [
                    OverviewDashboardMetricIds.Gpu(0),
                    OverviewDashboardMetricIds.GpuCoreClock(0),
                    OverviewDashboardMetricIds.GpuTemperature(0),
                    OverviewDashboardMetricIds.GpuVram(0),
                    OverviewDashboardMetricIds.GpuPower(0),
                    OverviewDashboardMetricIds.ObservedGpuEnergy(0)
                ]
            }
            : card).ToArray();
        var legacy = new OverviewDashboardLayout(10, legacyCards, CardSizesLocked: true, LockedSurfaceWidth: 1048);

        var migrated = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(legacy, [0]);

        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, migrated.Version);
        Assert.False(migrated.CardSizesLocked);
        Assert.Equal(0, migrated.LockedSurfaceWidth);
        Assert.All(migrated.Cards, card => Assert.Equal(4, card.Bounds!.Width));
        Assert.All(migrated.Cards, card => Assert.Equal(231, card.Bounds!.Height));
        Assert.DoesNotContain(OverviewDashboardMetricIds.GpuCoreClock(0),
            migrated.Cards.Single(card => card.Id == "default-gpu-0").MetricIds);
        Assert.Equal([OverviewDashboardMetricIds.Gpu(0)],
            migrated.Cards.Single(card => card.Id == "default-gpu-0").ChartMetricIds);
    }

    [Fact]
    public void OverviewDashboardMigratesVersionElevenGpuPowerChartAndTallCards()
    {
        var versionEleven = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(
            OverviewDashboardLayoutPolicy.Default,
            [0]) with
        {
            Version = 11,
            Cards = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(
                    OverviewDashboardLayoutPolicy.Default,
                    [0])
                .Cards
                .Select(card => card.Id == "default-gpu-0"
                    ? card with
                    {
                        ChartMetricIds =
                        [
                            OverviewDashboardMetricIds.Gpu(0),
                            OverviewDashboardMetricIds.GpuPower(0)
                        ],
                        Bounds = card.Bounds! with { Height = 329 }
                    }
                    : card)
                .ToArray()
        };

        var migrated = OverviewDashboardLayoutPolicy.Normalize(versionEleven);
        var gpuCard = migrated.Cards.Single(card => card.Id == "default-gpu-0");

        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, migrated.Version);
        Assert.All(migrated.Cards, card => Assert.Equal(231, card.Bounds!.Height));
        Assert.Contains(OverviewDashboardMetricIds.GpuPower(0), gpuCard.MetricIds);
        Assert.Equal([OverviewDashboardMetricIds.Gpu(0)], gpuCard.ChartMetricIds);
    }

    [Fact]
    public void OverviewDashboardUsesProductionDefaultWhenLegacyLiveOnlyCardsAreRemoved()
    {
        var legacy = new OverviewDashboardLayout(4,
        [
            new("live-rates",
            [
                OverviewDashboardMetricIds.GenerationRate,
                OverviewDashboardMetricIds.PromptRate,
                OverviewDashboardMetricIds.MtpGeneratedRate,
                OverviewDashboardMetricIds.MtpAcceptedRate
            ])
        ]);

        var migrated = OverviewDashboardLayoutPolicy.Normalize(legacy);

        Assert.Equal(["default-runtime", "default-host"], migrated.Cards.Select(card => card.Id));
        Assert.Equal(
        [
            OverviewDashboardMetricIds.AveragePromptRate,
            OverviewDashboardMetricIds.AverageGenerationRate,
            OverviewDashboardMetricIds.MtpAcceptedTokens,
            OverviewDashboardMetricIds.MtpGeneratedTokens,
            OverviewDashboardMetricIds.KvCacheUsage,
            OverviewDashboardMetricIds.KvCacheUsed,
            OverviewDashboardMetricIds.GeneratedTokens,
            OverviewDashboardMetricIds.PromptTokens
        ], migrated.Cards[0].MetricIds);
        Assert.False(migrated.CardSizesLocked);
        Assert.Equal(0, migrated.LockedSurfaceWidth);
    }

    [Fact]
    public void OverviewDashboardBuiltInMetricLabelsRemainCompactAndDescriptive()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var definitions = OverviewDashboardMetricRegistry.BuiltInDefinitions();

        Assert.Equal("Observed energy · Total", definitions.Single(item =>
            item.Id == OverviewDashboardMetricIds.ObservedGpuEnergyTotal).DisplayName);
        Assert.Equal("Energy cost · Total", definitions.Single(item =>
            item.Id == OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal).DisplayName);
        Assert.Equal("Draft generation average", definitions.Single(item =>
            item.Id == OverviewDashboardMetricIds.AverageMtpGeneratedRate).DisplayName);
        Assert.Equal("Draft tokens accepted", definitions.Single(item =>
            item.Id == OverviewDashboardMetricIds.MtpAcceptedTokens).DisplayName);
        Assert.All(definitions, definition => Assert.True(definition.DisplayName.Length <= 24,
            $"Built-in dashboard label is too long: {definition.DisplayName}"));
    }

    [Fact]
    public void OverviewDashboardCardTitlesAreOptionalBoundedAndVersioned()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var cardId = layout.Cards[0].Id;

        var titled = OverviewDashboardLayoutPolicy.SetCardTitle(
            layout,
            cardId,
            "  Runtime\u0001 summary  ");
        Assert.Equal("Runtime summary", titled.Cards[0].Title);

        titled = OverviewDashboardLayoutPolicy.SetCardTitle(
            titled,
            cardId,
            new string('x', OverviewDashboardLayoutPolicy.MaximumCardTitleLength + 20));
        Assert.Equal(OverviewDashboardLayoutPolicy.MaximumCardTitleLength, titled.Cards[0].Title.Length);

        var cleared = OverviewDashboardLayoutPolicy.SetCardTitle(titled, cardId, "   ");
        Assert.Equal("", cleared.Cards[0].Title);

        var versionEight = new OverviewDashboardLayout(
            OverviewDashboardLayoutPolicy.FixedCardSizeLayoutVersion,
            [layout.Cards[0] with { Title = "Not valid before v9" }]);
        Assert.Equal("", OverviewDashboardLayoutPolicy.Normalize(versionEight).Cards[0].Title);
    }

    [Fact]
    public async Task StateStorePersistsOptionalDashboardCardTitle()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var layout = OverviewDashboardLayoutPolicy.SetCardTitle(
            OverviewDashboardLayoutPolicy.Default,
            OverviewDashboardLayoutPolicy.Default.Cards[0].Id,
            "Primary telemetry");
        var settings = AppSettings.CreateDefault(root) with { OverviewDashboardLayout = layout };

        await store.SaveAppSettingsAsync(settings);
        var reloaded = await store.GetAppSettingsAsync(root);

        Assert.Equal("Primary telemetry", reloaded.OverviewDashboardLayout!.Cards[0].Title);
        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, reloaded.OverviewDashboardLayout.Version);
    }
}
