namespace LocalLlmConsole.Services;

public sealed class UsageMetricsService
{
    public UsageMetricsWindow ResolveWindow(
        UsageMetricsQuery query,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(query);
        var dates = NormalizeDates(query.Dates);
        if (dates.Count == 0) return ResolveWindow(query.Range, now, timeZone);
        return new UsageMetricsWindow(
            LocalMidnightUtc(dates[0], timeZone),
            LocalMidnightUtc(dates[^1].AddDays(1), timeZone),
            dates[0],
            dates[^1]);
    }

    public UsageMetricsWindow ResolveWindow(
        UsageMetricsRange range,
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var localNow = TimeZoneInfo.ConvertTime(now, timeZone);
        var lastDate = DateOnly.FromDateTime(localNow.Date);
        if (range == UsageMetricsRange.CurrentMonth)
        {
            var firstOfMonth = new DateOnly(lastDate.Year, lastDate.Month, 1);
            var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
            return new UsageMetricsWindow(
                LocalMidnightUtc(firstOfMonth, timeZone),
                LocalMidnightUtc(lastOfMonth.AddDays(1), timeZone),
                firstOfMonth,
                lastOfMonth);
        }

        var dayCount = range switch
        {
            UsageMetricsRange.OneDay => 1,
            UsageMetricsRange.SevenDays => 7,
            UsageMetricsRange.ThirtyDays => 30,
            UsageMetricsRange.NinetyDays => 90,
            _ => 0
        };
        DateOnly? firstDate = dayCount == 0 ? null : lastDate.AddDays(-(dayCount - 1));
        DateTimeOffset? fromUtc = firstDate is null ? null : LocalMidnightUtc(firstDate.Value, timeZone);
        var toUtc = LocalMidnightUtc(lastDate.AddDays(1), timeZone);
        return new UsageMetricsWindow(fromUtc, toUtc, firstDate, lastDate);
    }

    public UsageMetricsWindow ResolveCalendarWindow(
        DateTimeOffset now,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);
        var localToday = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, timeZone).Date);
        var currentMonth = new DateOnly(localToday.Year, localToday.Month, 1);
        var firstDate = currentMonth.AddMonths(-23);
        var lastDate = currentMonth.AddMonths(1).AddDays(-1);
        return new UsageMetricsWindow(
            LocalMidnightUtc(firstDate, timeZone),
            LocalMidnightUtc(lastDate.AddDays(1), timeZone),
            firstDate,
            lastDate);
    }

    public UsageMetricsReport BuildReport(
        UsageMetricsQuery query,
        IReadOnlyList<UsageMetricBucket> buckets,
        IReadOnlyList<TokenUsageRecord> lifetime,
        UsageMetricDimensions dimensions,
        DateTimeOffset now,
        TimeZoneInfo timeZone,
        DateTimeOffset? trackingStartedAtOverride = null)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(buckets);
        ArgumentNullException.ThrowIfNull(lifetime);
        ArgumentNullException.ThrowIfNull(dimensions);
        ArgumentNullException.ThrowIfNull(timeZone);

        var selectedDates = NormalizeDates(query.Dates);
        var selectedDateSet = selectedDates.ToHashSet();
        var window = ResolveWindow(query, now, timeZone);
        var filtered = buckets
            .Where(bucket => WithinWindow(bucket, window))
            .Where(bucket => selectedDateSet.Count == 0
                || selectedDateSet.Contains(LocalDate(bucket.BucketStartUtc, timeZone)))
            .Where(bucket => Matches(bucket.ModelId, query.ModelId))
            .Where(bucket => Matches(bucket.LaunchProfileId, query.LaunchProfileId))
            .Where(bucket => Matches(bucket.RuntimeId, query.RuntimeId))
            .ToArray();

        var trackedSummary = Totals(filtered);
        var canUseLifetime = selectedDates.Count == 0
            && query.Range == UsageMetricsRange.All
            && string.IsNullOrWhiteSpace(query.LaunchProfileId)
            && string.IsNullOrWhiteSpace(query.RuntimeId);
        var lifetimeRows = lifetime
            .Where(row => Matches(row.ModelId, query.ModelId))
            .ToArray();
        var lifetimeSummary = UsageMetricTotals.Sum(lifetimeRows.Select(Totals));
        var summary = canUseLifetime ? lifetimeSummary : trackedSummary;
        DateTimeOffset? trackingStartedAt = trackingStartedAtOverride
            ?? (buckets.Count == 0 ? null : buckets.Min(bucket => bucket.BucketStartUtc));
        var days = BuildDays(filtered, window, timeZone, trackingStartedAt, now, selectedDates);
        var calendarWindow = ResolveCalendarWindow(now, timeZone);
        var calendarBuckets = buckets
            .Where(bucket => WithinWindow(bucket, calendarWindow))
            .Where(bucket => Matches(bucket.ModelId, query.ModelId))
            .Where(bucket => Matches(bucket.LaunchProfileId, query.LaunchProfileId))
            .Where(bucket => Matches(bucket.RuntimeId, query.RuntimeId))
            .ToArray();
        var calendarDays = BuildDays(calendarBuckets, calendarWindow, timeZone, trackingStartedAt, now, []);
        var modelRows = BuildModelRows(filtered, lifetimeRows, canUseLifetime);
        var insights = BuildInsights(days, trackedSummary);
        var includesLegacy = canUseLifetime && summary.TotalTokens > trackedSummary.TotalTokens;

        return new UsageMetricsReport(
            query,
            window,
            summary,
            trackedSummary,
            days,
            calendarWindow,
            calendarDays,
            modelRows,
            dimensions,
            insights,
            trackingStartedAt,
            includesLegacy);
    }

    public static UsageMetricsRange ParseRange(string? value)
        => value?.Trim().ToLowerInvariant() switch
        {
            "1" or "1d" or "day" or "today" => UsageMetricsRange.OneDay,
            "7" or "7d" or "week" => UsageMetricsRange.SevenDays,
            "month" or "current-month" or "currentmonth" => UsageMetricsRange.CurrentMonth,
            "30" or "30d" => UsageMetricsRange.ThirtyDays,
            "90" or "90d" or "quarter" => UsageMetricsRange.NinetyDays,
            "all" or "lifetime" => UsageMetricsRange.All,
            _ => UsageMetricsRange.ThirtyDays
        };

    public static IReadOnlyList<DateOnly> NormalizeDates(IEnumerable<DateOnly>? dates)
        => dates?.Distinct().Order().ToArray() ?? [];

    private static IReadOnlyList<UsageMetricDay> BuildDays(
        IReadOnlyList<UsageMetricBucket> buckets,
        UsageMetricsWindow window,
        TimeZoneInfo timeZone,
        DateTimeOffset? trackingStartedAt,
        DateTimeOffset now,
        IReadOnlyList<DateOnly> selectedDates)
    {
        var grouped = buckets
            .GroupBy(bucket => LocalDate(bucket.BucketStartUtc, timeZone))
            .ToDictionary(group => group.Key, Totals);
        var localToday = LocalDate(now, timeZone);
        var firstTrackedDate = trackingStartedAt is null
            ? (DateOnly?)null
            : LocalDate(trackingStartedAt.Value, timeZone);
        if (selectedDates.Count > 0)
            return selectedDates
                .Select(date => new UsageMetricDay(
                    date,
                    grouped.GetValueOrDefault(date, UsageMetricTotals.Empty),
                    IsTracked(date, firstTrackedDate, localToday)))
                .ToArray();

        var firstDate = window.FirstLocalDate
            ?? (grouped.Count == 0 ? null : grouped.Keys.Min());
        if (firstDate is null) return [];

        var result = new List<UsageMetricDay>();
        for (var date = firstDate.Value; date <= window.LastLocalDate; date = date.AddDays(1))
            result.Add(new UsageMetricDay(
                date,
                grouped.GetValueOrDefault(date, UsageMetricTotals.Empty),
                IsTracked(date, firstTrackedDate, localToday)));
        return result;
    }

    private static IReadOnlyList<UsageMetricModelBreakdown> BuildModelRows(
        IReadOnlyList<UsageMetricBucket> buckets,
        IReadOnlyList<TokenUsageRecord> lifetime,
        bool useLifetime)
    {
        if (useLifetime)
        {
            var trackedByModel = buckets
                .GroupBy(bucket => bucket.ModelId, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, Totals, StringComparer.OrdinalIgnoreCase);
            return lifetime
                .Select(row =>
                {
                    var totals = Totals(row);
                    var tracked = trackedByModel.GetValueOrDefault(row.ModelId, UsageMetricTotals.Empty);
                    return new UsageMetricModelBreakdown(
                        row.ModelId,
                        row.ModelName,
                        totals with { CacheCounterObserved = tracked.CacheCounterObserved },
                        tracked,
                        row.UpdatedAt);
                })
                .OrderByDescending(row => row.Totals.TotalTokens)
                .ThenBy(row => row.ModelName, StringComparer.CurrentCultureIgnoreCase)
                .ToArray();
        }

        return buckets
            .GroupBy(bucket => new { bucket.ModelId, bucket.ModelName })
            .Select(group => new UsageMetricModelBreakdown(
                group.Key.ModelId,
                group.Key.ModelName,
                Totals(group),
                Totals(group),
                group.Max(bucket => bucket.UpdatedAt)))
            .OrderByDescending(row => row.Totals.TotalTokens)
            .ThenBy(row => row.ModelName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    private static UsageMetricTotals Totals(IEnumerable<UsageMetricBucket> buckets)
        => UsageMetricTotals.Sum(buckets.Select(bucket => new UsageMetricTotals(
            bucket.PromptTokens,
            bucket.CachedPromptTokens,
            bucket.GeneratedTokens,
            bucket.CacheCounterObserved,
            bucket.PromptSeconds,
            bucket.GeneratedSeconds,
            bucket.TimingCounterObserved,
            bucket.RequestCount,
            bucket.FailedRequestCount,
            bucket.RequestCounterObserved)));

    private static UsageMetricTotals Totals(TokenUsageRecord row)
        => new(row.PromptTokens, row.CachedPromptTokens, row.GeneratedTokens, row.CacheCounterObserved);

    private static UsageMetricsInsights BuildInsights(
        IReadOnlyList<UsageMetricDay> days,
        UsageMetricTotals trackedSummary)
    {
        var active = days
            .Where(day => day.IsTracked && (day.Totals.TotalTokens > 0 || day.Totals.RequestCount > 0))
            .ToArray();
        if (active.Length == 0) return UsageMetricsInsights.Empty;
        var peak = active
            .OrderByDescending(day => day.Totals.TotalTokens)
            .ThenByDescending(day => day.Totals.RequestCount)
            .ThenBy(day => day.Date)
            .First();
        return new UsageMetricsInsights(
            active.Length,
            peak.Date,
            peak.Totals,
            trackedSummary.TotalTokens / (double)active.Length);
    }

    private static bool Matches(string actual, string expected)
        => string.IsNullOrWhiteSpace(expected)
           || actual.Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static bool WithinWindow(UsageMetricBucket bucket, UsageMetricsWindow window)
        => (window.FromUtc is null || bucket.BucketStartUtc >= window.FromUtc.Value)
           && bucket.BucketStartUtc < window.ToUtc;

    private static DateOnly LocalDate(DateTimeOffset value, TimeZoneInfo timeZone)
        => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(value, timeZone).Date);

    private static bool IsTracked(DateOnly date, DateOnly? firstTrackedDate, DateOnly localToday)
        => firstTrackedDate is not null && date >= firstTrackedDate.Value && date <= localToday;

    private static DateTimeOffset LocalMidnightUtc(DateOnly date, TimeZoneInfo timeZone)
    {
        var local = DateTime.SpecifyKind(date.ToDateTime(TimeOnly.MinValue), DateTimeKind.Unspecified);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(local, timeZone), TimeSpan.Zero);
    }
}
