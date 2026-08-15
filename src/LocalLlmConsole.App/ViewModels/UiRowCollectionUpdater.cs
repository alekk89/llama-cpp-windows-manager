using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public static class UiRowCollectionUpdater
{
    public static bool Reconcile(
        ObservableCollection<UiRow> target,
        IEnumerable<UiRow> rows,
        Func<UiRow, string>? keySelector = null)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(rows);

        var desired = rows.ToArray();
        var changed = false;
        for (var index = 0; index < desired.Length; index++)
        {
            var desiredRow = desired[index];
            var existingIndex = index < target.Count && SameKey(target[index], desiredRow, keySelector)
                ? index
                : FindIndex(target, desiredRow, keySelector, index + 1);
            if (existingIndex >= 0)
            {
                if (existingIndex != index)
                {
                    target.Move(existingIndex, index);
                    changed = true;
                }

                changed |= Apply(target[index], desiredRow);
                continue;
            }

            target.Insert(index, desiredRow);
            changed = true;
        }

        while (target.Count > desired.Length)
        {
            target.RemoveAt(target.Count - 1);
            changed = true;
        }

        return changed;
    }

    private static int FindIndex(
        IReadOnlyList<UiRow> target,
        UiRow desired,
        Func<UiRow, string>? keySelector,
        int startIndex)
    {
        if (keySelector is null) return -1;
        for (var index = Math.Max(0, startIndex); index < target.Count; index++)
        {
            if (SameKey(target[index], desired, keySelector)) return index;
        }

        return -1;
    }

    private static bool SameKey(UiRow left, UiRow right, Func<UiRow, string>? keySelector)
        => keySelector is null
            || string.Equals(keySelector(left), keySelector(right), StringComparison.OrdinalIgnoreCase);

    private static bool Apply(UiRow target, UiRow source)
    {
        if (RowsEqual(target, source)) return false;
        target.Apply(source);
        return true;
    }

    private static bool RowsEqual(UiRow left, UiRow right)
        => string.Equals(left.C1, right.C1, StringComparison.Ordinal)
           && string.Equals(left.C2, right.C2, StringComparison.Ordinal)
           && string.Equals(left.C3, right.C3, StringComparison.Ordinal)
           && string.Equals(left.C4, right.C4, StringComparison.Ordinal)
           && string.Equals(left.C5, right.C5, StringComparison.Ordinal)
           && string.Equals(left.C6, right.C6, StringComparison.Ordinal)
           && string.Equals(left.C7, right.C7, StringComparison.Ordinal)
           && string.Equals(left.C8, right.C8, StringComparison.Ordinal)
           && string.Equals(left.C9, right.C9, StringComparison.Ordinal)
           && string.Equals(left.C10, right.C10, StringComparison.Ordinal)
           && string.Equals(left.T1, right.T1, StringComparison.Ordinal)
           && string.Equals(left.T2, right.T2, StringComparison.Ordinal)
           && string.Equals(left.T3, right.T3, StringComparison.Ordinal)
           && string.Equals(left.T4, right.T4, StringComparison.Ordinal)
           && string.Equals(left.T5, right.T5, StringComparison.Ordinal)
           && left.B1 == right.B1
           && left.B2 == right.B2
           && left.B3 == right.B3
           && left.B4 == right.B4
           && left.B5 == right.B5
           && JsonNode.DeepEquals(left.Data, right.Data);
}
