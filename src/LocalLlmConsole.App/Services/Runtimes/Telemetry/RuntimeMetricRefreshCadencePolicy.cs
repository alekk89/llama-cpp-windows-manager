namespace LocalLlmConsole.Services;

public static class RuntimeMetricRefreshCadencePolicy
{
    public static TimeSpan Interval(string currentPage, bool minimizedOrHidden)
        => minimizedOrHidden
            ? TimeSpan.FromSeconds(10)
            : string.Equals(currentPage, "Overview", StringComparison.Ordinal)
                ? TimeSpan.FromSeconds(1)
                : TimeSpan.FromSeconds(5);

    public static bool ShouldPoll(
        string currentPage,
        bool minimizedOrHidden,
        DateTimeOffset now,
        DateTimeOffset lastPollAt)
        => lastPollAt == DateTimeOffset.MinValue
            || now - lastPollAt >= Interval(currentPage, minimizedOrHidden);
}
