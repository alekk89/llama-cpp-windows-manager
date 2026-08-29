using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed record LifetimePageActionControllerActions(
    Func<LifetimeMetricRow?, Task> ResetLifetimeMetricAsync,
    Func<Task> ResetVisibleMetricsAsync,
    Func<Task> RangeChangedAsync,
    Func<Task> FiltersChangedAsync,
    Func<Task> DateSelectionChangedAsync,
    Func<Task> ClearDateSelectionAsync,
    Func<bool> IsApplyingPresentation,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class LifetimePageActionController
{
    private readonly LifetimePageActionControllerActions _actions;

    public LifetimePageActionController(LifetimePageActionControllerActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public LifetimePageActions Build()
        => new(
            Range_SelectionChanged,
            Filter_SelectionChanged,
            Calendar_SelectionChanged,
            ClearDateSelection_Click,
            ResetLifetimeRow_Click,
            ResetVisible_Click);

    private async void Range_SelectionChanged(object? sender, EventArgs e)
    {
        if (_actions.IsApplyingPresentation()) return;
        await _actions.RunEventAsync(_actions.RangeChangedAsync);
    }

    private async void Filter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_actions.IsApplyingPresentation()) return;
        await _actions.RunEventAsync(_actions.FiltersChangedAsync);
    }

    private async void Calendar_SelectionChanged(object? sender, EventArgs e)
    {
        if (_actions.IsApplyingPresentation()) return;
        await _actions.RunEventAsync(_actions.DateSelectionChangedAsync);
    }

    private async void ClearDateSelection_Click(object sender, RoutedEventArgs e)
        => await _actions.RunEventAsync(_actions.ClearDateSelectionAsync);

    private async void ResetLifetimeRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            await _actions.ResetLifetimeMetricAsync((sender as FrameworkElement)?.Tag as LifetimeMetricRow);
        });
    }

    private async void ResetVisible_Click(object sender, RoutedEventArgs e)
        => await _actions.RunEventAsync(_actions.ResetVisibleMetricsAsync);
}
