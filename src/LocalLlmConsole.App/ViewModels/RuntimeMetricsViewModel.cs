using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class RuntimeMetricsViewModel
{
    public ObservableCollection<RuntimeMetricRow> Rows { get; } = new();

    public void ReplaceSamples(IReadOnlyList<PrometheusSample> samples, RuntimeMetricRow? leadingRow = null)
    {
        var rows = samples
            .OrderBy(sample => sample.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(sample => sample.Labels, StringComparer.OrdinalIgnoreCase)
            .Select(sample => new RuntimeMetricRow
            {
                Name = sample.Name,
                Labels = sample.Labels,
                Value = string.IsNullOrWhiteSpace(sample.RawValue) ? DisplayFormatService.MetricNumber(sample.Value) : sample.RawValue,
                Type = sample.Type,
                Help = sample.Help
            })
            .ToList();
        if (leadingRow is not null)
            rows.Insert(0, leadingRow);
        Reconcile(rows);
    }

    private void Reconcile(IReadOnlyList<RuntimeMetricRow> desired)
    {
        for (var index = 0; index < desired.Count; index++)
        {
            var desiredRow = desired[index];
            var existingIndex = index < Rows.Count && SameKey(Rows[index], desiredRow)
                ? index
                : FindIndex(desiredRow, index + 1);
            if (existingIndex >= 0)
            {
                if (existingIndex != index)
                    Rows.Move(existingIndex, index);
                Rows[index].Apply(desiredRow);
            }
            else
            {
                Rows.Insert(index, desiredRow);
            }
        }

        while (Rows.Count > desired.Count)
            Rows.RemoveAt(Rows.Count - 1);
    }

    private int FindIndex(RuntimeMetricRow desired, int startIndex)
    {
        for (var index = Math.Max(0, startIndex); index < Rows.Count; index++)
            if (SameKey(Rows[index], desired)) return index;
        return -1;
    }

    private static bool SameKey(RuntimeMetricRow left, RuntimeMetricRow right)
        => string.Equals(left.Name, right.Name, StringComparison.OrdinalIgnoreCase)
           && string.Equals(left.Labels, right.Labels, StringComparison.OrdinalIgnoreCase);
}
