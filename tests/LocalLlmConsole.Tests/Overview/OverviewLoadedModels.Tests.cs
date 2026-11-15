using System.Globalization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class OverviewLoadedModelsTests : ManagerRegressionTestBase
{
    [Fact]
    public void LoadedModelsCardIsOptionalAndAlwaysIsolated()
    {
        Assert.DoesNotContain(OverviewDashboardLayoutPolicy.Default.Cards,
            card => card.MetricIds.Contains(OverviewDashboardMetricIds.LoadedModels));

        var withMixedCard = OverviewDashboardLayoutPolicy.AddCard(
            OverviewDashboardLayoutPolicy.Default,
            [OverviewDashboardMetricIds.LoadedModels, OverviewDashboardMetricIds.QueuedRequests]);

        var defaultCount = OverviewDashboardLayoutPolicy.Default.Cards.Count;
        Assert.Equal(defaultCount + 2, withMixedCard.Cards.Count);
        var loadedCard = Assert.Single(withMixedCard.Cards, OverviewDashboardLayoutPolicy.IsLoadedModelsCard);
        Assert.NotNull(loadedCard.Bounds);
        var mixedRemainder = withMixedCard.Cards[defaultCount];
        Assert.Equal([OverviewDashboardMetricIds.QueuedRequests], mixedRemainder.MetricIds);

        var addedToLoadedCard = OverviewDashboardLayoutPolicy.AddMetrics(
            withMixedCard, loadedCard.Id, [OverviewDashboardMetricIds.Cpu]);
        Assert.Single(addedToLoadedCard.Cards, OverviewDashboardLayoutPolicy.IsLoadedModelsCard);
        Assert.Contains(addedToLoadedCard.Cards, card => card.MetricIds.SequenceEqual([OverviewDashboardMetricIds.Cpu]));

        var charted = OverviewDashboardLayoutPolicy.SetChartVisibility(
            withMixedCard, loadedCard.Id, OverviewDashboardMetricIds.LoadedModels, visible: true);
        Assert.Empty(charted.Cards.Single(OverviewDashboardLayoutPolicy.IsLoadedModelsCard).ChartMetricIds ?? []);
    }

    [Fact]
    public void SavedLayoutsKeepTheirCardsAndRoundTripTheLoadedModelsCard()
    {
        var saved = OverviewDashboardLayoutPolicy.AddCard(
            OverviewDashboardLayoutPolicy.Default, [OverviewDashboardMetricIds.LoadedModels]);
        var normalized = OverviewDashboardLayoutPolicy.Normalize(saved);

        Assert.Equal(saved.Cards.Select(card => card.Id), normalized.Cards.Select(card => card.Id));
        Assert.Equal(saved.Cards.Select(card => card.Bounds), normalized.Cards.Select(card => card.Bounds));
        Assert.Equal(OverviewDashboardLayoutPolicy.Normalize(OverviewDashboardLayoutPolicy.Default).Cards.Count,
            OverviewDashboardLayoutPolicy.Default.Cards.Count);

        var removed = OverviewDashboardLayoutPolicy.RemoveMetric(
            normalized,
            normalized.Cards.Single(OverviewDashboardLayoutPolicy.IsLoadedModelsCard).Id,
            OverviewDashboardMetricIds.LoadedModels);
        Assert.Equal(OverviewDashboardLayoutPolicy.Default.Cards.Count, removed.Cards.Count);
    }

    [Fact]
    public void ProjectionSummarizesEveryRunningSessionFromTheSharedPoll()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, ParallelSlots = 4, ContextSize = 8192 };
        var busy = RuntimeMetricSession(root, settings) with
        {
            SessionId = "busy",
            ModelName = "Qwen",
            LaunchProfileName = "Fast",
            IsSelected = false
        };
        var idle = RuntimeMetricSession(root, settings with { Port = 8082 }) with
        {
            SessionId = "idle",
            ModelName = "Gemma",
            LaunchProfileName = "",
            IsSelected = true
        };
        var loading = RuntimeSession(root, settings with { Port = 8083 }, LoadedModelSessionStatus.Loading, isRunning: true) with
        {
            SessionId = "loading",
            ModelName = "Llama"
        };
        var stopped = RuntimeSession(root, settings with { Port = 8084 }, LoadedModelSessionStatus.Stopped, isRunning: false) with
        {
            SessionId = "stopped",
            ModelName = "Phi"
        };
        var busyPoll = new RuntimeMetricPollResult(busy, RuntimeMetricPollerService.RuntimeKey(busy),
        [
            Sample("llamacpp:tokens_predicted_total", 600),
            Sample("llamacpp:tokens_predicted_seconds_total", 20),
            Sample("llamacpp:requests_processing", 2),
            Sample("llamacpp:kv_cache_usage_ratio", .25)
        ], null, "");
        var idlePoll = new RuntimeMetricPollResult(idle, RuntimeMetricPollerService.RuntimeKey(idle), [], null,
            "connection refused", EndpointResponded: false);

        var rows = LoadedSessionDashboardProjection.Project([busy, idle, loading, stopped], [busyPoll, idlePoll]);

        Assert.Equal(["idle", "loading", "busy"], rows.Select(row => row.SessionId));
        var busyRow = rows.Single(row => row.SessionId == "busy");
        Assert.Equal(30, busyRow.AverageGenerationRate);
        Assert.Equal(2, busyRow.ActiveSlots);
        Assert.Equal(4, busyRow.SlotCapacity);
        Assert.Equal(25, busyRow.KvCacheUsagePercent);
        Assert.Equal(8081, busyRow.Port);
        Assert.Equal("Fast", busyRow.ProfileName);
        Assert.False(busyRow.IsSelected);

        var idleRow = rows.Single(row => row.SessionId == "idle");
        Assert.True(idleRow.IsSelected);
        Assert.Null(idleRow.AverageGenerationRate);
        Assert.Null(idleRow.ActiveSlots);
        Assert.Null(idleRow.KvCacheUsagePercent);
        Assert.Equal(8082, idleRow.Port);
        Assert.Equal(LoadedModelSessionStatus.Loading, rows.Single(row => row.SessionId == "loading").Status);
        Assert.Empty(LoadedSessionDashboardProjection.Project([stopped], []));
    }

    private static PrometheusSample Sample(string name, double value)
        => new(name, "", value, value.ToString(CultureInfo.InvariantCulture), "gauge", "");
}
