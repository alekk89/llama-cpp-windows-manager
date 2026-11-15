namespace LocalLlmConsole.Services;

public static partial class OverviewDashboardLayoutPolicy
{
    public static bool IsLoadedModelsCard(OverviewDashboardCardLayout card)
        => card.MetricIds.Count == 1
           && string.Equals(card.MetricIds[0], OverviewDashboardMetricIds.LoadedModels, StringComparison.Ordinal);

    // The loaded-models list is a session table rather than a metric row, so it
    // always occupies its own card. A mixed card keeps its other metrics and the
    // list moves to a new card placed by the normal missing-bounds pass.
    private static void IsolateLoadedModelsCards(List<OverviewDashboardCardLayout> cards, HashSet<string> cardIds)
    {
        for (var index = 0; index < cards.Count; index++)
        {
            var card = cards[index];
            if (card.MetricIds.Count < 2
                || !card.MetricIds.Contains(OverviewDashboardMetricIds.LoadedModels, StringComparer.Ordinal))
                continue;

            cards[index] = card with
            {
                MetricIds = card.MetricIds
                    .Where(id => !string.Equals(id, OverviewDashboardMetricIds.LoadedModels, StringComparison.Ordinal))
                    .ToArray()
            };
            if (cards.Count >= MaximumCards) continue;
            var id = UniqueCardId("loaded-models", cardIds, cards.Count + 1);
            cardIds.Add(id);
            cards.Add(new OverviewDashboardCardLayout(id, [OverviewDashboardMetricIds.LoadedModels], ChartMetricIds: []));
        }
    }
}
