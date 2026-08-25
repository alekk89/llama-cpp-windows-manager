namespace LocalLlmConsole.Services;

public static partial class OverviewDashboardLayoutPolicy
{
    public const int CurrentVersion = 12;
    public const int AtomicMetricLayoutVersion = 2;
    public const int MultiChartLayoutVersion = 4;
    public const int AverageRateLayoutVersion = 5;
    public const int ObservedEnergyLayoutVersion = 7;
    public const int CuratedMetricsLayoutVersion = 10;
    public const int ResponsiveDefaultLayoutVersion = 11;
    public const int CompactDefaultLayoutVersion = 12;
    public const int MaximumCards = 24; // Defensive persistence boundary; ordinary customization remains below it.
    public const int MaximumMetricsPerCard = 64;
    private static readonly string[] HardwareMetricIds =
    [
        OverviewDashboardMetricIds.Cpu,
        OverviewDashboardMetricIds.Ram,
        OverviewDashboardMetricIds.Gpu(0)
    ];

    private static readonly string[] SlotMetricIds =
    [
        OverviewDashboardMetricIds.ActiveSlots,
        OverviewDashboardMetricIds.QueuedRequests,
        OverviewDashboardMetricIds.BusyDecodeSlots
    ];

    private static readonly string[] TokenMetricIds =
    [
        OverviewDashboardMetricIds.AverageGenerationRate,
        OverviewDashboardMetricIds.AveragePromptRate,
        OverviewDashboardMetricIds.GeneratedTokens,
        OverviewDashboardMetricIds.PromptTokens
    ];

    private static readonly string[] MtpMetricIds =
    [
        OverviewDashboardMetricIds.AverageMtpGeneratedRate,
        OverviewDashboardMetricIds.DraftAcceptance,
        OverviewDashboardMetricIds.MtpGeneratedTokens,
        OverviewDashboardMetricIds.MtpAcceptedTokens
    ];

    private static readonly string[] DefaultSlotMetricIds =
        [OverviewDashboardMetricIds.ActiveSlots, OverviewDashboardMetricIds.QueuedRequests];
    private static readonly string[] DefaultTokenMetricIds =
        [OverviewDashboardMetricIds.AverageGenerationRate, OverviewDashboardMetricIds.AveragePromptRate];
    private static readonly string[] DefaultMtpMetricIds = [OverviewDashboardMetricIds.DraftAcceptance];
    private static readonly string[] DefaultKvCacheMetricIds = [OverviewDashboardMetricIds.KvCacheUsage];

    private static readonly string[] KvCacheMetricIds =
    [
        OverviewDashboardMetricIds.KvCacheUsed,
        OverviewDashboardMetricIds.KvCacheCapacity,
        OverviewDashboardMetricIds.KvCacheUsage,
        OverviewDashboardMetricIds.KvCacheAllocation
    ];

    private static readonly string[] BuiltInMetricIds =
    [
        OverviewDashboardMetricIds.ModelStatus,
        OverviewDashboardMetricIds.Cpu,
        OverviewDashboardMetricIds.CpuTemperature,
        OverviewDashboardMetricIds.CpuCoreClock,
        OverviewDashboardMetricIds.Ram,
        OverviewDashboardMetricIds.RamUsed,
        OverviewDashboardMetricIds.RamClock,
        OverviewDashboardMetricIds.ServerProcessCpu,
        OverviewDashboardMetricIds.ServerProcessMemory,
        OverviewDashboardMetricIds.RecentGenerationRate,
        OverviewDashboardMetricIds.RecentPromptRate,
        OverviewDashboardMetricIds.PromptCacheReuse,
        OverviewDashboardMetricIds.DraftAcceptance,
        OverviewDashboardMetricIds.PeakContextUsed,
        OverviewDashboardMetricIds.ContextShifts,
        OverviewDashboardMetricIds.GatewayTimeToFirstData,
        OverviewDashboardMetricIds.GatewayRequestDuration,
        OverviewDashboardMetricIds.GatewayResponseThroughput,
        OverviewDashboardMetricIds.GatewayRequests,
        OverviewDashboardMetricIds.GatewayFailures,
        OverviewDashboardMetricIds.GatewayFailureRate,
        .. SlotMetricIds,
        .. TokenMetricIds,
        .. MtpMetricIds,
        .. KvCacheMetricIds
    ];

    private static readonly OverviewDashboardLegacyVisibility ProductionDefaultVisibility =
        new(false, true, false, true, true, true);

    public static OverviewDashboardLayout Default => CreateProductionDefault([]);

    public static OverviewDashboardLayout CreateDefault(OverviewDashboardLegacyVisibility visibility)
        => ApplyLegacyVisibilityChanges(Default, ProductionDefaultVisibility, visibility);

    public static OverviewDashboardLayout Normalize(
        OverviewDashboardLayout? layout,
        OverviewDashboardLegacyVisibility? fallbackVisibility = null)
    {
        if (layout is null)
            return fallbackVisibility is { } visibility ? CreateDefault(visibility) : Default;
        if (layout.Version < CompactDefaultLayoutVersion && IsProductionDefaultFamily(layout))
            return MigrateProductionDefaultLayout(layout);

        var cards = new List<OverviewDashboardCardLayout>();
        var cardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in layout.Cards ?? [])
        {
            if (cards.Count >= MaximumCards) break;
            var expandedMetrics = (candidate.MetricIds ?? [])
                .SelectMany(metricId => ExpandMetricId(metricId, layout.Version))
                .Where(IsValidMetricId)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var candidateCharts = layout.Version >= MultiChartLayoutVersion && candidate.ChartMetricIds is not null
                ? candidate.ChartMetricIds
                : [candidate.ChartMetricId];
            var requestedCharts = candidateCharts
                .Select(metricId => MigrateChartMetricId(metricId, layout.Version))
                .Where(CanPersistChart)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var metricGroups = layout.Version < AtomicMetricLayoutVersion
                ? expandedMetrics.Chunk(MaximumMetricsPerCard)
                : [expandedMetrics.Take(MaximumMetricsPerCard).ToArray()];
            var part = 0;
            foreach (var metrics in metricGroups)
            {
                if (cards.Count >= MaximumCards || metrics.Length == 0) break;
                var requestedId = part++ == 0 ? candidate.Id : $"{candidate.Id}-part-{part}";
                var id = UniqueCardId(requestedId, cardIds, cards.Count + 1);
                cardIds.Add(id);
                var chartMetricIds = requestedCharts
                    .Where(requested => metrics.Contains(requested, StringComparer.Ordinal))
                    .ToArray();
                cards.Add(candidate with
                {
                    Id = id,
                    MetricIds = metrics,
                    ColumnSpan = Math.Clamp(candidate.ColumnSpan, 1, 3),
                    Height = Enum.IsDefined(candidate.Height) ? candidate.Height : OverviewDashboardCardHeight.Standard,
                    ChartMetricId = chartMetricIds.FirstOrDefault() ?? "",
                    ChartMetricIds = chartMetricIds,
                    Title = NormalizeCardTitle(layout.Version >= CardTitleLayoutVersion ? candidate.Title : ""),
                    Bounds = ValidBounds(candidate.Bounds) ? ConstrainBounds(candidate.Bounds!) : null
                });
            }
        }

        if (cards.Count == 0
            && layout.Version < CurrentVersion
            && layout.Cards is { Count: > 0 })
            return Default;

        AssignMissingBounds(cards);

        var cardSizesLocked = layout.Version >= FixedCardSizeLayoutVersion
                              && layout.CardSizesLocked
                              && ValidSurfaceWidth(layout.LockedSurfaceWidth);
        return new OverviewDashboardLayout(
            CurrentVersion,
            cards,
            cardSizesLocked,
            cardSizesLocked ? layout.LockedSurfaceWidth : 0);
    }

    public static OverviewDashboardLegacyVisibility LegacyVisibility(OverviewDashboardLayout? layout)
    {
        var metricIds = Normalize(layout).Cards
            .SelectMany(card => card.MetricIds)
            .ToHashSet(StringComparer.Ordinal);
        return new OverviewDashboardLegacyVisibility(
            metricIds.Contains(OverviewDashboardMetricIds.ModelStatus),
            metricIds.Any(IsHardwareMetric),
            metricIds.Overlaps(SlotMetricIds),
            metricIds.Overlaps(TokenMetricIds),
            metricIds.Overlaps(MtpMetricIds),
            metricIds.Overlaps(KvCacheMetricIds));
    }

    public static OverviewDashboardLegacyVisibility LegacyVisibility(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return new OverviewDashboardLegacyVisibility(
            settings.ShowOverviewModelStatus,
            settings.ShowOverviewHardware,
            settings.ShowOverviewSlots,
            settings.ShowOverviewTokens,
            settings.ShowOverviewMtpTokens,
            settings.ShowOverviewKvCache);
    }

    public static AppSettings WithLayout(AppSettings settings, OverviewDashboardLayout? layout)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = Normalize(layout, LegacyVisibility(settings));
        var visibility = LegacyVisibility(normalized);
        return settings with
        {
            OverviewDashboardLayout = normalized,
            ShowOverviewModelStatus = visibility.ModelStatus,
            ShowOverviewHardware = visibility.Hardware,
            ShowOverviewSlots = visibility.Slots,
            ShowOverviewTokens = visibility.Tokens,
            ShowOverviewMtpTokens = visibility.MtpTokens,
            ShowOverviewKvCache = visibility.KvCache
        };
    }

    public static OverviewDashboardLayout ApplyLegacyVisibilityChanges(
        OverviewDashboardLayout? layout,
        OverviewDashboardLegacyVisibility previous,
        OverviewDashboardLegacyVisibility updated)
    {
        var result = Normalize(layout, previous);
        result = ApplyChangedVisibility(result, [OverviewDashboardMetricIds.ModelStatus], previous.ModelStatus, updated.ModelStatus);
        result = ApplyChangedVisibility(result, HardwareMetricIds, previous.Hardware, updated.Hardware, IsHardwareMetric);
        result = ApplyChangedVisibility(result, SlotMetricIds, previous.Slots, updated.Slots);
        result = ApplyChangedVisibility(result, TokenMetricIds, previous.Tokens, updated.Tokens);
        result = ApplyChangedVisibility(result, MtpMetricIds, previous.MtpTokens, updated.MtpTokens);
        return ApplyChangedVisibility(result, KvCacheMetricIds, previous.KvCache, updated.KvCache);
    }

    public static OverviewDashboardLayout AddCard(OverviewDashboardLayout layout, IEnumerable<string> metricIds)
    {
        var normalized = Normalize(layout);
        if (normalized.Cards.Count >= MaximumCards) return normalized;
        var metrics = metricIds.Where(IsValidMetricId).Distinct(StringComparer.Ordinal).Take(MaximumMetricsPerCard).ToArray();
        if (metrics.Length == 0) return normalized;
        var ids = normalized.Cards.Select(card => card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var id = UniqueCardId("card", ids, normalized.Cards.Count + 1);
        var bounds = NextCardBounds(normalized.Cards);
        return Normalize(normalized with
        {
            Cards = [.. normalized.Cards, new OverviewDashboardCardLayout(id, metrics, Bounds: bounds)]
        });
    }

    public static OverviewDashboardLayout RemoveCard(OverviewDashboardLayout layout, string cardId)
        => Normalize(layout with
        {
            Cards = layout.Cards.Where(card => !string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase)).ToArray()
        });

    public static OverviewDashboardLayout MoveCard(OverviewDashboardLayout layout, string cardId, string beforeCardId)
    {
        var normalized = Normalize(layout);
        var cards = normalized.Cards.ToList();
        var source = cards.FindIndex(card => string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase));
        var target = cards.FindIndex(card => string.Equals(card.Id, beforeCardId, StringComparison.OrdinalIgnoreCase));
        if (source < 0 || target < 0 || source == target) return normalized;
        var card = cards[source];
        cards.RemoveAt(source);
        target = cards.FindIndex(item => string.Equals(item.Id, beforeCardId, StringComparison.OrdinalIgnoreCase));
        cards.Insert(target, card);
        return normalized with { Cards = cards };
    }

    public static OverviewDashboardLayout MoveCardToIndex(OverviewDashboardLayout layout, string cardId, int targetIndex)
    {
        var normalized = Normalize(layout);
        var cards = normalized.Cards.ToList();
        var source = cards.FindIndex(card => string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase));
        if (source < 0) return normalized;
        var card = cards[source];
        cards.RemoveAt(source);
        cards.Insert(Math.Clamp(targetIndex, 0, cards.Count), card);
        return normalized with { Cards = cards };
    }

    public static OverviewDashboardLayout AddMetrics(OverviewDashboardLayout layout, string cardId, IEnumerable<string> metricIds)
        => UpdateCard(layout, cardId, card => card with
        {
            MetricIds = card.MetricIds.Concat(metricIds)
                .Where(IsValidMetricId)
                .Distinct(StringComparer.Ordinal)
                .Take(MaximumMetricsPerCard)
                .ToArray()
        });

    public static OverviewDashboardLayout RemoveMetric(OverviewDashboardLayout layout, string cardId, string metricId)
    {
        var updated = UpdateCard(layout, cardId, card => card with
        {
            MetricIds = card.MetricIds.Where(id => !string.Equals(id, metricId, StringComparison.Ordinal)).ToArray(),
            ChartMetricIds = ChartMetricIds(card)
                .Where(id => !string.Equals(id, metricId, StringComparison.Ordinal))
                .ToArray()
        });
        return Normalize(updated);
    }

    public static OverviewDashboardLayout SetChart(OverviewDashboardLayout layout, string cardId, string metricId)
        => UpdateCard(layout, cardId, card => card with
        {
            ChartMetricId = card.MetricIds.Contains(metricId, StringComparer.Ordinal) && CanPersistChart(metricId) ? metricId : "",
            ChartMetricIds = card.MetricIds.Contains(metricId, StringComparer.Ordinal) && CanPersistChart(metricId) ? [metricId] : []
        });

    public static OverviewDashboardLayout SetChartVisibility(
        OverviewDashboardLayout layout,
        string cardId,
        string metricId,
        bool visible)
        => UpdateCard(layout, cardId, card =>
        {
            var charts = ChartMetricIds(card).ToList();
            charts.RemoveAll(id => string.Equals(id, metricId, StringComparison.Ordinal));
            if (visible && card.MetricIds.Contains(metricId, StringComparer.Ordinal) && CanPersistChart(metricId))
                charts.Add(metricId);
            return card with { ChartMetricIds = charts };
        });

    public static OverviewDashboardLayout ClearCharts(OverviewDashboardLayout layout, string cardId)
        => UpdateCard(layout, cardId, card => card with { ChartMetricId = "", ChartMetricIds = [] });

    public static OverviewDashboardLayout Reset() => Default;

    private static OverviewDashboardLayout SetMetricVisibility(
        OverviewDashboardLayout layout,
        IReadOnlyList<string> defaultMetricIds,
        bool visible,
        Func<string, bool>? belongsToGroup = null)
    {
        belongsToGroup ??= metricId => defaultMetricIds.Contains(metricId, StringComparer.Ordinal);
        var hasMetric = layout.Cards.Any(card => card.MetricIds.Any(belongsToGroup));
        if (visible == hasMetric) return layout;
        if (visible) return AddCard(layout, defaultMetricIds);

        var cards = layout.Cards.Select(card => card with
        {
            MetricIds = card.MetricIds.Where(id => !belongsToGroup(id)).ToArray(),
            ChartMetricIds = ChartMetricIds(card).Where(id => !belongsToGroup(id)).ToArray()
        });
        return Normalize(layout with { Cards = cards.ToArray() });
    }

    private static OverviewDashboardLayout ApplyChangedVisibility(
        OverviewDashboardLayout layout,
        IReadOnlyList<string> defaultMetricIds,
        bool previous,
        bool updated,
        Func<string, bool>? belongsToGroup = null)
        => previous == updated ? layout : SetMetricVisibility(layout, defaultMetricIds, updated, belongsToGroup);

    private static OverviewDashboardLayout UpdateCard(
        OverviewDashboardLayout layout,
        string cardId,
        Func<OverviewDashboardCardLayout, OverviewDashboardCardLayout> update)
        => Normalize(layout with
        {
            Cards = layout.Cards.Select(card => string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase)
                ? update(card)
                : card).ToArray()
        });

    private static void AddDefault(
        List<OverviewDashboardCardLayout> cards,
        IReadOnlyList<string> metricIds,
        bool visible,
        string chartMetricId = "")
    {
        if (!visible) return;
        cards.Add(new OverviewDashboardCardLayout(
            $"card-{cards.Count + 1}",
            metricIds,
            Height: metricIds.Count > 3 ? OverviewDashboardCardHeight.Tall : OverviewDashboardCardHeight.Standard,
            ChartMetricId: chartMetricId));
    }

    private static bool IsValidMetricId(string? metricId)
        => !string.IsNullOrWhiteSpace(metricId)
           && metricId.Length <= 512
           && (BuiltInMetricIds.Contains(metricId, StringComparer.Ordinal)
               || OverviewDashboardMetricIds.IsGpuMetric(metricId)
               || OverviewDashboardMetricIds.IsObservedGpuMetric(metricId)
               || OverviewDashboardMetricIds.TryParsePrometheus(metricId, out _, out _));

    private static bool CanPersistChart(string metricId)
        => IsValidMetricId(metricId)
           && IsChartableMetricId(metricId)
           && !SlotMetricIds.Contains(metricId, StringComparer.Ordinal);

    private static IReadOnlyList<string> ChartMetricIds(OverviewDashboardCardLayout card)
        => card.ChartMetricIds ?? (string.IsNullOrWhiteSpace(card.ChartMetricId) ? [] : [card.ChartMetricId]);

    private static bool IsHardwareMetric(string metricId)
        => string.Equals(metricId, OverviewDashboardMetricIds.Cpu, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.CpuTemperature, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.CpuCoreClock, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.Ram, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.RamUsed, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.RamClock, StringComparison.Ordinal)
           || OverviewDashboardMetricIds.IsObservedGpuMetric(metricId)
           || OverviewDashboardMetricIds.IsGpuMetric(metricId);

    private static string UniqueCardId(string? requested, IReadOnlySet<string> used, int ordinal)
    {
        var baseId = string.IsNullOrWhiteSpace(requested) ? $"card-{ordinal}" : requested.Trim();
        if (baseId.Length > 80) baseId = baseId[..80];
        if (!used.Contains(baseId)) return baseId;
        for (var suffix = 2; suffix <= MaximumCards + 1; suffix++)
        {
            var candidate = $"{baseId}-{suffix}";
            if (!used.Contains(candidate)) return candidate;
        }
        return $"card-{ordinal}-{Guid.NewGuid():N}";
    }
}
