namespace LocalLlmConsole;

public sealed record OverviewDashboardPlacement(
    string CardId,
    double Left,
    double Top,
    double Width,
    double Height);

/// <summary>
/// One card to flow: its saved column span, the narrowest width its content
/// can use, and its content height at a given width.
/// </summary>
public sealed record OverviewDashboardFlowItem(
    string CardId,
    int Span,
    double MinimumWidth,
    Func<double, double> HeightAt);

/// <summary>
/// Flows dashboard cards through a 12-column grid in their saved order. Each card
/// takes the highest free slot that fits its width (then the leftmost), so the
/// grid stays compact at every window width. Cards whose content would not fit
/// their span are widened, slivers no card could use are given to the card
/// beside them, and heights follow content.
/// </summary>
public static class OverviewDashboardLayoutEngine
{
    public const int Columns = 12;
    private const double Tolerance = .5;
    private const double RowTolerance = 24;
    private static double Gap => OverviewDashboardLayoutPolicy.CardGap;

    /// <summary>Horizontal distance from one column start to the next.</summary>
    public static double ColumnPitch(double availableWidth, double? lockedSurfaceWidth = null)
    {
        var sizingWidth = lockedSurfaceWidth is { } locked && double.IsFinite(locked) && locked > 0
            ? locked
            : EffectiveWidth(availableWidth);
        return (sizingWidth + Gap) / Columns;
    }

    public static double SpanWidth(int span, double pitch)
        => Math.Clamp(span, 1, Columns) * pitch - Gap;

    public static int SpanForWidth(double pixelWidth, double pitch)
        => Math.Clamp((int)Math.Round((pixelWidth + Gap) / pitch, MidpointRounding.AwayFromZero), 1, Columns);

    /// <summary>The smallest span whose width holds <paramref name="minimumWidth"/>.</summary>
    public static int MinimumSpan(double minimumWidth, double pitch)
        => Math.Clamp((int)Math.Ceiling((minimumWidth + Gap) / pitch - .001), 1, Columns);

    public static IReadOnlyList<OverviewDashboardPlacement> Flow(
        IReadOnlyList<OverviewDashboardFlowItem> items,
        double availableWidth,
        double? lockedSurfaceWidth = null)
    {
        ArgumentNullException.ThrowIfNull(items);
        var surface = EffectiveWidth(availableWidth);
        var locked = lockedSurfaceWidth is { } value && double.IsFinite(value) && value > 0;
        var pitch = ColumnPitch(surface, lockedSurfaceWidth);
        var placed = new List<OverviewDashboardPlacement>(items.Count);
        foreach (var item in items)
        {
            var span = Math.Clamp(item.Span, 1, Columns);
            var minimum = Math.Min(surface, Math.Max(0, item.MinimumWidth));
            if (!locked)
                span = Math.Max(span, MinimumSpan(minimum, pitch));
            var width = Math.Min(surface, Math.Max(SpanWidth(span, pitch), minimum));
            var height = CardHeight(item, width);
            var (left, top) = BestSlot(placed, width, surface);
            placed.Add(new OverviewDashboardPlacement(item.CardId, left, top, width, height));
        }

        if (!locked)
            AbsorbSlivers(placed, items, surface);
        return placed;
    }

    public static double SurfaceHeight(IReadOnlyList<OverviewDashboardPlacement> placements)
        => placements.Select(item => item.Top + item.Height + Gap)
            .DefaultIfEmpty(0)
            .Max();

    public static double ToHorizontalUnits(double pixels, double availableWidth)
        => pixels / EffectiveWidth(availableWidth) * OverviewDashboardLayoutPolicy.HorizontalUnits;

    /// <summary>
    /// Persistable bounds for a rendered placement: column position and span in
    /// grid units, top and height in pixels.
    /// </summary>
    public static OverviewDashboardCardBounds ToBounds(
        OverviewDashboardPlacement placement,
        double availableWidth,
        double? lockedSurfaceWidth = null)
    {
        var pitch = ColumnPitch(availableWidth, lockedSurfaceWidth);
        var renderPitch = ColumnPitch(availableWidth);
        return OverviewDashboardLayoutPolicy.ConstrainBounds(new OverviewDashboardCardBounds(
            Math.Round(placement.Left / renderPitch, MidpointRounding.AwayFromZero),
            Math.Round(placement.Top),
            SpanForWidth(placement.Width, pitch),
            Math.Round(placement.Height)));
    }

    private static (double Left, double Top) BestSlot(
        IReadOnlyList<OverviewDashboardPlacement> placed,
        double width,
        double surface)
    {
        var lefts = new SortedSet<double> { 0 };
        foreach (var item in placed)
            lefts.Add(item.Left + item.Width + Gap);
        var slots = new List<(double Left, double Top)>();
        foreach (var left in lefts)
        {
            if (left + width > surface + Tolerance) break;
            var top = placed
                .Where(item => item.Left < left + width - Tolerance && item.Left + item.Width > left + Tolerance)
                .Select(item => item.Top + item.Height + Gap)
                .DefaultIfEmpty(0)
                .Max();
            slots.Add((left, top));
        }
        if (slots.Count == 0)
            return (0, SurfaceHeight(placed));
        // Slots at nearly the same height read as one row, so take the leftmost.
        var highest = slots.Min(slot => slot.Top);
        return slots.Where(slot => slot.Top <= highest + RowTolerance).MinBy(slot => slot.Left);
    }

    /// <summary>
    /// Gives a card the empty space to its right when that space is narrower
    /// than any card can be, so rows end at the window edge instead of a dead strip.
    /// </summary>
    private static void AbsorbSlivers(
        List<OverviewDashboardPlacement> placed,
        IReadOnlyList<OverviewDashboardFlowItem> items,
        double surface)
    {
        if (placed.Count == 0) return;
        // A gap narrower than any card's minimum width can never hold a card.
        var narrowest = items.Min(item => Math.Max(item.MinimumWidth, 1));
        var byId = items.ToDictionary(item => item.CardId, StringComparer.OrdinalIgnoreCase);
        foreach (var index in Enumerable.Range(0, placed.Count).OrderByDescending(index => placed[index].Left))
        {
            var card = placed[index];
            var right = card.Left + card.Width;
            var limit = placed
                .Where(other => !ReferenceEquals(other, card)
                                && other.Left >= right - Tolerance
                                && other.Top < card.Top + card.Height + Gap - Tolerance
                                && other.Top + other.Height + Gap > card.Top + Tolerance)
                .Select(other => other.Left - Gap)
                .DefaultIfEmpty(surface)
                .Min();
            var spare = limit - right;
            if (spare <= Tolerance || spare - Gap >= narrowest - Tolerance) continue;
            var width = card.Width + spare;
            var height = byId.TryGetValue(card.CardId, out var item) ? CardHeight(item, width) : card.Height;
            placed[index] = card with { Width = width, Height = Math.Min(card.Height, height) };
        }
    }

    private static double CardHeight(OverviewDashboardFlowItem item, double width)
    {
        var height = item.HeightAt(width);
        return Math.Clamp(double.IsFinite(height) ? Math.Ceiling(height) : OverviewDashboardLayoutPolicy.MinimumCardHeight,
            OverviewDashboardLayoutPolicy.MinimumCardHeight,
            OverviewDashboardLayoutPolicy.MaximumCardHeight);
    }

    private static double EffectiveWidth(double availableWidth)
        => double.IsFinite(availableWidth) && availableWidth > 1 ? availableWidth : 1000;
}
