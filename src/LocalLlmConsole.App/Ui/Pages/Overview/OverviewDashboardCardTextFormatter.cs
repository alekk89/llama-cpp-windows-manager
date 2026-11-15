namespace LocalLlmConsole;

/// <summary>Formats a card's current readings as plain text for bug reports.</summary>
public static class OverviewDashboardCardTextFormatter
{
    public static string Format(
        string cardName,
        IEnumerable<(string Name, OverviewDashboardMetricReading? Reading)> rows,
        IReadOnlyList<LoadedSessionDashboardRow>? sessions,
        DateTimeOffset capturedAt)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var lines = new List<string>
        {
            cardName,
            Loc.T("Dashboard.Copy.Captured", Timestamp(capturedAt))
        };
        foreach (var (name, reading) in rows)
            lines.Add($"{name}: {ReadingText(reading)}");
        if (sessions is not null)
        {
            if (sessions.Count == 0)
                lines.Add(Loc.T("Dashboard.LoadedModels.Empty"));
            foreach (var session in sessions)
            {
                var selected = session.IsSelected ? $" ({Loc.T("Dashboard.LoadedModels.Selected")})" : "";
                lines.Add($"{OverviewDashboardLoadedModelsView.DisplayName(session)}{selected}: "
                          + $"{OverviewDashboardLoadedModelsView.StatusLabel(session.Status)} — "
                          + OverviewDashboardLoadedModelsView.MetricsLine(session));
            }
        }
        return string.Join(Environment.NewLine, lines);
    }

    private static string ReadingText(OverviewDashboardMetricReading? reading)
    {
        if (reading is null || string.IsNullOrWhiteSpace(reading.Value))
            return Loc.T("Dashboard.ValueUnavailable");
        var text = reading.Value;
        if (!string.IsNullOrWhiteSpace(reading.Unit))
            text += reading.Unit == "%" ? reading.Unit : $" {reading.Unit}";
        if (!string.IsNullOrWhiteSpace(reading.Detail))
            text += $" — {reading.Detail}";
        if (reading.LastKnownCapturedAt is { } lastKnown)
            text += $" ({Loc.T("Dashboard.Copy.LastKnown", Timestamp(lastKnown))})";
        return text;
    }

    private static string Timestamp(DateTimeOffset value)
        => value.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture);
}
