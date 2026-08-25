namespace LocalLlmConsole;

public static partial class OverviewDashboardLayoutEngine
{
    private static IReadOnlyList<OverviewDashboardPlacement> CompressVisibleRows(
        IReadOnlyList<OverviewDashboardPlacement> desiredPlacements,
        IReadOnlyDictionary<string, OverviewDashboardMinimumSize> minimumSizes,
        double surfaceWidth)
    {
        var placements = desiredPlacements.Select(placement =>
        {
            minimumSizes.TryGetValue(placement.CardId, out var minimum);
            return placement with
            {
                Width = Math.Min(surfaceWidth, Math.Max(placement.Width, minimum?.Width ?? 0)),
                Height = Math.Max(placement.Height, minimum?.Height ?? 0)
            };
        }).ToArray();
        var rows = new List<List<int>>();
        foreach (var index in Enumerable.Range(0, placements.Length)
                     .OrderBy(index => placements[index].Top)
            .ThenBy(index => placements[index].Left))
        {
            var row = rows.FirstOrDefault(candidate => candidate.Any(existing =>
                VerticallyOverlaps(desiredPlacements[existing], desiredPlacements[index])));
            if (row is null)
            {
                row = [];
                rows.Add(row);
            }
            row.Add(index);
        }

        foreach (var row in rows.Where(row => row.Count > 1))
            CompressVisibleRow(placements, row, minimumSizes, surfaceWidth);
        return placements;
    }

    private static void CompressVisibleRow(
        OverviewDashboardPlacement[] placements,
        IReadOnlyList<int> row,
        IReadOnlyDictionary<string, OverviewDashboardMinimumSize> minimumSizes,
        double surfaceWidth)
    {
        var ordered = row.OrderBy(index => placements[index].Left).ToArray();
        var gapWidth = OverviewDashboardLayoutPolicy.CardGap * (ordered.Length - 1);
        var availableCardWidth = Math.Max(0, surfaceWidth - gapWidth);
        var minimumWidths = ordered.Select(index =>
        {
            var placement = placements[index];
            return minimumSizes.TryGetValue(placement.CardId, out var minimum)
                ? Math.Min(surfaceWidth, Math.Max(0, minimum.Width))
                : placement.Width;
        }).ToArray();
        var currentWidth = ordered.Sum(index => placements[index].Width);
        var minimumWidth = minimumWidths.Sum();
        var targetWidth = Math.Max(minimumWidth, availableCardWidth);
        var rowOverflows = ordered.Any(index =>
            placements[index].Left < -.1
            || placements[index].Left + placements[index].Width > surfaceWidth + .1);
        var rowConflicts = Enumerable.Range(1, ordered.Length - 1).Any(position =>
            placements[ordered[position]].Left
            < placements[ordered[position - 1]].Left
              + placements[ordered[position - 1]].Width
              + OverviewDashboardLayoutPolicy.CardGap - .1);
        if (!rowOverflows && !rowConflicts && currentWidth + gapWidth <= surfaceWidth + .1) return;

        if (currentWidth > targetWidth + .1)
        {
            var shrinkCapacity = currentWidth - minimumWidth;
            var shrink = currentWidth - targetWidth;
            for (var position = 0; position < ordered.Length; position++)
            {
                var index = ordered[position];
                var capacity = placements[index].Width - minimumWidths[position];
                var width = placements[index].Width - shrink * capacity / shrinkCapacity;
                placements[index] = placements[index] with { Width = width };
            }
        }

        if (ordered.Sum(index => placements[index].Width) + gapWidth > surfaceWidth + .1)
        {
            PackWrappedRow(placements, ordered, surfaceWidth);
            return;
        }
        var left = 0d;
        foreach (var index in ordered)
        {
            placements[index] = placements[index] with { Left = left };
            left += placements[index].Width + OverviewDashboardLayoutPolicy.CardGap;
        }
    }

    private static void PackWrappedRow(
        OverviewDashboardPlacement[] placements,
        IReadOnlyList<int> ordered,
        double surfaceWidth)
    {
        var packedRows = new List<List<int>>();
        foreach (var index in ordered)
        {
            var target = packedRows.FirstOrDefault(row => PackedRowWidth(placements, row)
                + (row.Count == 0 ? 0 : OverviewDashboardLayoutPolicy.CardGap)
                + placements[index].Width <= surfaceWidth + .1);
            if (target is null)
            {
                target = [];
                packedRows.Add(target);
            }
            target.Add(index);
        }

        var top = ordered.Min(index => placements[index].Top);
        foreach (var row in packedRows)
        {
            var left = 0d;
            var rowHeight = row.Max(index => placements[index].Height);
            foreach (var index in row)
            {
                placements[index] = placements[index] with { Left = left, Top = top };
                left += placements[index].Width + OverviewDashboardLayoutPolicy.CardGap;
            }
            top += rowHeight + OverviewDashboardLayoutPolicy.CardGap;
        }
    }

    private static double PackedRowWidth(
        IReadOnlyList<OverviewDashboardPlacement> placements,
        IReadOnlyList<int> row)
        => row.Sum(index => placements[index].Width)
           + OverviewDashboardLayoutPolicy.CardGap * Math.Max(0, row.Count - 1);

    private static bool VerticallyOverlaps(
        OverviewDashboardPlacement first,
        OverviewDashboardPlacement second)
        => first.Top < second.Top + second.Height - .1
           && first.Top + first.Height > second.Top + .1;
}
