namespace LocalLlmConsole.Services;

internal static class RuntimeMetricSummaryCalculations
{
    public static double? CounterRateAndRemember(
        double? current,
        ref double? previous,
        ref DateTimeOffset? previousPollAt,
        DateTimeOffset now)
    {
        var rate = RuntimeDashboardService.CounterRate(current, previous, now, previousPollAt, 0.5);
        if (current is not null)
        {
            previous = current;
            previousPollAt = now;
        }

        return rate;
    }

    /// <summary>Computes a live rate using active generation-time deltas instead of wall-clock time.
    /// This prevents rate dilution during idle gaps between requests.</summary>
    public static double? SecondsBasedCounterRate(
        double? currentTokens,
        double? currentSeconds,
        ref double? previousTokens,
        ref double? previousSeconds)
    {
        if (currentTokens is null || currentSeconds is null
            || previousTokens is null || previousSeconds is null
            || currentTokens.Value < previousTokens.Value
            || currentSeconds.Value <= previousSeconds.Value)
        {
            if (currentTokens is not null) previousTokens = currentTokens;
            if (currentSeconds is not null) previousSeconds = currentSeconds;
            return null;
        }

        var tokensDelta = currentTokens.Value - previousTokens.Value;
        var secondsDelta = currentSeconds.Value - previousSeconds.Value;
        previousTokens = currentTokens;
        previousSeconds = currentSeconds;
        return secondsDelta > 0 ? tokensDelta / secondsDelta : null;
    }

    public static bool UsedPreviousCounter(double? observed, double? previous, double? display)
        => previous is not null
           && display == previous
           && (observed is null || observed.Value < previous.Value);

    public static bool UsedPreviousAverage(double? observed, double? previous)
        => observed is null && previous is not null;

    public static DateTimeOffset? DisplayValueCapturedAt(
        double? observed,
        double? display,
        DateTimeOffset? previousCapturedAt,
        DateTimeOffset now)
    {
        if (display is null) return null;
        return observed is not null && observed.Value == display.Value ? now : previousCapturedAt;
    }

    public static DateTimeOffset? LastKnownCapturedAt(RuntimeMetricDisplaySnapshot snapshot)
        => OldestCapturedAt(
               snapshot.GeneratedTokensCapturedAt,
               snapshot.PromptTokensCapturedAt,
               snapshot.MtpGeneratedTokensCapturedAt,
               snapshot.MtpAcceptedTokensCapturedAt,
               snapshot.AverageGenerationRateCapturedAt,
               snapshot.AveragePromptRateCapturedAt,
               snapshot.AverageMtpGeneratedRateCapturedAt,
               snapshot.AverageMtpAcceptedRateCapturedAt)
           ?? snapshot.CapturedAt;

    public static DateTimeOffset? OldestCapturedAt(params DateTimeOffset?[] capturedAt)
    {
        DateTimeOffset? oldest = null;
        foreach (var timestamp in capturedAt)
        {
            if (timestamp is null) continue;
            if (oldest is null || timestamp.Value < oldest.Value)
                oldest = timestamp;
        }

        return oldest;
    }
}
