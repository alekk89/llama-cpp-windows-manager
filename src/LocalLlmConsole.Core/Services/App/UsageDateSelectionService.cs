namespace LocalLlmConsole.Services;

/// <summary>Applies deterministic single, toggle, and anchored range selection to selectable local dates.</summary>
public sealed class UsageDateSelectionService
{
    public UsageDateSelection Apply(
        UsageDateSelection current,
        DateOnly clicked,
        UsageDateSelectionMode mode,
        IReadOnlyCollection<DateOnly>? selectableDates = null)
    {
        ArgumentNullException.ThrowIfNull(current);
        if (selectableDates is not null && !selectableDates.Contains(clicked)) return current;

        var allowed = selectableDates is null ? null : selectableDates.ToHashSet();
        var selected = current.Dates.Distinct().Order().ToHashSet();
        switch (mode)
        {
            case UsageDateSelectionMode.Toggle:
                if (!selected.Add(clicked)) selected.Remove(clicked);
                return Result(selected, clicked);

            case UsageDateSelectionMode.Range:
            case UsageDateSelectionMode.AddRange:
                var anchor = current.Anchor
                    ?? current.Dates.Order().LastOrDefault(clicked);
                var range = DatesBetween(anchor, clicked)
                    .Where(date => allowed is null || allowed.Contains(date));
                if (mode == UsageDateSelectionMode.Range) selected.Clear();
                selected.UnionWith(range);
                return Result(selected, anchor);

            default:
                if (selected.Count == 1 && selected.Contains(clicked))
                    return UsageDateSelection.Empty;
                return new UsageDateSelection([clicked], clicked);
        }
    }

    private static UsageDateSelection Result(HashSet<DateOnly> dates, DateOnly? anchor)
        => dates.Count == 0
            ? UsageDateSelection.Empty
            : new UsageDateSelection(dates.Order().ToArray(), anchor);

    private static IEnumerable<DateOnly> DatesBetween(DateOnly first, DateOnly second)
    {
        var start = first <= second ? first : second;
        var end = first <= second ? second : first;
        for (var date = start; date <= end; date = date.AddDays(1)) yield return date;
    }
}
