using System.Runtime.InteropServices;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardController
{
    /// <summary>The card's visible readings in their current order, as plain text.</summary>
    public string CardReadingsText(OverviewDashboardCardView view, DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(view);
        var rows = view.CurrentMetricOrder
            .Where(metricId => view.MetricRows.TryGetValue(metricId, out var row) && row.IsAvailable)
            .Select(metricId => (_registry.Definition(metricId).DisplayName,
                _readings.TryGetValue(metricId, out var reading) ? reading : null))
            .ToArray();
        var cardName = !string.IsNullOrWhiteSpace(view.Layout.Title)
            ? view.Layout.Title
            : string.Join(", ", view.Layout.MetricIds.Select(id => _registry.Definition(id).DisplayName));
        return OverviewDashboardCardTextFormatter.Format(
            cardName,
            rows,
            view.LoadedModels is null ? null : _loadedSessions,
            capturedAt);
    }

    private Task CopyCardReadingsAsync(string cardId)
    {
        if (_cardViews.TryGetValue(cardId, out var view))
            CopyCardReadings(view);
        return Task.CompletedTask;
    }

    private void CopyCardReadings(OverviewDashboardCardView view)
    {
        var text = CardReadingsText(view, DateTimeOffset.Now);
        var copyToClipboard = _actions.CopyToClipboard ?? System.Windows.Clipboard.SetText;
        bool copied;
        try
        {
            copyToClipboard(text);
            copied = true;
        }
        catch (Exception ex) when (ex is ExternalException or InvalidOperationException)
        {
            copied = false;
        }
        view.ShowCopyResult(copied);
    }
}
