using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    private const double KeyboardHorizontalStep = .25;
    private const double KeyboardVerticalStep = 4;

    private void ConfigureKeyboardInteraction(OverviewDashboardCardView view)
    {
        view.Root.PreviewKeyDown += async (_, args) => await HandleCardKeyAsync(view, args);
        foreach (var (metricId, row) in view.MetricRows)
            row.Root.PreviewKeyDown += async (_, args) => await HandleMetricKeyAsync(view, metricId, args);
    }

    private async Task HandleCardKeyAsync(OverviewDashboardCardView view, WpfKeyEventArgs args)
    {
        var key = EffectiveKey(args);
        if (key == Key.Apps || key == Key.F10 && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            if (view.Root.ContextMenu is { } menu)
            {
                menu.PlacementTarget = view.Root;
                menu.Placement = PlacementMode.Bottom;
                menu.IsOpen = true;
                args.Handled = true;
            }
            return;
        }

        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control)
            || key is not (Key.Left or Key.Right or Key.Up or Key.Down))
            return;

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            await ResizeCardFromKeyboardAsync(view, key);
        else
            await MoveCardFromKeyboardAsync(view, key);
        args.Handled = true;
    }

    private async Task MoveCardFromKeyboardAsync(OverviewDashboardCardView view, Key key)
    {
        var bounds = RenderedBounds(view, _dashboard.ActualWidth);
        var candidate = OverviewDashboardLayoutPolicy.ConstrainBounds(bounds with
        {
            X = bounds.X + (key == Key.Left ? -KeyboardHorizontalStep : key == Key.Right ? KeyboardHorizontalStep : 0),
            Y = bounds.Y + (key == Key.Up ? -KeyboardVerticalStep : key == Key.Down ? KeyboardVerticalStep : 0)
        });
        candidate = VisibleBounds(view, candidate, _dashboard.ActualWidth);
        candidate = OverviewDashboardLayoutEngine.SnapMove(
            candidate,
            InteractionObstacles(view.Layout.Id, _dashboard.ActualWidth),
            _dashboard.ActualWidth);
        var persisted = PersistedBounds(candidate, _dashboard.ActualWidth);
        await MutateAsync(layout => OverviewDashboardLayoutPolicy.SetCardBounds(layout, view.Layout.Id, persisted));
        FocusCard(view.Layout.Id);
    }

    private async Task ResizeCardFromKeyboardAsync(OverviewDashboardCardView view, Key key)
    {
        if (_layout.CardSizesLocked) return;
        var bounds = RenderedBounds(view, _dashboard.ActualWidth);
        view.UpdateMinimumSize(view.Root.Width);
        var minimumWidth = Math.Max(
            OverviewDashboardLayoutPolicy.MinimumCardWidth,
            OverviewDashboardLayoutEngine.ToHorizontalUnits(
                view.MinimumWidth + OverviewDashboardLayoutPolicy.CardGap,
                _dashboard.ActualWidth));
        var resized = OverviewDashboardLayoutPolicy.ConstrainBounds(bounds with
        {
            Width = Math.Max(minimumWidth, bounds.Width + (key == Key.Left ? -KeyboardHorizontalStep : key == Key.Right ? KeyboardHorizontalStep : 0)),
            Height = Math.Max(view.MinimumHeight, bounds.Height + (key == Key.Up ? -KeyboardVerticalStep : key == Key.Down ? KeyboardVerticalStep : 0))
        });
        await MutateAsync(layout => OverviewDashboardLayoutPolicy.SetCardBounds(layout, view.Layout.Id, resized));
        FocusCard(view.Layout.Id);
    }

    private async Task HandleMetricKeyAsync(
        OverviewDashboardCardView view,
        string metricId,
        WpfKeyEventArgs args)
    {
        var key = EffectiveKey(args);
        if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Alt) || key is not (Key.Up or Key.Down)) return;
        var order = view.Layout.MetricIds.ToList();
        var current = order.FindIndex(id => string.Equals(id, metricId, StringComparison.Ordinal));
        var target = key == Key.Up ? current - 1 : current + 1;
        if (current < 0 || target < 0 || target >= order.Count) return;
        (order[current], order[target]) = (order[target], order[current]);
        await MutateAsync(layout => OverviewDashboardLayoutPolicy.ReorderMetrics(layout, view.Layout.Id, order));
        FocusMetric(view.Layout.Id, metricId);
        args.Handled = true;
    }

    private void FocusCard(string cardId)
        => _ = _root.Dispatcher.BeginInvoke(() =>
        {
            if (_cardViews.TryGetValue(cardId, out var current))
                current.Root.Focus();
        });

    private void FocusMetric(string cardId, string metricId)
        => _ = _root.Dispatcher.BeginInvoke(() =>
        {
            if (_cardViews.TryGetValue(cardId, out var current)
                && current.MetricRows.TryGetValue(metricId, out var row))
                row.Root.Focus();
        });

    private static Key EffectiveKey(WpfKeyEventArgs args)
        => args.Key == Key.System ? args.SystemKey : args.Key;
}
