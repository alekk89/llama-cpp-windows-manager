using System.Windows;
using System.Windows.Input;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    private OverviewDashboardCardView? _reorderView;
    private UIElement? _reorderEventRoot;

    private void ConfigureReorderPersistence()
        => _root.Unloaded += async (_, _) => await CommitMetricReorderAsync();

    private async Task BeginMetricReorderAsync(string cardId)
    {
        if (_reorderView is not null)
            await CommitMetricReorderAsync();
        if (!_cardViews.TryGetValue(cardId, out var view) || view.MetricIds.Count < 2)
            return;

        _reorderView = view;
        view.SetReorderMode(true);
        _reorderEventRoot = Window.GetWindow(_root) is { } window ? window : _root;
        _reorderEventRoot.AddHandler(
            Mouse.PreviewMouseDownEvent,
            new MouseButtonEventHandler(HandleReorderBoundaryClick),
            handledEventsToo: true);
    }

    private async void HandleReorderBoundaryClick(object sender, MouseButtonEventArgs args)
    {
        if (_reorderView is null || args.ChangedButton != MouseButton.Left) return;
        if (_reorderView.ContainsMetricRow(args.OriginalSource as DependencyObject)) return;
        args.Handled = true;
        await CommitMetricReorderAsync();
    }

    private async Task CommitMetricReorderAsync()
    {
        if (_reorderView is not { } view) return;
        var cardId = view.Layout.Id;
        var order = view.CurrentMetricOrder;
        _reorderView = null;
        view.SetReorderMode(false);
        if (_reorderEventRoot is not null)
        {
            _reorderEventRoot.RemoveHandler(
                Mouse.PreviewMouseDownEvent,
                new MouseButtonEventHandler(HandleReorderBoundaryClick));
            _reorderEventRoot = null;
        }

        if (!order.SequenceEqual(view.Layout.MetricIds, StringComparer.Ordinal))
            await MutateAsync(layout => OverviewDashboardLayoutPolicy.ReorderMetrics(layout, cardId, order));
    }
}
