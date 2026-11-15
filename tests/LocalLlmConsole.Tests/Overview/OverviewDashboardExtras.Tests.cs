using System.Globalization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class OverviewDashboardExtrasTests : ManagerRegressionTestBase
{
    [Fact]
    public void SpeculativeSpeedupUsesReportedVerificationStepsOnly()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var withSteps = new RuntimeMetricSummaryTracker().Apply("runtime",
        [
            Sample("llamacpp:spec_decode_num_draft_tokens_total", 300),
            Sample("llamacpp:spec_decode_num_accepted_tokens_total", 150),
            Sample("llamacpp:spec_decode_num_drafts_total", 100)
        ], settings, null, null, DateTimeOffset.UtcNow);
        var withoutSteps = new RuntimeMetricSummaryTracker().Apply("runtime",
        [
            Sample("llamacpp:spec_decode_num_draft_tokens_total", 300),
            Sample("llamacpp:spec_decode_num_accepted_tokens_total", 150)
        ], settings, null, null, DateTimeOffset.UtcNow);

        Assert.Equal(2.5, withSteps.Atomic.SpeculativeTokensPerStep);
        Assert.Equal(50, withSteps.Atomic.DraftAcceptancePercent);
        Assert.Null(withoutSteps.Atomic.SpeculativeTokensPerStep);
        Assert.Equal(50, withoutSteps.Atomic.DraftAcceptancePercent);
    }

    [Fact]
    public void PromptCacheHitRateFollowsTheLatestPromptActivity()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot());
        var tracker = new RuntimeMetricSummaryTracker();
        var now = DateTimeOffset.UtcNow;
        RuntimeMetricAtomicSnapshot Poll(double processed, double cached, int second)
            => tracker.Apply("runtime",
            [
                Sample("llamacpp:prompt_tokens_total", processed),
                Sample("llamacpp:prompt_tokens_cached_total", cached)
            ], settings, null, null, now.AddSeconds(second)).Atomic;

        var first = Poll(100, 0, 0);
        Assert.Null(first.RecentPromptCacheHitPercent);

        var hit = Poll(120, 80, 1);
        Assert.Equal(80, hit.RecentPromptCacheHitPercent);
        Assert.Equal(80, hit.RecentPromptCachedTokens);
        Assert.Equal(100, hit.RecentPromptTokens);
        Assert.Equal(40, hit.PromptCacheReusePercent);

        Assert.Equal(80, Poll(120, 80, 2).RecentPromptCacheHitPercent);
        Assert.Equal(0, Poll(220, 80, 3).RecentPromptCacheHitPercent);
        Assert.Null(Poll(10, 0, 4).RecentPromptCacheHitPercent);

        var uncached = new RuntimeMetricSummaryTracker();
        uncached.Apply("runtime", [Sample("llamacpp:prompt_tokens_total", 10)], settings, null, null, now);
        Assert.Null(uncached.Apply("runtime", [Sample("llamacpp:prompt_tokens_total", 50)], settings, null, null, now.AddSeconds(1))
            .Atomic.RecentPromptCacheHitPercent);
    }

    [Fact]
    public void NewEfficiencyMetricsAreValidOptionalChartableRows()
    {
        var layout = OverviewDashboardLayoutPolicy.AddCard(OverviewDashboardLayoutPolicy.Default,
            [OverviewDashboardMetricIds.SpeculativeSpeedup, OverviewDashboardMetricIds.PromptCacheHitRate]);
        var card = layout.Cards[^1];
        Assert.Equal([OverviewDashboardMetricIds.SpeculativeSpeedup, OverviewDashboardMetricIds.PromptCacheHitRate], card.MetricIds);
        var charted = OverviewDashboardLayoutPolicy.SetChartVisibility(layout, card.Id, OverviewDashboardMetricIds.PromptCacheHitRate, true);
        Assert.Equal([OverviewDashboardMetricIds.PromptCacheHitRate], charted.Cards[^1].ChartMetricIds);

        var definitions = OverviewDashboardMetricRegistry.BuiltInDefinitions();
        var speedup = definitions.Single(item => item.Id == OverviewDashboardMetricIds.SpeculativeSpeedup);
        Assert.True(speedup.RequiresObservedValue);
        Assert.Contains("÷", speedup.Tooltip, StringComparison.Ordinal);
        var hitRate = definitions.Single(item => item.Id == OverviewDashboardMetricIds.PromptCacheHitRate);
        Assert.Equal(100, hitRate.FixedMaximum);
        Assert.Contains("÷", hitRate.Tooltip, StringComparison.Ordinal);
    }

    [Fact]
    public void CardTextIncludesNameValueUnitDetailAndTimestamps()
    {
        var captured = new DateTimeOffset(2026, 11, 15, 9, 30, 5, TimeSpan.FromHours(1));
        var text = OverviewDashboardCardTextFormatter.Format(
            "Throughput",
            [
                ("Average generation rate", new OverviewDashboardMetricReading(
                    OverviewDashboardMetricIds.AverageGenerationRate, "30.0", Unit: "t/s", Detail: "busy")),
                ("Prompt cache reuse", new OverviewDashboardMetricReading(
                    OverviewDashboardMetricIds.PromptCacheReuse, "40", Unit: "%",
                    LastKnownCapturedAt: captured.AddMinutes(-1))),
                ("Draft acceptance", null)
            ],
            null,
            captured);

        var lines = text.Split(Environment.NewLine);
        Assert.Equal("Throughput", lines[0]);
        Assert.Equal("Captured 2026-11-15 09:30:05 +01:00", lines[1]);
        Assert.Equal("Average generation rate: 30.0 t/s — busy", lines[2]);
        Assert.Equal("Prompt cache reuse: 40% (last known 2026-11-15 09:29:05 +01:00)", lines[3]);
        Assert.Equal("Draft acceptance: N/A", lines[4]);

        var sessions = OverviewDashboardCardTextFormatter.Format("Loaded models", [],
        [
            new LoadedSessionDashboardRow("s1", "m1", "Qwen", "Fast", LoadedModelSessionStatus.Running, true, 8081, 30, 1, 4, 25)
        ], captured).Split(Environment.NewLine);
        Assert.Equal("Qwen · Fast (selected): Loaded — 30.0 t/s · 1/4 slots · KV 25% · port 8081", sessions[2]);
        Assert.Equal("No models are running.",
            OverviewDashboardCardTextFormatter.Format("Loaded models", [], [], captured).Split(Environment.NewLine)[2]);
    }

    private static PrometheusSample Sample(string name, double value)
        => new(name, "", value, value.ToString(CultureInfo.InvariantCulture), "counter", "");
}
