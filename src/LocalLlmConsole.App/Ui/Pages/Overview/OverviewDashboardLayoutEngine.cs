namespace LocalLlmConsole;

public sealed record OverviewDashboardPlacement(
    string CardId,
    double Left,
    double Top,
    double Width,
    double Height);

public sealed record OverviewDashboardMinimumSize(double Width, double Height);

/// <summary>
/// Translates persisted responsive bounds into device-independent pixels.
/// User positions remain stable because this layer never repacks cards.
/// </summary>
public static partial class OverviewDashboardLayoutEngine
{
    public const double SnapDistance = 14;

    public static IReadOnlyList<OverviewDashboardPlacement> Place(
        IReadOnlyList<OverviewDashboardCardLayout> cards,
        double availableWidth,
        double? lockedSurfaceWidth = null)
    {
        ArgumentNullException.ThrowIfNull(cards);
        var width = EffectiveWidth(availableWidth);
        var positionPixelsPerUnit = width / OverviewDashboardLayoutPolicy.HorizontalUnits;
        var sizePixelsPerUnit = lockedSurfaceWidth is { } lockedWidth
                                && double.IsFinite(lockedWidth)
                                && lockedWidth > 0
            ? lockedWidth / OverviewDashboardLayoutPolicy.HorizontalUnits
            : positionPixelsPerUnit;
        return cards.Select(card =>
        {
            var bounds = OverviewDashboardLayoutPolicy.ConstrainBounds(card.Bounds
                ?? new OverviewDashboardCardBounds(
                    0,
                    0,
                    Math.Clamp(card.ColumnSpan, 1, 3) * 4,
                    OverviewDashboardLayoutPolicy.CardHeight(card.Height)));
            return new OverviewDashboardPlacement(
                card.Id,
                bounds.X * positionPixelsPerUnit,
                bounds.Y,
                Math.Max(80, bounds.Width * sizePixelsPerUnit -
                    (bounds.X + bounds.Width < OverviewDashboardLayoutPolicy.HorizontalUnits
                        ? OverviewDashboardLayoutPolicy.CardGap
                        : 0)),
                bounds.Height);
        }).ToArray();
    }

    public static double SurfaceHeight(IReadOnlyList<OverviewDashboardPlacement> placements)
        => placements.Select(item => item.Top + item.Height + OverviewDashboardLayoutPolicy.CardGap)
            .DefaultIfEmpty(0)
            .Max();

    public static IReadOnlyList<OverviewDashboardPlacement> ResolveVisiblePlacements(
        IReadOnlyList<OverviewDashboardPlacement> desiredPlacements,
        IReadOnlyDictionary<string, OverviewDashboardMinimumSize> minimumSizes,
        double availableWidth)
    {
        ArgumentNullException.ThrowIfNull(desiredPlacements);
        ArgumentNullException.ThrowIfNull(minimumSizes);
        var surfaceWidth = EffectiveWidth(availableWidth);
        var responsivePlacements = CompressVisibleRows(desiredPlacements, minimumSizes, surfaceWidth);
        var resolved = new List<OverviewDashboardPlacement>(responsivePlacements.Count);
        foreach (var desired in responsivePlacements)
        {
            minimumSizes.TryGetValue(desired.CardId, out var minimum);
            var width = Math.Min(surfaceWidth, Math.Max(desired.Width, minimum?.Width ?? 0));
            var height = Math.Max(desired.Height, minimum?.Height ?? 0);
            var desiredLeft = Math.Clamp(desired.Left, 0, Math.Max(0, surfaceWidth - width));
            var desiredTop = Math.Max(0, desired.Top);
            var xCandidates = new HashSet<double> { desiredLeft, 0, Math.Max(0, surfaceWidth - width) };
            var yCandidates = new HashSet<double> { desiredTop, 0 };
            foreach (var placed in resolved)
            {
                xCandidates.Add(Math.Clamp(
                    placed.Left + placed.Width + OverviewDashboardLayoutPolicy.CardGap,
                    0,
                    Math.Max(0, surfaceWidth - width)));
                xCandidates.Add(Math.Clamp(
                    placed.Left - OverviewDashboardLayoutPolicy.CardGap - width,
                    0,
                    Math.Max(0, surfaceWidth - width)));
                yCandidates.Add(Math.Max(0,
                    placed.Top + placed.Height + OverviewDashboardLayoutPolicy.CardGap));
                yCandidates.Add(Math.Max(0,
                    placed.Top - OverviewDashboardLayoutPolicy.CardGap - height));
            }

            var placement = xCandidates
                .SelectMany(left => yCandidates.Select(top => new OverviewDashboardPlacement(
                    desired.CardId, left, top, width, height)))
                .Where(candidate => resolved.All(placed => !VisiblePlacementsConflict(candidate, placed)))
                .OrderBy(candidate => VisiblePlacementDistanceSquared(candidate, desiredLeft, desiredTop))
                .ThenBy(candidate => candidate.Top)
                .ThenBy(candidate => candidate.Left)
                .FirstOrDefault();
            if (placement is null)
            {
                placement = new OverviewDashboardPlacement(
                    desired.CardId,
                    desiredLeft,
                    resolved.Select(item => item.Top + item.Height + OverviewDashboardLayoutPolicy.CardGap)
                        .DefaultIfEmpty(desiredTop)
                        .Max(),
                    width,
                    height);
            }
            resolved.Add(placement);
        }
        return resolved;
    }

    public static double ToHorizontalUnits(double pixels, double availableWidth)
        => pixels / EffectiveWidth(availableWidth) * OverviewDashboardLayoutPolicy.HorizontalUnits;

    public static OverviewDashboardCardBounds SnapMove(
        OverviewDashboardCardBounds candidate,
        IReadOnlyList<OverviewDashboardCardBounds> obstacles,
        double availableWidth)
    {
        var snapX = ToHorizontalUnits(SnapDistance, availableWidth);
        var bestX = (Distance: double.PositiveInfinity, Value: candidate.X);
        var bestY = (Distance: double.PositiveInfinity, Value: candidate.Y);
        foreach (var obstacle in obstacles)
        {
            if (OverlapsVertically(candidate, obstacle, SnapDistance))
            {
                Consider(candidate.X, obstacle.X + obstacle.Width, ref bestX, snapX);
                Consider(candidate.X, obstacle.X - candidate.Width, ref bestX, snapX);
            }
            if (OverlapsHorizontally(candidate, obstacle, snapX))
            {
                Consider(candidate.Y, obstacle.Y + obstacle.Height + OverviewDashboardLayoutPolicy.CardGap,
                    ref bestY, SnapDistance);
                Consider(candidate.Y, obstacle.Y - OverviewDashboardLayoutPolicy.CardGap - candidate.Height,
                    ref bestY, SnapDistance);
            }
        }

        candidate = OverviewDashboardLayoutPolicy.ConstrainBounds(candidate with
        {
            X = bestX.Value,
            Y = bestY.Value
        });
        return Separate(candidate, obstacles);
    }

    public static OverviewDashboardCardBounds SnapResize(
        OverviewDashboardCardBounds start,
        OverviewDashboardCardBounds candidate,
        OverviewDashboardResizeEdge edge,
        IReadOnlyList<OverviewDashboardCardBounds> obstacles,
        double availableWidth,
        double minimumWidth,
        double minimumHeight)
    {
        var snapX = ToHorizontalUnits(SnapDistance, availableWidth);
        var left = candidate.X;
        var right = candidate.X + candidate.Width;
        var top = candidate.Y;
        var bottom = candidate.Y + candidate.Height;
        foreach (var obstacle in obstacles)
        {
            if (OverlapsVertically(candidate, obstacle, SnapDistance))
            {
                if (edge.HasFlag(OverviewDashboardResizeEdge.Left)
                    && Math.Abs(left - obstacle.X - obstacle.Width) <= snapX)
                    left = obstacle.X + obstacle.Width;
                if (edge.HasFlag(OverviewDashboardResizeEdge.Right)
                    && Math.Abs(right - obstacle.X) <= snapX)
                    right = obstacle.X;
            }
        }

        var horizontallyAdjusted = candidate with { X = left, Width = right - left };
        var bestTop = (Distance: double.PositiveInfinity, Value: top);
        var bestBottom = (Distance: double.PositiveInfinity, Value: bottom);
        var adjacencyTolerance = ToHorizontalUnits(2, availableWidth);
        foreach (var obstacle in obstacles)
        {
            if (OverlapsHorizontally(horizontallyAdjusted, obstacle, snapX))
            {
                if (edge.HasFlag(OverviewDashboardResizeEdge.Top))
                    Consider(top, obstacle.Y + obstacle.Height + OverviewDashboardLayoutPolicy.CardGap,
                        ref bestTop, SnapDistance);
                if (edge.HasFlag(OverviewDashboardResizeEdge.Bottom))
                    Consider(bottom, obstacle.Y - OverviewDashboardLayoutPolicy.CardGap,
                        ref bestBottom, SnapDistance);
            }
            if (HorizontallyAdjacent(horizontallyAdjusted, obstacle, adjacencyTolerance))
            {
                if (edge.HasFlag(OverviewDashboardResizeEdge.Top))
                    Consider(top, obstacle.Y, ref bestTop, SnapDistance);
                if (edge.HasFlag(OverviewDashboardResizeEdge.Bottom))
                    Consider(bottom, obstacle.Y + obstacle.Height, ref bestBottom, SnapDistance);
            }
        }
        top = bestTop.Value;
        bottom = bestBottom.Value;

        candidate = ResizeFromEdges(start, edge, left, top, right, bottom, minimumWidth, minimumHeight);
        for (var pass = 0; pass < 3; pass++)
        {
            var changed = false;
            foreach (var obstacle in obstacles.Where(item => Conflicts(candidate, item)))
            {
                var alternatives = ResizeAlternatives(start, candidate, obstacle, edge, minimumWidth, minimumHeight)
                    .Where(item => !Conflicts(item, obstacle))
                    .OrderBy(item => DistanceSquared(candidate, item))
                    .ToArray();
                if (alternatives.Length == 0) continue;
                candidate = alternatives[0];
                changed = true;
            }
            if (!changed) break;
        }
        return OverviewDashboardLayoutPolicy.ConstrainBounds(candidate);
    }

    private static OverviewDashboardCardBounds Separate(
        OverviewDashboardCardBounds candidate,
        IReadOnlyList<OverviewDashboardCardBounds> obstacles)
    {
        for (var pass = 0; pass < 4; pass++)
        {
            var changed = false;
            foreach (var obstacle in obstacles.Where(item => Conflicts(candidate, item)))
            {
                var alternatives = new[]
                    {
                        candidate with { X = obstacle.X - candidate.Width },
                        candidate with { X = obstacle.X + obstacle.Width },
                        candidate with { Y = obstacle.Y - OverviewDashboardLayoutPolicy.CardGap - candidate.Height },
                        candidate with { Y = obstacle.Y + obstacle.Height + OverviewDashboardLayoutPolicy.CardGap }
                    }
                    .Select(OverviewDashboardLayoutPolicy.ConstrainBounds)
                    .Where(item => !Conflicts(item, obstacle))
                    .OrderBy(item => DistanceSquared(candidate, item))
                    .ToArray();
                if (alternatives.Length == 0) continue;
                candidate = alternatives[0];
                changed = true;
            }
            if (!changed) break;
        }
        return candidate;
    }

    private static IEnumerable<OverviewDashboardCardBounds> ResizeAlternatives(
        OverviewDashboardCardBounds start,
        OverviewDashboardCardBounds candidate,
        OverviewDashboardCardBounds obstacle,
        OverviewDashboardResizeEdge edge,
        double minimumWidth,
        double minimumHeight)
    {
        var startRight = start.X + start.Width;
        var startBottom = start.Y + start.Height;
        if (edge.HasFlag(OverviewDashboardResizeEdge.Right) && start.X <= obstacle.X)
            yield return ResizeFromEdges(start, edge, candidate.X, candidate.Y, obstacle.X,
                candidate.Y + candidate.Height, minimumWidth, minimumHeight);
        if (edge.HasFlag(OverviewDashboardResizeEdge.Left) && startRight >= obstacle.X + obstacle.Width)
            yield return ResizeFromEdges(start, edge, obstacle.X + obstacle.Width, candidate.Y,
                candidate.X + candidate.Width, candidate.Y + candidate.Height, minimumWidth, minimumHeight);
        if (edge.HasFlag(OverviewDashboardResizeEdge.Bottom) && start.Y <= obstacle.Y)
            yield return ResizeFromEdges(start, edge, candidate.X, candidate.Y,
                candidate.X + candidate.Width, obstacle.Y - OverviewDashboardLayoutPolicy.CardGap,
                minimumWidth, minimumHeight);
        if (edge.HasFlag(OverviewDashboardResizeEdge.Top) && startBottom >= obstacle.Y + obstacle.Height)
            yield return ResizeFromEdges(start, edge, candidate.X,
                obstacle.Y + obstacle.Height + OverviewDashboardLayoutPolicy.CardGap,
                candidate.X + candidate.Width, candidate.Y + candidate.Height,
                minimumWidth, minimumHeight);
    }

    private static OverviewDashboardCardBounds ResizeFromEdges(
        OverviewDashboardCardBounds start,
        OverviewDashboardResizeEdge edge,
        double left,
        double top,
        double right,
        double bottom,
        double minimumWidth,
        double minimumHeight)
    {
        if (!edge.HasFlag(OverviewDashboardResizeEdge.Left)) left = start.X;
        if (!edge.HasFlag(OverviewDashboardResizeEdge.Right)) right = start.X + start.Width;
        if (!edge.HasFlag(OverviewDashboardResizeEdge.Top)) top = start.Y;
        if (!edge.HasFlag(OverviewDashboardResizeEdge.Bottom)) bottom = start.Y + start.Height;
        if (right - left < minimumWidth)
        {
            if (edge.HasFlag(OverviewDashboardResizeEdge.Left)) left = right - minimumWidth;
            else right = left + minimumWidth;
        }
        if (bottom - top < minimumHeight)
        {
            if (edge.HasFlag(OverviewDashboardResizeEdge.Top)) top = bottom - minimumHeight;
            else bottom = top + minimumHeight;
        }
        return OverviewDashboardLayoutPolicy.ConstrainBounds(new(left, top, right - left, bottom - top));
    }

    private static bool Conflicts(OverviewDashboardCardBounds first, OverviewDashboardCardBounds second)
        => OverlapsHorizontally(first, second, 0)
           && first.Y < second.Y + second.Height + OverviewDashboardLayoutPolicy.CardGap
           && first.Y + first.Height + OverviewDashboardLayoutPolicy.CardGap > second.Y;

    private static bool OverlapsHorizontally(
        OverviewDashboardCardBounds first,
        OverviewDashboardCardBounds second,
        double tolerance)
        => first.X < second.X + second.Width + tolerance
           && first.X + first.Width + tolerance > second.X;

    private static bool OverlapsVertically(
        OverviewDashboardCardBounds first,
        OverviewDashboardCardBounds second,
        double tolerance)
        => first.Y < second.Y + second.Height + tolerance
           && first.Y + first.Height + tolerance > second.Y;

    private static bool HorizontallyAdjacent(
        OverviewDashboardCardBounds first,
        OverviewDashboardCardBounds second,
        double tolerance)
        => Math.Abs(first.X + first.Width - second.X) <= tolerance
           || Math.Abs(second.X + second.Width - first.X) <= tolerance;

    private static void Consider(
        double current,
        double target,
        ref (double Distance, double Value) best,
        double threshold)
    {
        var distance = Math.Abs(current - target);
        if (distance <= threshold && distance < best.Distance)
            best = (distance, target);
    }

    private static double DistanceSquared(OverviewDashboardCardBounds first, OverviewDashboardCardBounds second)
        => Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2);

    private static bool VisiblePlacementsConflict(
        OverviewDashboardPlacement first,
        OverviewDashboardPlacement second)
        => first.Left < second.Left + second.Width + OverviewDashboardLayoutPolicy.CardGap - .1
           && first.Left + first.Width + OverviewDashboardLayoutPolicy.CardGap > second.Left + .1
           && first.Top < second.Top + second.Height + OverviewDashboardLayoutPolicy.CardGap - .1
           && first.Top + first.Height + OverviewDashboardLayoutPolicy.CardGap > second.Top + .1;

    private static double VisiblePlacementDistanceSquared(
        OverviewDashboardPlacement placement,
        double desiredLeft,
        double desiredTop)
        => Math.Pow(placement.Left - desiredLeft, 2) + Math.Pow(placement.Top - desiredTop, 2);

    private static double EffectiveWidth(double availableWidth)
        => double.IsFinite(availableWidth) && availableWidth > 1 ? availableWidth : 1000;
}
