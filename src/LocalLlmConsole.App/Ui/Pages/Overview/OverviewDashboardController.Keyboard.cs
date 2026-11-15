using System.Windows.Controls.Primitives;
using System.Windows.Input;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
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

        if (key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
        {
            CopyCardReadings(view);
            args.Handled = true;
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

    /// <summary>
    /// Ctrl+Left or Ctrl+Up moves the card one place earlier in the flow;
    /// Ctrl+Right or Ctrl+Down moves it one place later.
    /// </summary>
    private async Task MoveCardFromKeyboardAsync(OverviewDashboardCardView view, Key key)
    {
        var visible = VisibleCards().ToList();
        var current = visible.FindIndex(card => string.Equals(card.Id, view.Layout.Id, StringComparison.OrdinalIgnoreCase));
        var target = current + (key is Key.Left or Key.Up ? -1 : 1);
        if (current < 0 || target < 0 || target >= visible.Count) return;
        await MoveCardAsync(view.Layout.Id, target);
        FocusCard(view.Layout.Id);
    }

    /// <summary>
    /// Ctrl+Shift+Left or Right narrows or widens the card by one grid column.
    /// Card height follows its content.
    /// </summary>
    private async Task ResizeCardFromKeyboardAsync(OverviewDashboardCardView view, Key key)
    {
        if (_layout.CardSizesLocked || key is not (Key.Left or Key.Right)) return;
        var pitch = OverviewDashboardLayoutEngine.ColumnPitch(_dashboard.ActualWidth);
        var rendered = _placements.TryGetValue(view.Layout.Id, out var placement)
            ? OverviewDashboardLayoutEngine.SpanForWidth(placement.Width, pitch)
            : OverviewDashboardLayoutPolicy.CardSpan(view.Layout);
        var span = Math.Clamp(rendered + (key == Key.Left ? -1 : 1),
            OverviewDashboardLayoutEngine.MinimumSpan(view.MinimumWidth, pitch),
            OverviewDashboardLayoutEngine.Columns);
        if (span == OverviewDashboardLayoutPolicy.CardSpan(view.Layout)) return;
        await SetCardSpanAsync(view.Layout.Id, span);
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
