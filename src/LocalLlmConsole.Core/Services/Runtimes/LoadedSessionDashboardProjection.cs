namespace LocalLlmConsole.Services;

/// <summary>One running session as shown by the Overview loaded-models card.</summary>
public sealed record LoadedSessionDashboardRow(
    string SessionId,
    string ModelId,
    string ModelName,
    string ProfileName,
    LoadedModelSessionStatus Status,
    bool IsSelected,
    int Port,
    double? AverageGenerationRate,
    double? ActiveSlots,
    int? SlotCapacity,
    double? KvCacheUsagePercent);

/// <summary>
/// Projects the telemetry poll that already runs for every session into compact
/// per-session rows. It is stateless, so it never disturbs the selected session's
/// rate history, and it issues no requests of its own.
/// </summary>
public static class LoadedSessionDashboardProjection
{
    public static IReadOnlyList<LoadedSessionDashboardRow> Project(
        IEnumerable<LoadedModelSessionSnapshot> sessions,
        IEnumerable<RuntimeMetricPollResult> pollResults)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(pollResults);
        var polls = pollResults
            .GroupBy(result => result.Session.SessionId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        return sessions
            .Where(session => session.IsRunning)
            .OrderBy(session => session.ModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.LaunchProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(session => Row(session, polls.GetValueOrDefault(session.SessionId)))
            .ToArray();
    }

    private static LoadedSessionDashboardRow Row(LoadedModelSessionSnapshot session, RuntimeMetricPollResult? poll)
    {
        var responded = poll is not null && (poll.Samples.Count > 0 || poll.SlotSnapshot is not null);
        double? generationRate = null;
        double? activeSlots = null;
        int? slotCapacity = null;
        double? kvPercent = null;
        if (responded)
        {
            var samples = poll!.Samples;
            generationRate = RuntimeDashboardService.Rate(
                    RuntimeDashboardService.GeneratedTokenCounter(samples),
                    RuntimeDashboardService.GeneratedSecondsCounter(samples))
                ?? PositiveOrNull(RuntimeMetrics.Sum(samples, ["predicted", "tokens", "second"], ["total"])
                    ?? RuntimeMetrics.Sum(samples, ["generation", "tokens", "second"], ["total"]));
            var capacity = RuntimeMetricSummaryTracker.ObserveCapacity(samples, poll.SlotSnapshot, session.LaunchSettings);
            activeSlots = capacity.ActiveSlots;
            slotCapacity = capacity.SlotCapacity;
            kvPercent = capacity.KvUsagePercent;
        }

        return new LoadedSessionDashboardRow(
            session.SessionId,
            session.ModelId,
            session.ModelName,
            session.LaunchProfileName,
            session.Status,
            session.IsSelected,
            session.LaunchSettings.Port,
            generationRate,
            activeSlots,
            slotCapacity,
            kvPercent);
    }

    private static double? PositiveOrNull(double? value)
        => value is > 0 && double.IsFinite(value.Value) ? value : null;
}
