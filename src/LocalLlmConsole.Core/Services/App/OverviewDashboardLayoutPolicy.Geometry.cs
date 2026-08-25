namespace LocalLlmConsole.Services;

public static partial class OverviewDashboardLayoutPolicy
{
    public const int FixedCardSizeLayoutVersion = 8;
    public const double HorizontalUnits = 12;
    public const double MinimumCardWidth = 1.5;
    public const double MinimumCardHeight = 84;
    public const double MaximumCardHeight = 1200;
    public const double MaximumCardY = 10000;
    public const double CardGap = 10;

    public static OverviewDashboardLayout WithDetectedGpuCards(
        OverviewDashboardLayout layout,
        IEnumerable<int> detectedGpuIndices)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(detectedGpuIndices);
        var normalized = Normalize(layout);
        var detected = detectedGpuIndices
            .Where(index => index is >= 0 and < 16)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        if (!IsProductionDefaultFamily(normalized))
            return normalized;

        var configured = normalized.Cards
            .Select(CardGpuIndex)
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        if (configured.SequenceEqual(detected)) return normalized;

        return RebuildProductionDefaultLayout(normalized, detected, resetDefaultPresentation: false);
    }

    private static OverviewDashboardLayout MigrateProductionDefaultLayout(OverviewDashboardLayout layout)
    {
        var configured = layout.Cards
            .Select(CardGpuIndex)
            .Where(index => index is not null)
            .Select(index => index!.Value)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        return RebuildProductionDefaultLayout(layout, configured, resetDefaultPresentation: true);
    }

    private static OverviewDashboardLayout RebuildProductionDefaultLayout(
        OverviewDashboardLayout layout,
        IReadOnlyList<int> gpuIndices,
        bool resetDefaultPresentation)
    {
        var existingById = layout.Cards.ToDictionary(card => card.Id, StringComparer.OrdinalIgnoreCase);
        var generated = CreateProductionDefault(gpuIndices);
        var cards = generated.Cards.Select(card => existingById.TryGetValue(card.Id, out var existing)
            ? card with
            {
                Title = existing.Title,
                ChartMetricId = resetDefaultPresentation ? card.ChartMetricId : existing.ChartMetricId,
                ChartMetricIds = resetDefaultPresentation ? card.ChartMetricIds : existing.ChartMetricIds
            }
            : card).ToList();
        cards.AddRange(layout.Cards.Where(IsDefaultCompatibilityCard));
        return Normalize(new OverviewDashboardLayout(
            CurrentVersion,
            cards,
            CardSizesLocked: resetDefaultPresentation ? false : layout.CardSizesLocked,
            LockedSurfaceWidth: resetDefaultPresentation ? 0 : layout.LockedSurfaceWidth));
    }

    public static IReadOnlyList<int> DefaultGpuCardIndices(IEnumerable<HostGpuSnapshot> gpus)
    {
        ArgumentNullException.ThrowIfNull(gpus);
        return gpus
            .Where(gpu => !IsIntegratedGraphics(gpu.Name))
            .Select(gpu => gpu.Index)
            .Where(index => index is >= 0 and < 16)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
    }

    private static OverviewDashboardLayout CreateProductionDefault(IReadOnlyList<int> gpuIndices)
    {
        var cards = new List<OverviewDashboardCardLayout>
        {
            new(
                "default-runtime",
                [
                    OverviewDashboardMetricIds.AveragePromptRate,
                    OverviewDashboardMetricIds.AverageGenerationRate,
                    OverviewDashboardMetricIds.MtpAcceptedTokens,
                    OverviewDashboardMetricIds.MtpGeneratedTokens,
                    OverviewDashboardMetricIds.KvCacheUsage,
                    OverviewDashboardMetricIds.KvCacheUsed,
                    OverviewDashboardMetricIds.GeneratedTokens,
                    OverviewDashboardMetricIds.PromptTokens
                ],
                Height: OverviewDashboardCardHeight.Tall)
        };

        for (var ordinal = 0; ordinal < gpuIndices.Count; ordinal++)
        {
            var gpuIndex = gpuIndices[ordinal];
            cards.Add(new OverviewDashboardCardLayout(
                $"default-gpu-{gpuIndex}",
                [
                    OverviewDashboardMetricIds.Gpu(gpuIndex),
                    OverviewDashboardMetricIds.GpuTemperature(gpuIndex),
                    OverviewDashboardMetricIds.GpuVram(gpuIndex),
                    OverviewDashboardMetricIds.GpuPower(gpuIndex),
                    OverviewDashboardMetricIds.ObservedGpuEnergy(gpuIndex)
                ],
                Height: OverviewDashboardCardHeight.Tall,
                ChartMetricId: OverviewDashboardMetricIds.Gpu(gpuIndex),
                ChartMetricIds:
                [
                    OverviewDashboardMetricIds.Gpu(gpuIndex)
                ]));
        }

        cards.Add(new OverviewDashboardCardLayout(
            "default-host",
            [
                OverviewDashboardMetricIds.Cpu,
                OverviewDashboardMetricIds.RamUsed,
                OverviewDashboardMetricIds.ObservedGpuEnergyTotal,
                OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal
            ],
            Height: OverviewDashboardCardHeight.Tall,
            ChartMetricId: OverviewDashboardMetricIds.Cpu,
            ChartMetricIds: [OverviewDashboardMetricIds.Cpu]));

        var columns = Math.Min(cards.Count, 4);
        var width = HorizontalUnits / columns;
        for (var ordinal = 0; ordinal < cards.Count; ordinal++)
        {
            var row = ordinal / columns;
            var column = ordinal % columns;
            cards[ordinal] = cards[ordinal] with
            {
                Bounds = new OverviewDashboardCardBounds(
                    column * width,
                    row * (231 + CardGap),
                    width,
                    231)
            };
        }

        return Normalize(new OverviewDashboardLayout(
            CurrentVersion,
            cards,
            CardSizesLocked: false,
            LockedSurfaceWidth: 0));
    }

    private static bool IsProductionDefaultFamily(OverviewDashboardLayout layout)
    {
        var runtimeMetrics = new HashSet<string>(
        [
            OverviewDashboardMetricIds.AveragePromptRate,
            OverviewDashboardMetricIds.AverageGenerationRate,
            OverviewDashboardMetricIds.MtpAcceptedTokens,
            OverviewDashboardMetricIds.MtpGeneratedTokens,
            OverviewDashboardMetricIds.KvCacheUsage,
            OverviewDashboardMetricIds.KvCacheUsed,
            OverviewDashboardMetricIds.GeneratedTokens,
            OverviewDashboardMetricIds.PromptTokens
        ], StringComparer.Ordinal);
        var hostMetrics = new HashSet<string>(
        [
            OverviewDashboardMetricIds.Cpu,
            OverviewDashboardMetricIds.RamUsed,
            OverviewDashboardMetricIds.ObservedGpuEnergyTotal,
            OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal
        ], StringComparer.Ordinal);
        var runtimeCards = layout.Cards.Count(card => runtimeMetrics.SetEquals(card.MetricIds));
        var hostCards = layout.Cards.Count(card => hostMetrics.SetEquals(card.MetricIds));
        return runtimeCards == 1
               && hostCards == 1
               && layout.Cards.Count >= 2
               && layout.Cards.Where(card => !runtimeMetrics.SetEquals(card.MetricIds)
                                             && !hostMetrics.SetEquals(card.MetricIds))
                   .All(card => CardGpuIndex(card) is not null || IsDefaultCompatibilityCard(card));
    }

    private static bool IsIntegratedGraphics(string? name)
    {
        var value = Regex.Replace(name ?? "", @"\s+", " ").Trim();
        if (value.Length == 0) return false;
        if (value.Contains("integrated graphics", StringComparison.OrdinalIgnoreCase)
            || value.Contains("iGPU", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Qualcomm Adreno", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Contains("Intel", StringComparison.OrdinalIgnoreCase))
        {
            if (Regex.IsMatch(value, @"\b(?:Arc(?:\(TM\))?\s+(?:Pro\s+)?[AB]\d{2,3})\b",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                || value.Contains("Data Center GPU", StringComparison.OrdinalIgnoreCase))
                return false;
            return value.Contains("UHD", StringComparison.OrdinalIgnoreCase)
                   || Regex.IsMatch(value, @"\bHD\s+Graphics\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
                   || value.Contains("Iris", StringComparison.OrdinalIgnoreCase)
                   || value.Contains("Arc", StringComparison.OrdinalIgnoreCase)
                   || Regex.IsMatch(value, @"\bIntel(?:\(R\))?\s+Graphics\b",
                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        if (!value.Contains("Radeon", StringComparison.OrdinalIgnoreCase)) return false;
        if (value.Contains("Radeon RX", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Radeon Pro", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase)
            || value.Contains("Radeon Instinct", StringComparison.OrdinalIgnoreCase))
            return false;
        return Regex.IsMatch(value, @"Radeon(?:\(TM\))?\s+Graphics\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(value, @"\bRadeon\s+\d{3,4}M(?:\s+Graphics)?\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
               || Regex.IsMatch(value, @"\bVega\s+\d+\s+Graphics\b",
                   RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool IsDefaultCompatibilityCard(OverviewDashboardCardLayout card)
        => card.MetricIds.SequenceEqual([OverviewDashboardMetricIds.ModelStatus], StringComparer.Ordinal)
           || card.MetricIds.ToHashSet(StringComparer.Ordinal).SetEquals(DefaultSlotMetricIds);

    private static int? CardGpuIndex(OverviewDashboardCardLayout card)
    {
        var indices = new HashSet<int>();
        foreach (var metricId in card.MetricIds)
        {
            if (!TryGpuMetricIndex(metricId, out var index)) return null;
            indices.Add(index);
        }
        return indices.Count == 1 ? indices.Single() : null;
    }

    private static bool TryGpuMetricIndex(string metricId, out int index)
        => OverviewDashboardMetricIds.TryParseGpu(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuVram(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuPower(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuCoreClock(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuTemperature(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuVramTemperature(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuMemoryClock(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuMemoryActivity(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuFanSpeed(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuPowerLimit(metricId, out index)
           || OverviewDashboardMetricIds.TryParseGpuThrottling(metricId, out index)
           || OverviewDashboardMetricIds.TryParseObservedGpuEnergy(metricId, out index)
           || OverviewDashboardMetricIds.TryParseObservedGpuElectricityCost(metricId, out index);

    public static OverviewDashboardLayout SetCardSizesLocked(
        OverviewDashboardLayout layout,
        bool locked,
        double surfaceWidth,
        IReadOnlyDictionary<string, OverviewDashboardCardBounds> renderedBounds)
    {
        ArgumentNullException.ThrowIfNull(renderedBounds);
        var normalized = Normalize(layout);
        if (!ValidSurfaceWidth(surfaceWidth)) return normalized;
        var cards = normalized.Cards.Select(card =>
        {
            if (!renderedBounds.TryGetValue(card.Id, out var bounds) || !ValidBounds(bounds))
                return card;
            var constrained = ConstrainBounds(bounds);
            return card with
            {
                Bounds = constrained,
                ColumnSpan = Math.Clamp((int)Math.Round(
                    constrained.Width / 4,
                    MidpointRounding.AwayFromZero), 1, 3),
                Height = NearestLegacyHeight(constrained.Height)
            };
        }).ToArray();
        return Normalize(normalized with
        {
            Cards = cards,
            CardSizesLocked = locked,
            LockedSurfaceWidth = locked ? surfaceWidth : 0
        });
    }

    public static OverviewDashboardLayout ReorderMetrics(
        OverviewDashboardLayout layout,
        string cardId,
        IReadOnlyList<string> metricIds)
    {
        ArgumentNullException.ThrowIfNull(metricIds);
        var normalized = Normalize(layout);
        return UpdateCard(normalized, cardId, card =>
        {
            var requested = metricIds
                .Where(IsValidMetricId)
                .Distinct(StringComparer.Ordinal)
                .Take(MaximumMetricsPerCard)
                .ToArray();
            return requested.Length == card.MetricIds.Count
                   && requested.ToHashSet(StringComparer.Ordinal).SetEquals(card.MetricIds)
                ? card with { MetricIds = requested }
                : card;
        });
    }

    public static OverviewDashboardLayout ResizeCard(
        OverviewDashboardLayout layout,
        string cardId,
        int columnSpan,
        OverviewDashboardCardHeight height)
        => UpdateCard(layout, cardId, card =>
        {
            var span = Math.Clamp(columnSpan, 1, 3);
            var bounds = card.Bounds ?? LegacyBounds(card, 0, 0);
            return card with
            {
                ColumnSpan = span,
                Height = height,
                Bounds = ConstrainBounds(bounds with
                {
                    Width = span * 4,
                    Height = CardHeight(height)
                })
            };
        });

    public static OverviewDashboardLayout SetCardBounds(
        OverviewDashboardLayout layout,
        string cardId,
        OverviewDashboardCardBounds bounds)
        => UpdateCard(layout, cardId, card =>
        {
            var constrained = ConstrainBounds(bounds);
            return card with
            {
                Bounds = constrained,
                ColumnSpan = Math.Clamp((int)Math.Round(constrained.Width / 4, MidpointRounding.AwayFromZero), 1, 3),
                Height = NearestLegacyHeight(constrained.Height)
            };
        });

    public static OverviewDashboardCardBounds ConstrainBounds(OverviewDashboardCardBounds bounds)
    {
        var width = FiniteOr(bounds.Width, 4);
        width = Math.Clamp(width, MinimumCardWidth, HorizontalUnits);
        var x = Math.Clamp(FiniteOr(bounds.X, 0), 0, HorizontalUnits - width);
        var height = Math.Clamp(FiniteOr(bounds.Height, CardHeight(OverviewDashboardCardHeight.Standard)),
            MinimumCardHeight, MaximumCardHeight);
        var y = Math.Clamp(FiniteOr(bounds.Y, 0), 0, MaximumCardY);
        return new OverviewDashboardCardBounds(x, y, width, height);
    }

    public static double CardHeight(OverviewDashboardCardHeight height)
        => height switch
        {
            OverviewDashboardCardHeight.Compact => 88,
            OverviewDashboardCardHeight.Tall => 176,
            _ => 112
        };

    private static void AssignMissingBounds(List<OverviewDashboardCardLayout> cards)
    {
        var missing = cards.Where(card => card.Bounds is null).ToArray();
        if (missing.Length == 0) return;

        var y = cards.Where(card => card.Bounds is not null)
            .Select(card => card.Bounds!.Y + card.Bounds.Height + CardGap)
            .DefaultIfEmpty(0)
            .Max();
        var x = 0d;
        var rowHeight = 0d;
        foreach (var card in missing)
        {
            var width = Math.Clamp(card.ColumnSpan, 1, 3) * 4d;
            var height = CardHeight(card.Height);
            if (x > 0 && x + width > HorizontalUnits)
            {
                x = 0;
                y += rowHeight + CardGap;
                rowHeight = 0;
            }
            var index = cards.FindIndex(item => string.Equals(item.Id, card.Id, StringComparison.OrdinalIgnoreCase));
            cards[index] = card with { Bounds = ConstrainBounds(new(x, y, width, height)) };
            x += width;
            rowHeight = Math.Max(rowHeight, height);
        }
    }

    private static OverviewDashboardCardBounds NextCardBounds(IReadOnlyList<OverviewDashboardCardLayout> cards)
    {
        var y = cards
            .Select(card => card.Bounds ?? LegacyBounds(card, 0, 0))
            .Select(bounds => bounds.Y + bounds.Height + CardGap)
            .DefaultIfEmpty(0)
            .Max();
        return ConstrainBounds(new OverviewDashboardCardBounds(0, y, 4, CardHeight(OverviewDashboardCardHeight.Standard)));
    }

    private static OverviewDashboardCardBounds LegacyBounds(OverviewDashboardCardLayout card, double x, double y)
        => new(x, y, Math.Clamp(card.ColumnSpan, 1, 3) * 4, CardHeight(card.Height));

    private static bool ValidBounds(OverviewDashboardCardBounds? bounds)
        => bounds is not null
           && double.IsFinite(bounds.X)
           && double.IsFinite(bounds.Y)
           && double.IsFinite(bounds.Width)
           && double.IsFinite(bounds.Height)
           && bounds.Width > 0
           && bounds.Height > 0;

    private static bool ValidSurfaceWidth(double width)
        => double.IsFinite(width) && width >= 1 && width <= 100000;

    private static double FiniteOr(double value, double fallback) => double.IsFinite(value) ? value : fallback;

    private static OverviewDashboardCardHeight NearestLegacyHeight(double height)
        => Enum.GetValues<OverviewDashboardCardHeight>()
            .MinBy(candidate => Math.Abs(CardHeight(candidate) - height));
}
