namespace LocalLlmConsole.Services;

public static partial class OverviewDashboardLayoutPolicy
{
    /// <summary>
    /// Tidies the dashboard: runtime and gateway cards first, then Loaded models,
    /// then GPU cards in device order, then other hardware, keeping the current
    /// order within each group; every card gets the same width so rows fill evenly.
    /// </summary>
    public static OverviewDashboardLayout AutoArrange(OverviewDashboardLayout layout, int cardsPerRow)
    {
        var normalized = Normalize(layout);
        var span = EvenSpan(cardsPerRow);
        var cards = normalized.Cards
            .Select((card, index) => (Card: card, Index: index))
            .OrderBy(item => ArrangeGroup(item.Card))
            .ThenBy(item => CardGpuIndex(item.Card) ?? 0)
            .ThenBy(item => item.Index)
            .Select(item => item.Card with
            {
                Bounds = ConstrainBounds((item.Card.Bounds ?? LegacyBounds(item.Card, 0, 0)) with { Width = span }),
                ColumnSpan = Math.Clamp((int)Math.Round(span / 4d, MidpointRounding.AwayFromZero), 1, 3)
            })
            .ToArray();
        return normalized with { Cards = cards };
    }

    /// <summary>The column span that fits <paramref name="cardsPerRow"/> equal cards in a row.</summary>
    public static int EvenSpan(int cardsPerRow)
        => Math.Clamp(cardsPerRow, 1, 4) switch
        {
            1 => 12,
            2 => 6,
            3 => 4,
            _ => 3
        };

    private static int ArrangeGroup(OverviewDashboardCardLayout card)
    {
        if (IsLoadedModelsCard(card)) return 1;
        if (CardGpuIndex(card) is not null) return 2;
        return card.MetricIds.Count > 0 && card.MetricIds.All(id => MetricSource(id) == OverviewDashboardMetricSource.Hardware)
            ? 3
            : 0;
    }
}
