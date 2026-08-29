using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class OverviewDashboardTests : ManagerRegressionTestBase
{
    [Fact]
    public void OverviewDashboardPolicyNormalizesUntrustedLayouts()
    {
        var rawId = OverviewDashboardMetricIds.Prometheus("llama_slots", "state=busy");
        var layout = new OverviewDashboardLayout(99,
        [
            new("duplicate", [OverviewDashboardMetricIds.AverageGenerationRate, OverviewDashboardMetricIds.AverageGenerationRate, "invalid"], 99,
                (OverviewDashboardCardHeight)99, "invalid"),
            new("duplicate", [rawId], 0, OverviewDashboardCardHeight.Tall, rawId),
            new("empty", ["invalid"])
        ]);

        var normalized = OverviewDashboardLayoutPolicy.Normalize(layout);

        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, normalized.Version);
        Assert.Equal(2, normalized.Cards.Count);
        Assert.Equal(["duplicate", "duplicate-2"], normalized.Cards.Select(card => card.Id));
        Assert.Equal(3, normalized.Cards[0].ColumnSpan);
        Assert.Equal(OverviewDashboardCardHeight.Standard, normalized.Cards[0].Height);
        Assert.Equal(new OverviewDashboardCardBounds(0, 0, 12, 112), normalized.Cards[0].Bounds);
        Assert.Equal("", normalized.Cards[0].ChartMetricId);
        Assert.Equal(rawId, normalized.Cards[1].ChartMetricId);
    }

    [Fact]
    public void OverviewDashboardOperationsRemainComposable()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var firstId = layout.Cards[0].Id;
        var originalMetrics = layout.Cards[0].MetricIds.ToArray();
        var originalMetricCardId = layout.Cards.Single(card =>
            card.MetricIds.Contains(OverviewDashboardMetricIds.Cpu)).Id;
        layout = OverviewDashboardLayoutPolicy.AddMetrics(layout, firstId, [OverviewDashboardMetricIds.QueuedRequests]);
        layout = OverviewDashboardLayoutPolicy.RemoveCard(layout, layout.Cards.Single(card =>
            card.Id == originalMetricCardId).Id);
        layout = OverviewDashboardLayoutPolicy.ResizeCard(layout, firstId, 2, OverviewDashboardCardHeight.Tall);
        layout = OverviewDashboardLayoutPolicy.SetChart(layout, firstId, OverviewDashboardMetricIds.QueuedRequests);
        layout = OverviewDashboardLayoutPolicy.MoveCardToIndex(layout, firstId, layout.Cards.Count - 1);

        var card = layout.Cards.Single(item => item.Id == firstId);
        Assert.Equal([.. originalMetrics, OverviewDashboardMetricIds.QueuedRequests], card.MetricIds);
        Assert.Equal(2, card.ColumnSpan);
        Assert.Equal(OverviewDashboardCardHeight.Tall, card.Height);
        Assert.Equal(8, card.Bounds!.Width);
        Assert.Equal(176, card.Bounds.Height);
        Assert.Equal("", card.ChartMetricId);
        Assert.Empty(card.ChartMetricIds!);
        Assert.Equal(firstId, layout.Cards[^1].Id);

        layout = OverviewDashboardLayoutPolicy.RemoveMetric(layout, firstId, originalMetrics[0]);
        Assert.Equal([.. originalMetrics[1..], OverviewDashboardMetricIds.QueuedRequests], layout.Cards[^1].MetricIds);
    }

    [Fact]
    public void OverviewDashboardAddsEverySelectedMetricBeyondTheFormerSixRowLimit()
    {
        var metrics = OverviewDashboardLayoutPolicy.Default.Cards
            .SelectMany(card => card.MetricIds)
            .Distinct(StringComparer.Ordinal)
            .Take(9)
            .ToArray();
        var layout = new OverviewDashboardLayout(
            OverviewDashboardLayoutPolicy.CurrentVersion,
            [new OverviewDashboardCardLayout("many-rows", metrics[..6])]);

        layout = OverviewDashboardLayoutPolicy.AddMetrics(layout, "many-rows", metrics[6..]);

        Assert.Equal(metrics, Assert.Single(layout.Cards).MetricIds);
    }

    [Fact]
    public void OverviewDashboardMigratesCompositeVersionOneMetricsToCurrentBounds()
    {
        var legacy = new OverviewDashboardLayout(1,
        [
            new("hardware", [OverviewDashboardMetricIds.LegacyHardware]),
            new("runtime", [OverviewDashboardMetricIds.LegacyTokens, OverviewDashboardMetricIds.LegacyKvCache],
                ChartMetricId: OverviewDashboardMetricIds.LegacyTokens)
        ]);

        var migrated = OverviewDashboardLayoutPolicy.Normalize(legacy);

        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, migrated.Version);
        Assert.Equal(
            [OverviewDashboardMetricIds.Cpu, OverviewDashboardMetricIds.Ram, OverviewDashboardMetricIds.Gpu(0)],
            migrated.Cards[0].MetricIds);
        Assert.Equal(OverviewDashboardMetricIds.AverageGenerationRate, migrated.Cards[1].ChartMetricId);
        Assert.Equal([OverviewDashboardMetricIds.AverageGenerationRate], migrated.Cards[1].ChartMetricIds);
        Assert.Contains(OverviewDashboardMetricIds.KvCacheUsage, migrated.Cards.SelectMany(card => card.MetricIds));
        Assert.DoesNotContain(migrated.Cards.SelectMany(card => card.MetricIds),
            metricId => metricId is OverviewDashboardMetricIds.LegacyHardware
                or OverviewDashboardMetricIds.LegacyTokens
                or OverviewDashboardMetricIds.LegacyKvCache);
        Assert.All(migrated.Cards, card => Assert.NotNull(card.Bounds));
    }

    [Fact]
    public void OverviewDashboardMigratesVersionTwoPackedCardsWithoutReinterpretingAtomicMetrics()
    {
        var versionTwo = new OverviewDashboardLayout(2,
        [
            new("hardware", [OverviewDashboardMetricIds.Cpu, OverviewDashboardMetricIds.Ram], 2,
                OverviewDashboardCardHeight.Tall),
            new("runtime", [OverviewDashboardMetricIds.GenerationRate, OverviewDashboardMetricIds.AverageGenerationRate], 1,
                OverviewDashboardCardHeight.Compact, OverviewDashboardMetricIds.GenerationRate)
        ]);

        var migrated = OverviewDashboardLayoutPolicy.Normalize(versionTwo);

        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, migrated.Version);
        Assert.Equal([OverviewDashboardMetricIds.Cpu, OverviewDashboardMetricIds.Ram], migrated.Cards[0].MetricIds);
        Assert.Equal(new OverviewDashboardCardBounds(0, 0, 8, 176), migrated.Cards[0].Bounds);
        Assert.Equal(new OverviewDashboardCardBounds(8, 0, 4, 88), migrated.Cards[1].Bounds);
        Assert.Equal([OverviewDashboardMetricIds.AverageGenerationRate], migrated.Cards[1].MetricIds);
        Assert.Equal(OverviewDashboardMetricIds.AverageGenerationRate, migrated.Cards[1].ChartMetricId);
        Assert.Equal([OverviewDashboardMetricIds.AverageGenerationRate], migrated.Cards[1].ChartMetricIds);
    }

    [Fact]
    public void OverviewDashboardCurrentVersionSupportsIndependentAverageMetricCharts()
    {
        var cardId = OverviewDashboardLayoutPolicy.Default.Cards.Single(card =>
            card.MetricIds.Contains(OverviewDashboardMetricIds.AverageGenerationRate)).Id;
        var layout = OverviewDashboardLayoutPolicy.SetChartVisibility(
            OverviewDashboardLayoutPolicy.Default,
            cardId,
            OverviewDashboardMetricIds.AverageGenerationRate,
            true);
        layout = OverviewDashboardLayoutPolicy.SetChartVisibility(
            layout,
            cardId,
            OverviewDashboardMetricIds.AveragePromptRate,
            true);

        var card = layout.Cards.Single(item => item.Id == cardId);
        Assert.Equal(
            [OverviewDashboardMetricIds.AverageGenerationRate, OverviewDashboardMetricIds.AveragePromptRate],
            card.ChartMetricIds);

        layout = OverviewDashboardLayoutPolicy.SetChartVisibility(
            layout,
            cardId,
            OverviewDashboardMetricIds.AverageGenerationRate,
            false);
        card = layout.Cards.Single(item => item.Id == cardId);
        Assert.Equal([OverviewDashboardMetricIds.AveragePromptRate], card.ChartMetricIds);
        Assert.Equal(OverviewDashboardMetricIds.AveragePromptRate, card.ChartMetricId);

        layout = OverviewDashboardLayoutPolicy.RemoveMetric(layout, cardId, OverviewDashboardMetricIds.AveragePromptRate);
        card = layout.Cards.Single(item => item.Id == cardId);
        Assert.Empty(card.ChartMetricIds!);
        Assert.Equal("", card.ChartMetricId);
    }

    [Fact]
    public void OverviewDashboardMigratesVersionThreeLiveChartToCurrentAverageChartList()
    {
        var versionThree = new OverviewDashboardLayout(3,
        [
            new("runtime", [OverviewDashboardMetricIds.GenerationRate, OverviewDashboardMetricIds.AveragePromptRate],
                ChartMetricId: OverviewDashboardMetricIds.PromptRate,
                Bounds: new OverviewDashboardCardBounds(0, 0, 4, 112))
        ]);

        var migrated = OverviewDashboardLayoutPolicy.Normalize(versionThree);

        Assert.Equal(OverviewDashboardLayoutPolicy.CurrentVersion, migrated.Version);
        Assert.Equal([OverviewDashboardMetricIds.AveragePromptRate], migrated.Cards[0].ChartMetricIds);
    }

    [Fact]
    public void OverviewDashboardMigratesVersionSixSessionNamedEnergyMetricsToObservedLiveMetrics()
    {
        var versionSix = new OverviewDashboardLayout(6,
        [
            new("energy",
            [
                OverviewDashboardMetricIds.LegacySessionGpuEnergyTotal,
                $"{OverviewDashboardMetricIds.LegacySessionGpuEnergyPrefix}2",
                OverviewDashboardMetricIds.LegacySessionGpuElectricityCostTotal,
                $"{OverviewDashboardMetricIds.LegacySessionGpuElectricityCostPrefix}2"
            ], Bounds: new OverviewDashboardCardBounds(0, 0, 4, 112))
        ]);

        var migrated = OverviewDashboardLayoutPolicy.Normalize(versionSix);

        Assert.Equal(
        [
            OverviewDashboardMetricIds.ObservedGpuEnergyTotal,
            OverviewDashboardMetricIds.ObservedGpuEnergy(2),
            OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal,
            OverviewDashboardMetricIds.ObservedGpuElectricityCost(2)
        ], Assert.Single(migrated.Cards).MetricIds);
    }

    [Fact]
    public void OverviewDashboardLegacyVisibilityChangesProjectIntoLayout()
    {
        var previous = new OverviewDashboardLegacyVisibility(true, true, true, true, true, true);
        var updated = previous with { Hardware = false, MtpTokens = false };

        var layout = OverviewDashboardLayoutPolicy.ApplyLegacyVisibilityChanges(null, previous, updated);
        var effective = OverviewDashboardLayoutPolicy.LegacyVisibility(layout);

        Assert.Equal(updated, effective);
        Assert.DoesNotContain(layout.Cards.SelectMany(card => card.MetricIds), id =>
            id == OverviewDashboardMetricIds.Cpu || id == OverviewDashboardMetricIds.Ram
            || OverviewDashboardMetricIds.IsGpuMetric(id));
        Assert.DoesNotContain(layout.Cards.SelectMany(card => card.MetricIds),
            id => id.StartsWith("overview.runtime.mtp.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(600, 200, 400)]
    [InlineData(900, 300, 600)]
    [InlineData(1200, 400, 800)]
    public void OverviewDashboardLayoutEngineScalesHorizontalBoundsWithoutRepacking(
        double surfaceWidth,
        double expectedLeft,
        double expectedWidth)
    {
        var cards = new[]
        {
            new OverviewDashboardCardLayout(
                "freeform",
                [OverviewDashboardMetricIds.AverageGenerationRate],
                Bounds: new OverviewDashboardCardBounds(4, 37, 8, 143))
        };

        var placement = Assert.Single(OverviewDashboardLayoutEngine.Place(cards, surfaceWidth));

        Assert.Equal(expectedLeft, placement.Left);
        Assert.Equal(expectedWidth, placement.Width);
        Assert.Equal(37, placement.Top);
        Assert.Equal(143, placement.Height);
    }

    [Fact]
    public void OverviewDashboardMetricOrderPersistsOnlyACompleteCardPermutation()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var card = layout.Cards.First(item => item.MetricIds.Count > 2);
        var reversed = card.MetricIds.Reverse().ToArray();

        layout = OverviewDashboardLayoutPolicy.ReorderMetrics(layout, card.Id, reversed);
        Assert.Equal(reversed, layout.Cards.Single(item => item.Id == card.Id).MetricIds);

        layout = OverviewDashboardLayoutPolicy.ReorderMetrics(layout, card.Id, [reversed[0]]);
        Assert.Equal(reversed, layout.Cards.Single(item => item.Id == card.Id).MetricIds);
    }

    [Fact]
    public void OverviewDashboardBoundsCanMoveAndResizeIndependentlyOfLegacyPresets()
    {
        var layout = OverviewDashboardLayoutPolicy.Default;
        var id = layout.Cards[0].Id;

        layout = OverviewDashboardLayoutPolicy.SetCardBounds(
            layout,
            id,
            new OverviewDashboardCardBounds(2.125, 48, 5.375, 219));

        var card = layout.Cards.Single(item => item.Id == id);
        Assert.Equal(new OverviewDashboardCardBounds(2.125, 48, 5.375, 219), card.Bounds);
        Assert.Equal(1, card.ColumnSpan);
        Assert.Equal(OverviewDashboardCardHeight.Tall, card.Height);
    }

    [Fact]
    public void OverviewDashboardGeometrySnapsAndMaintainsCardSpacing()
    {
        var obstacle = new OverviewDashboardCardBounds(0, 0, 4, 100);
        var horizontallySnapped = OverviewDashboardLayoutEngine.SnapMove(
            new OverviewDashboardCardBounds(4.1, 0, 4, 100),
            [obstacle],
            1200);
        var verticallySnapped = OverviewDashboardLayoutEngine.SnapMove(
            new OverviewDashboardCardBounds(0, 105, 4, 100),
            [obstacle],
            1200);
        var separated = OverviewDashboardLayoutEngine.SnapMove(
            new OverviewDashboardCardBounds(2, 0, 4, 100),
            [obstacle],
            1200);

        Assert.Equal(4, horizontallySnapped.X);
        Assert.Equal(110, verticallySnapped.Y);
        Assert.Equal(4, separated.X);
    }

    [Fact]
    public void OverviewDashboardResizeSnapsTheVisibleEdgeBeforeAnotherCard()
    {
        var start = new OverviewDashboardCardBounds(0, 0, 4, 100);
        var resized = OverviewDashboardLayoutEngine.SnapResize(
            start,
            new OverviewDashboardCardBounds(0, 0, 6.1, 100),
            OverviewDashboardResizeEdge.Right,
            [new OverviewDashboardCardBounds(6, 0, 4, 100)],
            1200,
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutPolicy.MinimumCardHeight);

        Assert.Equal(6, resized.Width);
    }

    [Fact]
    public void OverviewDashboardResizeAlignsVerticalEdgesOfAdjacentCards()
    {
        var neighbor = new OverviewDashboardCardBounds(4, 0, 4, 140);
        var bottom = OverviewDashboardLayoutEngine.SnapResize(
            new OverviewDashboardCardBounds(0, 0, 4, 100),
            new OverviewDashboardCardBounds(0, 0, 4, 132),
            OverviewDashboardResizeEdge.Bottom,
            [neighbor],
            1200,
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutPolicy.MinimumCardHeight);
        var top = OverviewDashboardLayoutEngine.SnapResize(
            new OverviewDashboardCardBounds(0, 40, 4, 100),
            new OverviewDashboardCardBounds(0, 12, 4, 128),
            OverviewDashboardResizeEdge.Top,
            [neighbor],
            1200,
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutPolicy.MinimumCardHeight);
        var separated = OverviewDashboardLayoutEngine.SnapResize(
            new OverviewDashboardCardBounds(0, 0, 3.5, 100),
            new OverviewDashboardCardBounds(0, 0, 3.5, 132),
            OverviewDashboardResizeEdge.Bottom,
            [neighbor],
            1200,
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutPolicy.MinimumCardHeight);

        Assert.Equal(140, bottom.Height);
        Assert.Equal(0, top.Y);
        Assert.Equal(140, top.Height);
        Assert.Equal(132, separated.Height);
    }

    [Fact]
    public void OverviewDashboardPersistsOnlyTheCardEditedAtResponsiveWidth()
    {
        var layout = OverviewDashboardLayoutPolicy.Normalize(new OverviewDashboardLayout(7,
        [
            new("cpu", [OverviewDashboardMetricIds.Cpu], Bounds: new(0, 0, 4, 390)),
            new("gpu-0", [OverviewDashboardMetricIds.Gpu(0)], Bounds: new(4, 0, 4, 390)),
            new("gpu-2", [OverviewDashboardMetricIds.Gpu(2)], Bounds: new(8, 0, 4, 390))
        ]));
        var untouched = layout.Cards.Skip(1).Select(card => card.Bounds).ToArray();

        var persisted = OverviewDashboardLayoutPolicy.SetCardBounds(
            layout, "cpu", new OverviewDashboardCardBounds(0, 0, 6, 420));

        Assert.Equal(6, persisted.Cards[0].Bounds!.Width);
        Assert.Equal(420, persisted.Cards[0].Bounds!.Height);
        Assert.Equal(untouched, persisted.Cards.Skip(1).Select(card => card.Bounds));
    }

    [Fact]
    public void PrometheusDashboardMetricIdsRoundTripNamesAndLabels()
    {
        var id = OverviewDashboardMetricIds.Prometheus("llama.metric/temperature", "gpu=0, name=A|B");

        Assert.True(OverviewDashboardMetricIds.TryParsePrometheus(id, out var name, out var labels));
        Assert.Equal("llama.metric/temperature", name);
        Assert.Equal("gpu=0, name=A|B", labels);
    }

    [Fact]
    public void IndexedGpuSensorMetricIdsRoundTrip()
    {
        Assert.True(OverviewDashboardMetricIds.TryParseGpuVram(OverviewDashboardMetricIds.GpuVram(3), out var vram));
        Assert.True(OverviewDashboardMetricIds.TryParseGpuPower(OverviewDashboardMetricIds.GpuPower(3), out var power));
        Assert.True(OverviewDashboardMetricIds.TryParseGpuCoreClock(OverviewDashboardMetricIds.GpuCoreClock(3), out var clock));
        Assert.True(OverviewDashboardMetricIds.TryParseGpuTemperature(OverviewDashboardMetricIds.GpuTemperature(3), out var temperature));
        Assert.True(OverviewDashboardMetricIds.TryParseGpuVramTemperature(OverviewDashboardMetricIds.GpuVramTemperature(3), out var vramTemperature));
        Assert.Equal([3, 3, 3, 3, 3], [vram, power, clock, temperature, vramTemperature]);
        Assert.True(OverviewDashboardMetricIds.TryParseObservedGpuEnergy(
            OverviewDashboardMetricIds.ObservedGpuEnergy(3), out var observedEnergy));
        Assert.Equal(3, observedEnergy);
        Assert.True(OverviewDashboardMetricIds.TryParseObservedGpuElectricityCost(
            OverviewDashboardMetricIds.ObservedGpuElectricityCost(3), out var observedCost));
        Assert.Equal(3, observedCost);
    }

    [Fact]
    public void DashboardRegistryExposesAppLiveObservedEnergyAsOptionalNonChartableMetrics()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var registry = new OverviewDashboardMetricRegistry();
        var snapshot = new ObservedGpuEnergySnapshot(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow,
            [
                new ObservedGpuEnergyDevice("gpu-0", 0, "NVIDIA RTX", 125, .04),
                new ObservedGpuEnergyDevice("gpu-1", 1, "AMD Radeon", 75, .02)
            ],
            "GBP");

        var readings = registry.ObserveGpuEnergy(snapshot)
            .ToDictionary(reading => reading.MetricId, StringComparer.Ordinal);
        Assert.Equal(.2, readings[OverviewDashboardMetricIds.ObservedGpuEnergyTotal].Primary!.Value, 6);
        Assert.Equal(.125, readings[OverviewDashboardMetricIds.ObservedGpuEnergy(0)].Primary!.Value, 6);
        Assert.Equal("kWh", readings[OverviewDashboardMetricIds.ObservedGpuEnergy(1)].Unit);
        Assert.Equal(.06, readings[OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal].Primary!.Value, 6);
        Assert.Equal(.04, readings[OverviewDashboardMetricIds.ObservedGpuElectricityCost(0)].Primary!.Value, 6);
        Assert.Equal("GBP", readings[OverviewDashboardMetricIds.ObservedGpuElectricityCost(1)].Unit);

        var definitions = registry.Definitions();
        var total = definitions.Single(definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuEnergyTotal);
        var device = definitions.Single(definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuEnergy(0));
        Assert.False(total.Chartable);
        Assert.False(total.RequiresObservedValue);
        Assert.False(device.Chartable);
        Assert.False(device.RequiresObservedValue);
        Assert.Contains("resets when the Manager restarts", device.Tooltip, StringComparison.Ordinal);
        var cost = definitions.Single(definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuElectricityCost(0));
        Assert.False(cost.Chartable);
        Assert.False(cost.RequiresObservedValue);
        Assert.Contains("configured day and night", cost.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void DashboardRegistryObservesHardwareSensorsAsIndependentChartableMetrics()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var registry = new OverviewDashboardMetricRegistry();

        var readings = registry.ObserveHardware(
            "CPU: AMD Ryzen\nTelemetry: 18.5% load | 16C/32T | 57.2 °C thermal | 5200 MHz core\n" +
            "RAM: 12.0/32.0 GiB | 37.5% | 6000 MHz\n" +
            "GPU 0: NVIDIA RTX 4090 | 53.4% load | 62 °C | 8.0/24.0 GiB VRAM | 205.4 W | 1695 MHz core | 76 °C memory\n" +
            "GPU 1: Intel Arc A770 | 8% load");

        var byId = readings.ToDictionary(reading => reading.MetricId, StringComparer.Ordinal);
        Assert.Equal(18.5, byId[OverviewDashboardMetricIds.Cpu].Primary);
        Assert.Equal(57.2, byId[OverviewDashboardMetricIds.CpuTemperature].Primary);
        Assert.Equal(5200, byId[OverviewDashboardMetricIds.CpuCoreClock].Primary);
        Assert.Equal(37.5, byId[OverviewDashboardMetricIds.Ram].Primary);
        Assert.Equal(12, byId[OverviewDashboardMetricIds.RamUsed].Primary);
        Assert.Equal(32, byId[OverviewDashboardMetricIds.RamUsed].Secondary);
        Assert.Equal(6000, byId[OverviewDashboardMetricIds.RamClock].Primary);
        Assert.Equal(8, byId[OverviewDashboardMetricIds.GpuVram(0)].Primary);
        Assert.Equal(24, byId[OverviewDashboardMetricIds.GpuVram(0)].Secondary);
        Assert.Equal(205.4, byId[OverviewDashboardMetricIds.GpuPower(0)].Primary);
        Assert.Equal(1695, byId[OverviewDashboardMetricIds.GpuCoreClock(0)].Primary);
        Assert.Equal(62, byId[OverviewDashboardMetricIds.GpuTemperature(0)].Primary);
        Assert.Equal(76, byId[OverviewDashboardMetricIds.GpuVramTemperature(0)].Primary);
        Assert.Equal(8, byId[OverviewDashboardMetricIds.Gpu(1)].Primary);
        var definitions = registry.Definitions();
        Assert.Contains(definitions, definition =>
            definition.Id == OverviewDashboardMetricIds.GpuPower(0)
            && definition.DisplayName == "GPU 0 · Power draw" && definition.Chartable
            && definition.Tooltip.Contains(OverviewDashboardMetricIds.GpuPower(0), StringComparison.Ordinal));
        Assert.Contains(definitions, definition => definition.Id == OverviewDashboardMetricIds.Gpu(1));
        Assert.Contains(definitions, definition =>
            definition.Id == OverviewDashboardMetricIds.GpuVramTemperature(0)
            && definition.DisplayName == "GPU 0 · VRAM temperature" && definition.Chartable);
        Assert.DoesNotContain(definitions, definition => definition.Id == OverviewDashboardMetricIds.GpuPower(1));
        Assert.Contains(definitions, definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuEnergy(0)
            && !definition.RequiresObservedValue);
        Assert.Contains(definitions, definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuElectricityCost(0)
            && !definition.RequiresObservedValue);
        Assert.DoesNotContain(definitions, definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuEnergy(1));
        Assert.DoesNotContain(definitions, definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuElectricityCost(1));
    }

    [Fact]
    public void DashboardRegistryUsesFriendlyNamesAndPreservesRawMetricDetailsInTooltips()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var registry = new OverviewDashboardMetricRegistry();
        registry.Observe([
            new PrometheusSample(
                "llamacpp:tokens_predicted_total",
                "slot=\"0\"",
                42,
                "42",
                "counter",
                "Number of generated tokens.")
        ], "runtime");

        var definitions = registry.Definitions();
        var raw = definitions.Single(item => item.Id == OverviewDashboardMetricIds.Prometheus(
            "llamacpp:tokens_predicted_total", "slot=\"0\""));
        Assert.Equal("Generated tokens (total) · Slot: 0", raw.DisplayName);
        Assert.Contains("Number of generated tokens.", raw.Tooltip, StringComparison.Ordinal);
        Assert.Contains("llamacpp:tokens_predicted_total{slot=\"0\"}", raw.Tooltip, StringComparison.Ordinal);
        Assert.Contains("Prometheus type: counter", raw.Tooltip, StringComparison.Ordinal);

        Assert.Equal("CPU usage", definitions.Single(item => item.Id == OverviewDashboardMetricIds.Cpu).DisplayName);
        Assert.Equal("Memory usage", definitions.Single(item => item.Id == OverviewDashboardMetricIds.Ram).DisplayName);
        Assert.All(definitions, definition => Assert.False(string.IsNullOrWhiteSpace(definition.Tooltip)));
    }

    [Fact]
    public void DashboardRegistryReplacesDynamicCatalogsWhenTheirSourceChanges()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var registry = new OverviewDashboardMetricRegistry();
        var firstRawId = OverviewDashboardMetricIds.Prometheus("runtime_one", "");
        var secondRawId = OverviewDashboardMetricIds.Prometheus("runtime_two", "");
        registry.Observe([new PrometheusSample("runtime_one", "", 1, "1", "gauge", "")], "first");
        registry.Observe([new PrometheusSample("runtime_two", "", 2, "2", "gauge", "")], "second");

        var runtimeDefinitions = registry.Definitions();
        Assert.DoesNotContain(runtimeDefinitions, definition => definition.Id == firstRawId);
        Assert.Contains(runtimeDefinitions, definition => definition.Id == secondRawId);

        registry.ObserveHardware("GPU 0: NVIDIA RTX | 20% load | 100 W");
        Assert.Contains(registry.Definitions(), definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuEnergy(0));
        registry.ObserveHardware("CPU: AMD Ryzen\nTelemetry: 10% load");
        Assert.DoesNotContain(registry.Definitions(), definition =>
            definition.Id == OverviewDashboardMetricIds.ObservedGpuEnergy(0));
    }

    [Fact]
    public void OverviewDashboardChartsOnlyExposeCuratedTimeVaryingMetrics()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var registry = new OverviewDashboardMetricRegistry();
        registry.Observe([
            new PrometheusSample("llama_slots", "state=busy", 1, "1", "gauge", "Slots")
        ], "runtime");
        var definitions = registry.Definitions();

        Assert.False(definitions.Single(item => item.Id == OverviewDashboardMetricIds.ActiveSlots).Chartable);
        Assert.False(definitions.Single(item => item.Id == OverviewDashboardMetricIds.QueuedRequests).Chartable);
        Assert.False(definitions.Single(item => item.Id == OverviewDashboardMetricIds.BusyDecodeSlots).Chartable);
        Assert.False(definitions.Single(item => item.Id == OverviewDashboardMetricIds.KvCacheCapacity).Chartable);
        Assert.False(definitions.Single(item => item.Id == OverviewDashboardMetricIds.RamClock).Chartable);
        Assert.True(definitions.Single(item => item.Id == OverviewDashboardMetricIds.Cpu).Chartable);
        Assert.True(definitions.Single(item => item.Id == OverviewDashboardMetricIds.CpuTemperature).RequiresObservedValue);
        Assert.True(definitions.Single(item => item.Id == OverviewDashboardMetricIds.ContextShifts).RequiresObservedValue);
        Assert.False(definitions.Single(item => item.Id ==
            OverviewDashboardMetricIds.Prometheus("llama_slots", "state=busy")).Chartable);
    }

    [Fact]
    public void OverviewDashboardLayoutDropsChartsForStaticAndSlotMetrics()
    {
        var layout = new OverviewDashboardLayout(5,
        [
            new("static", [
                    OverviewDashboardMetricIds.ActiveSlots,
                    OverviewDashboardMetricIds.RamClock,
                    OverviewDashboardMetricIds.KvCacheCapacity,
                    OverviewDashboardMetricIds.Cpu
                ],
                ChartMetricIds: [
                    OverviewDashboardMetricIds.ActiveSlots,
                    OverviewDashboardMetricIds.RamClock,
                    OverviewDashboardMetricIds.KvCacheCapacity,
                    OverviewDashboardMetricIds.Cpu
                ])
        ]);

        var normalized = OverviewDashboardLayoutPolicy.Normalize(layout);

        Assert.Equal([OverviewDashboardMetricIds.Cpu], normalized.Cards[0].ChartMetricIds);
    }

    [Fact]
    public void LegacyVisibilityRemainsCompatibleWithoutAppearingInSettingsUi()
    {
        LocalLlmConsole.Localization.Loc.LoadLanguage("en");
        var current = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            OverviewDashboardLayout = OverviewDashboardLayoutPolicy.Default
        };
        var visibleSettingKeys = new SettingsPageDefinitionService().BuildRows(current)
            .Select(row => row.Key)
            .ToHashSet(StringComparer.Ordinal);
        Assert.DoesNotContain("showOverviewModelStatus", visibleSettingKeys);
        Assert.DoesNotContain("showOverviewHardware", visibleSettingKeys);
        Assert.DoesNotContain("showOverviewSlots", visibleSettingKeys);
        Assert.DoesNotContain("showOverviewTokens", visibleSettingKeys);
        Assert.DoesNotContain("showOverviewMtpTokens", visibleSettingKeys);
        Assert.DoesNotContain("showOverviewKvCache", visibleSettingKeys);
        Assert.Contains("showOverviewModelSection", visibleSettingKeys);
        Assert.Contains("showOverviewLiveRuntimeLog", visibleSettingKeys);
        Assert.DoesNotContain("showOverviewAllMetrics", visibleSettingKeys);

        var updates = new AppSettingsUpdateService().Build(new AppSettingsUpdateRequest(
            current,
            current.WorkspaceRoot,
            current.ThemeMode,
            new Dictionary<string, string> { ["showOverviewHardware"] = "Hide" },
            new HashSet<int>()));
        Assert.True(updates.Success);
        Assert.False(updates.Settings.ShowOverviewHardware);
        Assert.DoesNotContain(updates.Settings.OverviewDashboardLayout!.Cards.SelectMany(card => card.MetricIds),
            id => id == OverviewDashboardMetricIds.Cpu || id == OverviewDashboardMetricIds.Ram
                || OverviewDashboardMetricIds.IsGpuMetric(id));

        var patched = new ControlAppSettingsMutationService().Patch(
            current,
            new JsonObject { ["showOverviewMtpTokens"] = false },
            []);
        Assert.False(patched.ShowOverviewMtpTokens);
        Assert.DoesNotContain(patched.OverviewDashboardLayout!.Cards.SelectMany(card => card.MetricIds),
            id => id.StartsWith("overview.runtime.mtp.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task StateStorePersistsVersionedDashboardLayout()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var runtimeOnly = OverviewDashboardLayoutPolicy.Normalize(
            OverviewDashboardLayoutPolicy.Default with
            {
                Cards = [OverviewDashboardLayoutPolicy.Default.Cards[0]]
            });
        var layout = OverviewDashboardLayoutPolicy.ResizeCard(
            runtimeOnly,
            OverviewDashboardLayoutPolicy.Default.Cards[0].Id,
            2,
            OverviewDashboardCardHeight.Tall);
        var visibility = OverviewDashboardLayoutPolicy.LegacyVisibility(layout);
        var settings = AppSettings.CreateDefault(root) with
        {
            OverviewDashboardLayout = layout,
            ShowOverviewHardware = visibility.Hardware,
            ShowOverviewModelSection = false
        };

        await store.SaveAppSettingsAsync(settings);
        var reloaded = await store.GetAppSettingsAsync(root);

        Assert.NotNull(reloaded.OverviewDashboardLayout);
        Assert.Single(reloaded.OverviewDashboardLayout.Cards);
        Assert.Equal(2, reloaded.OverviewDashboardLayout.Cards[0].ColumnSpan);
        Assert.Equal(OverviewDashboardCardHeight.Tall, reloaded.OverviewDashboardLayout.Cards[0].Height);
        Assert.Equal(8, reloaded.OverviewDashboardLayout.Cards[0].Bounds!.Width);
        Assert.Equal(176, reloaded.OverviewDashboardLayout.Cards[0].Bounds!.Height);
        Assert.False(reloaded.ShowOverviewHardware);
        Assert.False(reloaded.ShowOverviewModelSection);
    }
}
