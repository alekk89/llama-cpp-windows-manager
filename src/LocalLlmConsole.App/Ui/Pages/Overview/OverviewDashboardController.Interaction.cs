using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using WpfButton = System.Windows.Controls.Button;
using WpfPanel = System.Windows.Controls.Panel;

namespace LocalLlmConsole;

/// <summary>
/// Pointer and placement handling for the flow dashboard. Cards keep a saved
/// order and column span; dragging a card picks the slot nearest the pointer
/// while the other cards reflow around a placeholder, and resizing snaps the
/// card's width to grid columns.
/// </summary>
public sealed partial class OverviewDashboardController
{
    private const double DragStartDistance = 4;
    private static readonly Duration MoveAnimationDuration = new(TimeSpan.FromMilliseconds(170));
    private readonly Dictionary<string, OverviewDashboardPlacement> _placements = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<string, System.Windows.Point>? _animateFrom;
    private System.Windows.Shapes.Rectangle? _dropPlaceholder;
    private Vector _dragPointerOffset;
    private bool _dragMoved;
    private int _dragTargetIndex = -1;
    private double _resizeStartWidth;
    private int _resizeStartSpan;
    private int _resizeSpan;

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
        var placements = FlowPlacements(VisibleCards(), availableWidth);
        var animateFrom = _animateFrom;
        _animateFrom = null;
        ApplyPlacements(placements, availableWidth, animate: animateFrom is not null, animateFrom);
        _lastPlacementWidth = availableWidth;
        _placementDirty = false;
    }

    private IReadOnlyList<OverviewDashboardCardLayout> VisibleCards()
        => _layout.Cards.Where(card => _cardViews.TryGetValue(card.Id, out var view)
                                       && view.Root.Visibility == Visibility.Visible).ToArray();

    private IReadOnlyList<OverviewDashboardPlacement> FlowPlacements(
        IEnumerable<OverviewDashboardCardLayout> cards,
        double availableWidth,
        string? spanCardId = null,
        int span = 0)
    {
        var items = cards.Select(card =>
        {
            var view = _cardViews[card.Id];
            return new OverviewDashboardFlowItem(
                card.Id,
                string.Equals(card.Id, spanCardId, StringComparison.OrdinalIgnoreCase)
                    ? span
                    : OverviewDashboardLayoutPolicy.CardSpan(card),
                view.MinimumWidth,
                view.HeightAt);
        }).ToArray();
        return OverviewDashboardLayoutEngine.Flow(
            items,
            availableWidth,
            _layout.CardSizesLocked ? _layout.LockedSurfaceWidth : null);
    }

    private void ApplyPlacements(
        IReadOnlyList<OverviewDashboardPlacement> placements,
        double availableWidth,
        bool animate,
        IReadOnlyDictionary<string, System.Windows.Point>? animateFrom = null,
        string? floatingCardId = null)
    {
        _placements.Clear();
        var surfaceHeight = OverviewDashboardLayoutEngine.SurfaceHeight(placements);
        for (var index = 0; index < placements.Count; index++)
        {
            var placement = placements[index];
            _placements[placement.CardId] = placement;
            if (string.Equals(placement.CardId, floatingCardId, StringComparison.OrdinalIgnoreCase)) continue;
            var view = _cardViews[placement.CardId];
            var previous = animateFrom is not null && animateFrom.TryGetValue(placement.CardId, out var from)
                ? from
                : CurrentVisualPosition(view.Root);
            ApplyCardPlacement(view, placement, availableWidth);
            if (animate)
                AnimateFrom(view.Root, previous.X - placement.Left, previous.Y - placement.Top);
            WpfPanel.SetZIndex(view.Root, index);
        }
        if (_dragView is not null && _dragMoved)
            surfaceHeight = Math.Max(surfaceHeight,
                Canvas.GetTop(_dragView.Root) + _dragView.Root.Height + OverviewDashboardLayoutPolicy.CardGap);
        if (!SameLength(_surface.Height, surfaceHeight))
            _surface.Height = surfaceHeight;
        if (!SameLength(_dashboard.Height, surfaceHeight))
            _dashboard.Height = surfaceHeight;
    }

    private void BeginDrag(OverviewDashboardCardView view, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left) return;
        var pointer = args.GetPosition(_surface);
        _dragView = view;
        _dragOrigin = pointer;
        _dragPointerOffset = pointer - new System.Windows.Point(Canvas.GetLeft(view.Root), Canvas.GetTop(view.Root));
        _dragMoved = false;
        _dragTargetIndex = VisibleCards().ToList().FindIndex(card =>
            string.Equals(card.Id, view.Layout.Id, StringComparison.OrdinalIgnoreCase));
        view.DragSurface.CaptureMouse();
        args.Handled = true;
    }

    private void TrackDrag(System.Windows.Input.MouseEventArgs args)
    {
        if (_dragView is null || args.LeftButton != MouseButtonState.Pressed) return;
        var pointer = args.GetPosition(_surface);
        if (!_dragMoved)
        {
            if ((pointer - _dragOrigin).Length < DragStartDistance) return;
            _dragMoved = true;
            StopAnimation(_dragView.Root);
            _dragView.Root.Opacity = .85;
            _dragView.SetInteractionPreview(true);
            WpfPanel.SetZIndex(_dragView.Root, 1000);
            ShowDropPlaceholder();
        }

        var width = _dashboard.ActualWidth;
        var left = Math.Clamp(pointer.X - _dragPointerOffset.X, 0, Math.Max(0, width - _dragView.Root.Width));
        var top = Math.Max(0, pointer.Y - _dragPointerOffset.Y);
        Canvas.SetLeft(_dragView.Root, left);
        Canvas.SetTop(_dragView.Root, top);

        var target = NearestDropIndex(_dragView.Layout.Id, new System.Windows.Point(left, top), width,
            out var placements);
        if (target != _dragTargetIndex || _dropPlaceholder!.Visibility != Visibility.Visible)
        {
            _dragTargetIndex = target;
            ApplyPlacements(placements, width, animate: true, floatingCardId: _dragView.Layout.Id);
            MoveDropPlaceholder(_placements[_dragView.Layout.Id]);
        }
        args.Handled = true;
    }

    /// <summary>
    /// Chooses the position in the card order whose resulting slot for the
    /// dragged card lies nearest to where the card is being held.
    /// </summary>
    private int NearestDropIndex(
        string cardId,
        System.Windows.Point held,
        double availableWidth,
        out IReadOnlyList<OverviewDashboardPlacement> placements)
    {
        var visible = VisibleCards();
        var dragged = visible.First(card => string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase));
        var others = visible.Where(card => !ReferenceEquals(card, dragged)).ToList();
        var bestIndex = 0;
        var bestDistance = double.PositiveInfinity;
        placements = [];
        for (var index = 0; index <= others.Count; index++)
        {
            var order = others.ToList();
            order.Insert(index, dragged);
            var candidate = FlowPlacements(order, availableWidth);
            var slot = candidate.First(item => string.Equals(item.CardId, cardId, StringComparison.OrdinalIgnoreCase));
            var distance = Math.Pow(slot.Left - held.X, 2) + Math.Pow(slot.Top - held.Y, 2);
            if (distance >= bestDistance - .5) continue;
            bestDistance = distance;
            bestIndex = index;
            placements = candidate;
        }
        return bestIndex;
    }

    private async Task EndDragAsync(OverviewDashboardCardView view, MouseButtonEventArgs args)
    {
        if (_dragView is null) return;
        view.DragSurface.ReleaseMouseCapture();
        view.Root.Opacity = 1;
        view.SetInteractionPreview(false);
        view.ResetPointer();
        HideDropPlaceholder();
        var moved = _dragMoved;
        var target = _dragTargetIndex;
        _dragView = null;
        _dragMoved = false;
        _dragTargetIndex = -1;
        args.Handled = moved;
        if (!moved)
        {
            ApplyPlacement(_dashboard.ActualWidth);
            return;
        }

        var cardId = view.Layout.Id;
        var visible = VisibleCards();
        var currentIndex = visible.ToList().FindIndex(card =>
            string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase));
        if (target == currentIndex)
        {
            _animateFrom = CurrentVisualPositions();
            ApplyPlacement(_dashboard.ActualWidth);
            return;
        }
        await MoveCardAsync(cardId, target);
    }

    /// <summary>
    /// Moves a card to <paramref name="visibleIndex"/> among the visible cards
    /// and saves the new order.
    /// </summary>
    internal async Task MoveCardAsync(string cardId, int visibleIndex)
    {
        var layoutIndex = LayoutIndexForVisibleIndex(cardId, visibleIndex);
        await MutateAsync(layout => WithFlowPositions(
            OverviewDashboardLayoutPolicy.MoveCardToIndex(layout, cardId, layoutIndex)));
    }

    /// <summary>Sets a card's width in grid columns and saves it.</summary>
    internal async Task SetCardSpanAsync(string cardId, int span)
        => await MutateAsync(layout => WithFlowPositions(
            OverviewDashboardLayoutPolicy.SetCardSpan(layout, cardId, span)));

    /// <summary>
    /// The position among visible cards where a card held with its top-left
    /// corner at <paramref name="held"/> would be dropped.
    /// </summary>
    internal int DropIndexAt(string cardId, System.Windows.Point held)
        => NearestDropIndex(cardId, held, _dashboard.ActualWidth, out _);

    /// <summary>
    /// Converts a position among visible cards to a position in the saved order,
    /// which also holds cards that are hidden while they have nothing to show.
    /// </summary>
    private int LayoutIndexForVisibleIndex(string cardId, int visibleIndex)
    {
        var others = VisibleCards()
            .Where(card => !string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var remaining = _layout.Cards
            .Where(card => !string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (others.Length == 0) return remaining.Count;
        if (visibleIndex < others.Length)
            return remaining.FindIndex(card => string.Equals(card.Id, others[visibleIndex].Id, StringComparison.OrdinalIgnoreCase));
        return remaining.FindIndex(card => string.Equals(card.Id, others[^1].Id, StringComparison.OrdinalIgnoreCase)) + 1;
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
        _resizeStartWidth = view.Root.Width;
        _resizeStartSpan = OverviewDashboardLayoutPolicy.CardSpan(view.Layout);
        _resizeSpan = _resizeStartSpan;
        view.SetInteractionPreview(true);
        view.Root.CaptureMouse();
        args.Handled = true;
    }

    private void TrackResize(System.Windows.Input.MouseEventArgs args)
    {
        if (_resizeView is null || args.LeftButton != MouseButtonState.Pressed) return;
        var delta = args.GetPosition(_surface).X - _resizeOrigin.X;
        if (_resizeEdge.HasFlag(OverviewDashboardResizeEdge.Left)) delta = -delta;
        var width = _dashboard.ActualWidth;
        var pitch = OverviewDashboardLayoutEngine.ColumnPitch(width);
        var span = Math.Max(
            OverviewDashboardLayoutEngine.MinimumSpan(_resizeView.MinimumWidth, pitch),
            OverviewDashboardLayoutEngine.SpanForWidth(_resizeStartWidth + delta, pitch));
        if (span != _resizeSpan)
        {
            _resizeSpan = span;
            ApplyPlacements(FlowPlacements(VisibleCards(), width, _resizeView.Layout.Id, span), width, animate: true);
        }
        args.Handled = true;
    }

    private async Task EndResizeAsync(MouseButtonEventArgs args)
    {
        if (_resizeView is null) return;
        var view = _resizeView;
        view.Root.ReleaseMouseCapture();
        view.SetInteractionPreview(false);
        view.ResetPointer();
        var span = _resizeSpan;
        _resizeView = null;
        args.Handled = true;
        if (span != _resizeStartSpan)
            await SetCardSpanAsync(view.Layout.Id, span);
        else
            ApplyPlacement(_dashboard.ActualWidth);
    }

    /// <summary>
    /// Stores where the flow places each visible card in <paramref name="layout"/>,
    /// so the saved positions match the saved order.
    /// </summary>
    private OverviewDashboardLayout WithFlowPositions(OverviewDashboardLayout layout)
    {
        var width = _dashboard.ActualWidth;
        var visibleIds = VisibleCards().Select(card => card.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cards = layout.Cards.Where(card => visibleIds.Contains(card.Id) && _cardViews.ContainsKey(card.Id));
        var locked = layout.CardSizesLocked ? layout.LockedSurfaceWidth : (double?)null;
        var bounds = FlowPlacements(cards, width).ToDictionary(
            placement => placement.CardId,
            placement => OverviewDashboardLayoutEngine.ToBounds(placement, width, locked),
            StringComparer.OrdinalIgnoreCase);
        return OverviewDashboardLayoutPolicy.SetCardBounds(layout, bounds);
    }

    private Dictionary<string, OverviewDashboardCardBounds> RenderedCardBounds(double availableWidth, double? lockedSurfaceWidth)
        => _placements.Values.ToDictionary(
            placement => placement.CardId,
            placement => OverviewDashboardLayoutEngine.ToBounds(placement, availableWidth, lockedSurfaceWidth),
            StringComparer.OrdinalIgnoreCase);

    private Dictionary<string, System.Windows.Point> CurrentVisualPositions()
        => _cardViews.ToDictionary(
            pair => pair.Key,
            pair => CurrentVisualPosition(pair.Value.Root),
            StringComparer.OrdinalIgnoreCase);

    private static System.Windows.Point CurrentVisualPosition(FrameworkElement element)
    {
        var left = Canvas.GetLeft(element);
        var top = Canvas.GetTop(element);
        if (!double.IsFinite(left) || !double.IsFinite(top)) return new(double.NaN, double.NaN);
        var offset = element.RenderTransform as TranslateTransform;
        return new(left + (offset?.X ?? 0), top + (offset?.Y ?? 0));
    }

    private static void ApplyCardPlacement(
        OverviewDashboardCardView view,
        OverviewDashboardPlacement placement,
        double availableWidth)
    {
        var width = placement.Width;
        var height = placement.Height;
        var left = Math.Clamp(placement.Left, 0, Math.Max(0, availableWidth - width));
        if (!SameLength(view.Root.Width, width)) view.Root.Width = width;
        if (!SameLength(view.Root.Height, height)) view.Root.Height = height;
        if (!SameLength(Canvas.GetLeft(view.Root), left)) Canvas.SetLeft(view.Root, left);
        if (!SameLength(Canvas.GetTop(view.Root), placement.Top)) Canvas.SetTop(view.Root, placement.Top);
    }

    /// <summary>
    /// Slides a card from where it was drawn to its new slot. Skipped when
    /// Windows animations are turned off.
    /// </summary>
    private static void AnimateFrom(FrameworkElement element, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)
            || (Math.Abs(dx) < .5 && Math.Abs(dy) < .5)
            || !SystemParameters.ClientAreaAnimation)
        {
            StopAnimation(element);
            return;
        }
        if (element.RenderTransform is not TranslateTransform offset || offset.IsFrozen)
        {
            offset = new TranslateTransform();
            element.RenderTransform = offset;
        }
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        offset.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(dx, 0, MoveAnimationDuration) { EasingFunction = ease });
        offset.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(dy, 0, MoveAnimationDuration) { EasingFunction = ease });
    }

    private static void StopAnimation(FrameworkElement element)
    {
        if (element.RenderTransform is not TranslateTransform offset || offset.IsFrozen) return;
        offset.BeginAnimation(TranslateTransform.XProperty, null);
        offset.BeginAnimation(TranslateTransform.YProperty, null);
        offset.X = 0;
        offset.Y = 0;
    }

    private void ShowDropPlaceholder()
    {
        if (_dropPlaceholder is null)
        {
            _dropPlaceholder = new System.Windows.Shapes.Rectangle
            {
                RadiusX = 6,
                RadiusY = 6,
                StrokeThickness = 1.5,
                StrokeDashArray = [4, 3],
                IsHitTestVisible = false,
                Opacity = .9
            };
            _dropPlaceholder.SetResourceReference(Shape.StrokeProperty, "AccentBlue");
            _dropPlaceholder.SetResourceReference(Shape.FillProperty, "InfoSoft");
        }
        if (!_surface.Children.Contains(_dropPlaceholder))
            _surface.Children.Add(_dropPlaceholder);
        WpfPanel.SetZIndex(_dropPlaceholder, -1);
        _dropPlaceholder.Visibility = Visibility.Collapsed;
    }

    private void MoveDropPlaceholder(OverviewDashboardPlacement slot)
    {
        if (_dropPlaceholder is null) return;
        var previous = _dropPlaceholder.Visibility == Visibility.Visible
            ? CurrentVisualPosition(_dropPlaceholder)
            : new System.Windows.Point(double.NaN, double.NaN);
        _dropPlaceholder.Width = slot.Width;
        _dropPlaceholder.Height = slot.Height;
        Canvas.SetLeft(_dropPlaceholder, slot.Left);
        Canvas.SetTop(_dropPlaceholder, slot.Top);
        _dropPlaceholder.Visibility = Visibility.Visible;
        AnimateFrom(_dropPlaceholder, previous.X - slot.Left, previous.Y - slot.Top);
    }

    private void HideDropPlaceholder()
    {
        if (_dropPlaceholder is null) return;
        _dropPlaceholder.Visibility = Visibility.Collapsed;
        _surface.Children.Remove(_dropPlaceholder);
    }
}
