using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class OverviewDashboardThresholdsTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(OverviewDashboardMetricIds.KvCacheUsage, OverviewDashboardThresholdKind.Above, 85d, 95d)]
    [InlineData(OverviewDashboardMetricIds.QueuedRequests, OverviewDashboardThresholdKind.Above, 0d, null)]
    [InlineData(OverviewDashboardMetricIds.GatewayFailureRate, OverviewDashboardThresholdKind.Above, 1d, 5d)]
    [InlineData(OverviewDashboardMetricIds.CpuTemperature, OverviewDashboardThresholdKind.Above, 85d, 95d)]
    [InlineData("overview.hardware.gpu-temperature:1", OverviewDashboardThresholdKind.Above, 85d, 95d)]
    [InlineData("overview.hardware.gpu-vram:0", OverviewDashboardThresholdKind.Above, 90d, 97d)]
    [InlineData(OverviewDashboardMetricIds.DraftAcceptance, OverviewDashboardThresholdKind.Below, 40d, null)]
    [InlineData(OverviewDashboardMetricIds.ContextShifts, OverviewDashboardThresholdKind.Increasing, null, null)]
    public void Rule_HasIssueDefaults(string metricId, OverviewDashboardThresholdKind kind, double? warning, double? critical)
    {
        var rule = OverviewDashboardThresholdPolicy.Rule(metricId);

        Assert.NotNull(rule);
        Assert.Equal(kind, rule.Kind);
        Assert.True(rule.Default.Enabled);
        Assert.Equal(warning, rule.Default.Warning);
        Assert.Equal(critical, rule.Default.Critical);
    }

    [Fact]
    public void Rule_UnsupportedMetric_HasNoThreshold()
    {
        Assert.Null(OverviewDashboardThresholdPolicy.Rule(OverviewDashboardMetricIds.AverageGenerationRate));
        Assert.Null(OverviewDashboardThresholdPolicy.Effective(OverviewDashboardMetricIds.AverageGenerationRate, null));
    }

    [Theory]
    [InlineData(50, OverviewDashboardAlertLevel.Normal)]
    [InlineData(85, OverviewDashboardAlertLevel.Normal)]
    [InlineData(85.1, OverviewDashboardAlertLevel.Warning)]
    [InlineData(95.5, OverviewDashboardAlertLevel.Critical)]
    public void Evaluate_AboveRule_UsesStrictLevels(double value, OverviewDashboardAlertLevel expected)
        => Assert.Equal(expected, new OverviewDashboardAlertTracker().Evaluate(
            OverviewDashboardMetricIds.KvCacheUsage, value, null, "runtime", null, Now));

    [Fact]
    public void Evaluate_AboveRule_ClearsOnlyPastHysteresisMargin()
    {
        var tracker = new OverviewDashboardAlertTracker();
        OverviewDashboardAlertLevel Kv(double value)
            => tracker.Evaluate(OverviewDashboardMetricIds.KvCacheUsage, value, null, "runtime", null, Now);

        Assert.Equal(OverviewDashboardAlertLevel.Critical, Kv(96));
        Assert.Equal(OverviewDashboardAlertLevel.Critical, Kv(93));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, Kv(92));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, Kv(83));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, Kv(82));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, Kv(84));
    }

    [Fact]
    public void Evaluate_QueuedRequests_WarnsForAnyQueue()
    {
        var tracker = new OverviewDashboardAlertTracker();

        Assert.Equal(OverviewDashboardAlertLevel.Normal, tracker.Evaluate(OverviewDashboardMetricIds.QueuedRequests, 0, null, "r", null, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, tracker.Evaluate(OverviewDashboardMetricIds.QueuedRequests, 1, null, "r", null, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, tracker.Evaluate(OverviewDashboardMetricIds.QueuedRequests, 0, null, "r", null, Now));
    }

    [Fact]
    public void Evaluate_VramUsesPercentOfTotal()
    {
        var tracker = new OverviewDashboardAlertTracker();
        var vram = OverviewDashboardMetricIds.GpuVram(0);

        Assert.Equal(OverviewDashboardAlertLevel.Normal, tracker.Evaluate(vram, 20, 24, "hw", null, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, tracker.Evaluate(vram, 22, 24, "hw", null, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Critical, tracker.Evaluate(vram, 23.6, 24, "hw", null, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, tracker.Evaluate(vram, 23.6, null, "hw", null, Now));
    }

    [Fact]
    public void Evaluate_DraftAcceptance_WarnsBelowLevelAndIgnoresInactiveDrafting()
    {
        var tracker = new OverviewDashboardAlertTracker();

        Assert.Equal(OverviewDashboardAlertLevel.Normal, tracker.Evaluate(OverviewDashboardMetricIds.DraftAcceptance, 70, null, "r", null, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, tracker.Evaluate(OverviewDashboardMetricIds.DraftAcceptance, 35, null, "r", null, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, tracker.Evaluate(OverviewDashboardMetricIds.DraftAcceptance, null, null, "r", null, Now));
    }

    [Fact]
    public void Evaluate_ContextShifts_WarnsForHoldAfterIncrease()
    {
        var tracker = new OverviewDashboardAlertTracker();
        OverviewDashboardAlertLevel Shifts(double value, DateTimeOffset at, string runtime = "r")
            => tracker.Evaluate(OverviewDashboardMetricIds.ContextShifts, value, null, runtime, null, at);

        Assert.Equal(OverviewDashboardAlertLevel.Normal, Shifts(4, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, Shifts(4, Now.AddSeconds(5)));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, Shifts(5, Now.AddSeconds(10)));
        Assert.Equal(OverviewDashboardAlertLevel.Warning, Shifts(5, Now.AddMinutes(4)));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, Shifts(5, Now.AddMinutes(6)));
        // A different session starts a new baseline instead of comparing counters.
        Assert.Equal(OverviewDashboardAlertLevel.Normal, Shifts(9, Now.AddMinutes(7), "other"));
    }

    [Fact]
    public void Evaluate_UserOverrideAndDisable_ReplaceDefaults()
    {
        var tracker = new OverviewDashboardAlertTracker();
        var raised = new Dictionary<string, OverviewDashboardMetricThreshold>
        {
            [OverviewDashboardMetricIds.KvCacheUsage] = new(true, 70, null)
        };
        var disabled = new Dictionary<string, OverviewDashboardMetricThreshold>
        {
            [OverviewDashboardMetricIds.KvCacheUsage] = new(false, 85, 95)
        };

        Assert.Equal(OverviewDashboardAlertLevel.Warning, tracker.Evaluate(OverviewDashboardMetricIds.KvCacheUsage, 99, null, "r", raised, Now));
        Assert.Equal(OverviewDashboardAlertLevel.Normal, tracker.Evaluate(OverviewDashboardMetricIds.KvCacheUsage, 99, null, "r", disabled, Now));
    }

    [Theory]
    [InlineData(OverviewDashboardMetricIds.KvCacheUsage, 90d, 80d, false)]
    [InlineData(OverviewDashboardMetricIds.KvCacheUsage, 80d, 90d, true)]
    [InlineData(OverviewDashboardMetricIds.DraftAcceptance, 40d, 20d, true)]
    [InlineData(OverviewDashboardMetricIds.DraftAcceptance, 20d, 40d, false)]
    [InlineData(OverviewDashboardMetricIds.KvCacheUsage, double.NaN, null, false)]
    [InlineData(OverviewDashboardMetricIds.ContextShifts, 1d, null, false)]
    public void IsValid_RequiresFiniteOrderedLevels(string metricId, double? warning, double? critical, bool expected)
        => Assert.Equal(expected, OverviewDashboardThresholdPolicy.IsValid(
            OverviewDashboardThresholdPolicy.Rule(metricId)!,
            new OverviewDashboardMetricThreshold(true, warning, critical)));

    [Fact]
    public void SetMetricThreshold_PersistsOverrideAndDefaultRemovesIt()
    {
        var layout = OverviewDashboardLayoutPolicy.SetMetricThreshold(
            OverviewDashboardLayoutPolicy.Default,
            OverviewDashboardMetricIds.KvCacheUsage,
            new OverviewDashboardMetricThreshold(true, 70, 90));

        var restored = JsonSerializer.Deserialize<OverviewDashboardLayout>(JsonSerializer.Serialize(layout));
        var normalized = OverviewDashboardLayoutPolicy.Normalize(restored);
        Assert.Equal(new OverviewDashboardMetricThreshold(true, 70, 90),
            normalized.MetricThresholds![OverviewDashboardMetricIds.KvCacheUsage]);

        var reset = OverviewDashboardLayoutPolicy.SetMetricThreshold(
            normalized, OverviewDashboardMetricIds.KvCacheUsage, null);
        Assert.Null(reset.MetricThresholds);
        var sameAsDefault = OverviewDashboardLayoutPolicy.SetMetricThreshold(
            normalized, OverviewDashboardMetricIds.KvCacheUsage, new OverviewDashboardMetricThreshold(true, 85, 95));
        Assert.Null(sameAsDefault.MetricThresholds);
    }

    [Fact]
    public void Normalize_SavedLayoutWithoutThresholds_StaysCompatible()
    {
        var json = JsonSerializer.Serialize(OverviewDashboardLayoutPolicy.Default with { MetricThresholds = null });
        Assert.DoesNotContain("\"MetricThresholds\":{", json, StringComparison.Ordinal);

        var legacy = JsonSerializer.Deserialize<OverviewDashboardLayout>(
            "{\"Version\":12,\"Cards\":[{\"Id\":\"card\",\"MetricIds\":[\"overview.runtime.kv-cache.usage\"]}]}");
        var normalized = OverviewDashboardLayoutPolicy.Normalize(legacy);

        Assert.Null(normalized.MetricThresholds);
        Assert.Equal(new OverviewDashboardMetricThreshold(true, 85, 95),
            OverviewDashboardThresholdPolicy.Effective(OverviewDashboardMetricIds.KvCacheUsage, normalized.MetricThresholds));
    }

    [Fact]
    public void Normalize_DropsInvalidAndUnsupportedOverrides()
    {
        var layout = OverviewDashboardLayoutPolicy.Default with
        {
            MetricThresholds = new Dictionary<string, OverviewDashboardMetricThreshold>
            {
                [OverviewDashboardMetricIds.AverageGenerationRate] = new(true, 1, 2),
                [OverviewDashboardMetricIds.KvCacheUsage] = new(true, 90, 50),
                [OverviewDashboardMetricIds.QueuedRequests] = new(false, 0, null)
            }
        };

        var normalized = OverviewDashboardLayoutPolicy.Normalize(layout);

        var saved = Assert.Single(normalized.MetricThresholds!);
        Assert.Equal(OverviewDashboardMetricIds.QueuedRequests, saved.Key);
    }

    [Fact]
    public void WithDetectedGpuCards_KeepsThresholds()
    {
        var layout = OverviewDashboardLayoutPolicy.SetMetricThreshold(
            OverviewDashboardLayoutPolicy.Default,
            OverviewDashboardMetricIds.GpuTemperature(0),
            new OverviewDashboardMetricThreshold(true, 80, 90));

        var detected = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(layout, [0]);

        Assert.Contains(detected.Cards, card => card.Id == "default-gpu-0");
        Assert.True(detected.MetricThresholds!.ContainsKey(OverviewDashboardMetricIds.GpuTemperature(0)));
    }
}
