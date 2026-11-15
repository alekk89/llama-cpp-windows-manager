namespace LocalLlmConsole.Services;

/// <summary>
/// Decides whether dashboard readings are last-known values and formats their
/// age compactly. Ages are coarse so the text changes at most once a minute
/// after the first minute.
/// </summary>
public static class OverviewDashboardFreshnessPolicy
{
    /// <summary>
    /// Returns the oldest last-known capture time, or null when every reading
    /// is live.
    /// </summary>
    public static DateTimeOffset? OldestLastKnown(IEnumerable<DateTimeOffset?> capturedAt)
    {
        ArgumentNullException.ThrowIfNull(capturedAt);
        DateTimeOffset? oldest = null;
        foreach (var value in capturedAt)
        {
            if (value is { } captured && (oldest is null || captured < oldest))
                oldest = captured;
        }
        return oldest;
    }

    /// <summary>
    /// Formats the age with the compact unit letters used elsewhere for elapsed
    /// times: seconds below one minute, whole minutes below one hour, then hours
    /// and minutes. Returns an empty string for a future or zero age.
    /// </summary>
    public static string RelativeAge(DateTimeOffset capturedAt, DateTimeOffset now)
    {
        var age = now - capturedAt;
        if (age < TimeSpan.FromSeconds(1)) return "";
        if (age < TimeSpan.FromMinutes(1))
            return $"{(int)age.TotalSeconds}s";
        if (age < TimeSpan.FromHours(1))
            return $"{(int)age.TotalMinutes}m";
        return $"{(int)age.TotalHours}h {age.Minutes:00}m";
    }
}
