using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class RuntimeMetricsViewModel
{
    public ObservableCollection<UiRow> Rows { get; } = new();

    public void ReplaceSamples(IReadOnlyList<PrometheusSample> samples, UiRow? leadingRow = null)
    {
        var rows = samples
            .OrderBy(sample => sample.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sample => sample.Labels, StringComparer.OrdinalIgnoreCase)
            .Select(sample => new UiRow
            {
                C1 = sample.Name,
                C2 = sample.Labels,
                C3 = string.IsNullOrWhiteSpace(sample.RawValue) ? DisplayFormatService.MetricNumber(sample.Value) : sample.RawValue,
                C4 = sample.Type,
                C5 = sample.Help
            })
            .ToList();
        if (leadingRow is not null)
            rows.Insert(0, leadingRow);
        UiRowCollectionUpdater.Reconcile(Rows, rows, row => $"{row.C1}\0{row.C2}");
    }
}
