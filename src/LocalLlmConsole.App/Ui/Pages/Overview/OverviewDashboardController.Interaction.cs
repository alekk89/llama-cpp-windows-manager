using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    private void BeginPointerInteraction(OverviewDashboardCardView view, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        if (VisualTreeTraversal.FindAncestor<WpfButton>(args.OriginalSource as DependencyObject) is not null) return;
        if (ReferenceEquals(_reorderView, view)) return;
        var edge = view.ResizeEdgeAt(args.GetPosition(view.Root));
        if (edge == 0)
            BeginDrag(view, args);
        else
            BeginResize(view, edge, args);
    }

    private void TrackPointerInteraction(OverviewDashboardCardView view, System.Windows.Input.MouseEventArgs args)
    {
        if (ReferenceEquals(_resizeView, view))
        {
            TrackResize(args);
            return;
        }
        if (ReferenceEquals(_dragView, view))
        {
            TrackDrag(args);
            return;
        }
        if (ReferenceEquals(_reorderView, view)) return;
        view.UpdatePointer(args.GetPosition(view.Root));
    }

    private async Task EndPointerInteractionAsync(OverviewDashboardCardView view, MouseButtonEventArgs args)
    {
        if (ReferenceEquals(_resizeView, view))
            await EndResizeAsync(args);
        else if (ReferenceEquals(_dragView, view))
            await EndDragAsync(view, args);
    }

    private void ResetPointerWhenIdle(OverviewDashboardCardView view)
    {
        if (!ReferenceEquals(_resizeView, view) && !ReferenceEquals(_dragView, view)
            && !ReferenceEquals(_reorderView, view))
            view.ResetPointer();
    }

    private void ApplyPlacement(double availableWidth)
    {
        var visibleCards = _layout.Cards.Where(card =>
            _cardViews[card.Id].Root.Visibility == Visibility.Visible).ToArray();
        var desiredPlacements = OverviewDashboardLayoutEngine.Place(
            visibleCards,
            availableWidth,
            _layout.CardSizesLocked ? _layout.LockedSurfaceWidth : null);
        var minimumSizes = new Dictionary<string, OverviewDashboardMinimumSize>(StringComparer.OrdinalIgnoreCase);
        foreach (var placement in desiredPlacements)
        {
            var view = _cardViews[placement.CardId];
            view.UpdateMinimumSize(placement.Width);
            minimumSizes[placement.CardId] = new OverviewDashboardMinimumSize(
                _layout.CardSizesLocked ? Math.Max(view.MinimumWidth, placement.Width) : view.MinimumWidth,
                _layout.CardSizesLocked ? Math.Max(view.MinimumHeight, placement.Height) : view.MinimumHeight);
        }
        var placements = OverviewDashboardLayoutEngine.ResolveVisiblePlacements(
            desiredPlacements,
            minimumSizes,
            availableWidth);
        var surfaceHeight = 0d;
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            var view = _cardViews[placement.CardId];
            var actualHeight = ApplyCardPlacement(view, placement, availableWidth);
            surfaceHeight = Math.Max(surfaceHeight,
                Canvas.GetTop(view.Root) + actualHeight + OverviewDashboardLayoutPolicy.CardGap);
            WpfPanel.SetZIndex(view.Root, index);
        }
        if (!SameLength(_surface.Height, surfaceHeight))
            _surface.Height = surfaceHeight;
        if (!SameLength(_dashboard.Height, surfaceHeight))
            _dashboard.Height = surfaceHeight;
        _lastPlacementWidth = availableWidth;
        _placementDirty = false;
    }

    private void BeginDrag(OverviewDashboardCardView view, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        _dragView = view;
        _dragOrigin = args.GetPosition(_surface);
        _dragStartBounds = RenderedBounds(view, _dashboard.ActualWidth);
        _interactionBounds = _dragStartBounds;
        view.Root.Opacity = .82;
        view.SetInteractionPreview(true);
        WpfPanel.SetZIndex(view.Root, 1000);
        view.DragSurface.CaptureMouse();
        args.Handled = true;
    }

    private void TrackDrag(System.Windows.Input.MouseEventArgs args)
    {
        if (_dragView is null || _dragStartBounds is null || args.LeftButton != MouseButtonState.Pressed) return;
        var current = args.GetPosition(_surface);
        var delta = current - _dragOrigin;
        var candidate = OverviewDashboardLayoutPolicy.ConstrainBounds(_dragStartBounds with
        {
            X = SnapHorizontal(_dragStartBounds.X
                + OverviewDashboardLayoutEngine.ToHorizontalUnits(delta.X, _dashboard.ActualWidth)),
            Y = SnapVertical(_dragStartBounds.Y + delta.Y)
        });
        candidate = VisibleBounds(_dragView, candidate, _dashboard.ActualWidth);
        _interactionBounds = OverviewDashboardLayoutEngine.SnapMove(
            candidate,
            InteractionObstacles(_dragView.Layout.Id, _dashboard.ActualWidth),
            _dashboard.ActualWidth);
        ApplyCardBounds(_dragView, _interactionBounds, _dashboard.ActualWidth);
        args.Handled = true;
    }

    private async Task EndDragAsync(OverviewDashboardCardView view, MouseButtonEventArgs args)
    {
        if (_dragView is null) return;
        view.DragSurface.ReleaseMouseCapture();
        view.Root.Opacity = 1;
        view.SetInteractionPreview(false);
        view.ResetPointer();
        var bounds = _interactionBounds;
        _dragView = null;
        _dragStartBounds = null;
        _interactionBounds = null;
        var persistedBounds = bounds is null ? null : PersistedBounds(bounds, _dashboard.ActualWidth);
        if (persistedBounds is not null && persistedBounds != view.Layout.Bounds)
            await MutateAsync(layout => OverviewDashboardLayoutPolicy.SetCardBounds(layout, view.Layout.Id, persistedBounds));
        else
            ApplyPlacement(_dashboard.ActualWidth);
        args.Handled = true;
    }

    private void BeginResize(
        OverviewDashboardCardView view,
        OverviewDashboardResizeEdge edge,
        MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        _resizeView = view;
        _resizeEdge = edge;
        _resizeOrigin = args.GetPosition(_surface);
        _resizeStartBounds = RenderedBounds(view, _dashboard.ActualWidth);
        _interactionBounds = _resizeStartBounds;
        view.SetInteractionPreview(true);
        WpfPanel.SetZIndex(view.Root, 1000);
        view.Root.CaptureMouse();
        args.Handled = true;
    }

    private void TrackResize(System.Windows.Input.MouseEventArgs args)
    {
        if (_resizeView is null || _resizeStartBounds is null || args.LeftButton != MouseButtonState.Pressed) return;
        var current = args.GetPosition(_surface);
        var delta = current - _resizeOrigin;
        var horizontalDelta = OverviewDashboardLayoutEngine.ToHorizontalUnits(delta.X, _dashboard.ActualWidth);
        var provisional = ResizeBounds(
            _resizeStartBounds,
            _resizeEdge,
            horizontalDelta,
            delta.Y,
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutPolicy.MinimumCardHeight);
        var provisionalPlacement = OverviewDashboardLayoutEngine.Place(
            [_resizeView.Layout with { Bounds = provisional }],
            _dashboard.ActualWidth)[0];
        _resizeView.UpdateMinimumSize(provisionalPlacement.Width);
        var minimumWidthUnits = Math.Max(
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutEngine.ToHorizontalUnits(
                _resizeView.MinimumWidth + OverviewDashboardLayoutPolicy.CardGap,
                _dashboard.ActualWidth));
        var resized = ResizeBounds(
            _resizeStartBounds,
            _resizeEdge,
            horizontalDelta,
            delta.Y,
            minimumWidthUnits,
            _resizeView.MinimumHeight);
        _interactionBounds = OverviewDashboardLayoutEngine.SnapResize(
            _resizeStartBounds,
            resized,
            _resizeEdge,
            InteractionObstacles(_resizeView.Layout.Id, _dashboard.ActualWidth),
            _dashboard.ActualWidth,
            minimumWidthUnits,
            _resizeView.MinimumHeight);
        ApplyCardBounds(_resizeView, _interactionBounds, _dashboard.ActualWidth);
        args.Handled = true;
    }

    private async Task EndResizeAsync(MouseButtonEventArgs args)
    {
        if (_resizeView is null) return;
        var view = _resizeView;
        view.Root.ReleaseMouseCapture();
        view.SetInteractionPreview(false);
        view.ResetPointer();
        var bounds = _interactionBounds;
        _resizeView = null;
        _resizeStartBounds = null;
        _interactionBounds = null;
        if (bounds is not null && bounds != view.Layout.Bounds)
            await MutateAsync(layout => OverviewDashboardLayoutPolicy.SetCardBounds(layout, view.Layout.Id, bounds));
        else
            ApplyPlacement(_dashboard.ActualWidth);
        args.Handled = true;
    }

    private static OverviewDashboardCardBounds ResizeBounds(
        OverviewDashboardCardBounds start,
        OverviewDashboardResizeEdge edge,
        double deltaX,
        double deltaY,
        double minimumWidth,
        double minimumHeight)
    {
        minimumWidth = Math.Clamp(minimumWidth,
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutPolicy.HorizontalUnits);
        minimumHeight = Math.Clamp(minimumHeight,
            OverviewDashboardLayoutPolicy.MinimumCardHeight,
            OverviewDashboardLayoutPolicy.MaximumCardHeight);
        var left = start.X;
        var right = start.X + start.Width;
        var top = start.Y;
        var bottom = start.Y + start.Height;
        if (edge.HasFlag(OverviewDashboardResizeEdge.Left) && right < minimumWidth)
            right = minimumWidth;
        if (edge.HasFlag(OverviewDashboardResizeEdge.Right) && left + minimumWidth > OverviewDashboardLayoutPolicy.HorizontalUnits)
            left = OverviewDashboardLayoutPolicy.HorizontalUnits - minimumWidth;
        if (edge.HasFlag(OverviewDashboardResizeEdge.Top) && bottom < minimumHeight)
            bottom = minimumHeight;
        if (edge.HasFlag(OverviewDashboardResizeEdge.Left))
            left = Math.Clamp(SnapHorizontal(start.X + deltaX), 0, right - minimumWidth);
        if (edge.HasFlag(OverviewDashboardResizeEdge.Right))
            right = Math.Clamp(SnapHorizontal(start.X + start.Width + deltaX),
                left + minimumWidth,
                OverviewDashboardLayoutPolicy.HorizontalUnits);
        if (edge.HasFlag(OverviewDashboardResizeEdge.Top))
            top = Math.Clamp(SnapVertical(start.Y + deltaY), 0, bottom - minimumHeight);
        if (edge.HasFlag(OverviewDashboardResizeEdge.Bottom))
            bottom = Math.Clamp(SnapVertical(start.Y + start.Height + deltaY),
                top + minimumHeight,
                OverviewDashboardLayoutPolicy.MaximumCardY + OverviewDashboardLayoutPolicy.MaximumCardHeight);
        return OverviewDashboardLayoutPolicy.ConstrainBounds(new(left, top, right - left, bottom - top));
    }

    private static double SnapHorizontal(double value) => Math.Round(value * 8) / 8;
    private static double SnapVertical(double value) => Math.Round(value / 2) * 2;

    private static double ApplyCardPlacement(
        OverviewDashboardCardView view,
        OverviewDashboardPlacement placement,
        double availableWidth)
    {
        var width = Math.Max(placement.Width, view.MinimumWidth);
        view.UpdateMinimumSize(width);
        var height = Math.Max(placement.Height, view.MinimumHeight);
        var left = Math.Clamp(placement.Left, 0, Math.Max(0, availableWidth - width));
        if (!SameLength(view.Root.Width, width)) view.Root.Width = width;
        if (!SameLength(view.Root.Height, height)) view.Root.Height = height;
        if (!SameLength(Canvas.GetLeft(view.Root), left)) Canvas.SetLeft(view.Root, left);
        if (!SameLength(Canvas.GetTop(view.Root), placement.Top)) Canvas.SetTop(view.Root, placement.Top);
        return height;
    }

    private static void ApplyCardBounds(
        OverviewDashboardCardView view,
        OverviewDashboardCardBounds bounds,
        double availableWidth)
    {
        var placement = OverviewDashboardLayoutEngine.Place(
            [view.Layout with { Bounds = bounds }],
            availableWidth)[0];
        ApplyCardPlacement(view, placement, availableWidth);
    }

    private IReadOnlyList<OverviewDashboardCardBounds> InteractionObstacles(string cardId, double availableWidth)
        => _layout.Cards
            .Where(card => !string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase))
            .Where(card => !_cardViews.TryGetValue(card.Id, out var view)
                           || view.Root.Visibility == Visibility.Visible)
            .Select(card => _cardViews.TryGetValue(card.Id, out var view)
                ? RenderedBounds(view, availableWidth)
                : card.Bounds!)
            .ToArray();

    private static OverviewDashboardCardBounds RenderedBounds(
        OverviewDashboardCardView view,
        double availableWidth)
    {
        var left = Canvas.GetLeft(view.Root);
        var reachesRightEdge = left + view.Root.Width >= availableWidth - .5;
        return OverviewDashboardLayoutPolicy.ConstrainBounds(new OverviewDashboardCardBounds(
            OverviewDashboardLayoutEngine.ToHorizontalUnits(left, availableWidth),
            Canvas.GetTop(view.Root),
            OverviewDashboardLayoutEngine.ToHorizontalUnits(
                view.Root.Width + (reachesRightEdge ? 0 : OverviewDashboardLayoutPolicy.CardGap),
                availableWidth),
            view.Root.Height));
    }

    private OverviewDashboardCardBounds PersistedBounds(
        OverviewDashboardCardBounds renderedBounds,
        double availableWidth)
    {
        if (!_layout.CardSizesLocked || _layout.LockedSurfaceWidth < 1)
            return renderedBounds;
        var renderedPixelSpan = renderedBounds.Width / OverviewDashboardLayoutPolicy.HorizontalUnits * availableWidth;
        var lockedWidth = OverviewDashboardLayoutEngine.ToHorizontalUnits(
            renderedPixelSpan,
            _layout.LockedSurfaceWidth);
        return OverviewDashboardLayoutPolicy.ConstrainBounds(renderedBounds with
        {
            Width = lockedWidth,
            X = Math.Min(renderedBounds.X, OverviewDashboardLayoutPolicy.HorizontalUnits - lockedWidth)
        });
    }

    private static OverviewDashboardCardBounds VisibleBounds(
        OverviewDashboardCardView view,
        OverviewDashboardCardBounds bounds,
        double availableWidth)
    {
        var minimumWidth = OverviewDashboardLayoutEngine.ToHorizontalUnits(
            view.MinimumWidth + OverviewDashboardLayoutPolicy.CardGap,
            availableWidth);
        return OverviewDashboardLayoutPolicy.ConstrainBounds(bounds with
        {
            Width = Math.Max(bounds.Width, minimumWidth),
            Height = Math.Max(bounds.Height, view.MinimumHeight)
        });
    }
}
