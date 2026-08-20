namespace LocalLlmConsole.Services;

internal sealed class ControlUsageMetricsEndpoints : ControlEndpointHandler
{
    private readonly LifetimeMetricsApplicationService _lifetimeMetrics;

    public ControlUsageMetricsEndpoints(ControlEndpointContext context)
        : base(context)
    {
        _lifetimeMetrics = context.Dependencies.LifetimeMetrics
            ?? new LifetimeMetricsApplicationService(context.Dependencies.StateStore);
    }

    internal async Task<LocalControlApiResponse> HandleAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (method != "GET"
            || segments.Length != 4
            || !segments[3].Equals("usage", StringComparison.OrdinalIgnoreCase))
            return Error(404, "Not found.");

        var rangeText = Query(request, "range", "30d");
        if (!AcceptedRange(rangeText))
            return Error(400, "'range' must be one of: 1d, 7d, month, 30d, 90d, all.");
        var timeZone = ResolveTimeZone(Query(request, "timeZone", ""));
        if (!TryParseDates(Query(request, "dates", ""), out var dates, out var dateError))
            return Error(400, dateError);
        var query = new UsageMetricsQuery(
            UsageMetricsService.ParseRange(rangeText),
            Query(request, "model", ""),
            Query(request, "profile", ""),
            Query(request, "runtime", ""),
            dates);
        var report = await _lifetimeMetrics.GetReportAsync(query, timeZone);
        return Ok(new
        {
            ok = true,
            timeZone = timeZone.Id,
            report = ReportView(report)
        });
    }

    private static object ReportView(UsageMetricsReport report)
        => new
        {
            query = new
            {
                range = report.Query.Range.ToString(),
                modelId = report.Query.ModelId,
                launchProfileId = report.Query.LaunchProfileId,
                runtimeId = report.Query.RuntimeId,
                dates = UsageMetricsService.NormalizeDates(report.Query.Dates)
                    .Select(date => date.ToString("yyyy-MM-dd"))
                    .ToArray()
            },
            window = new
            {
                fromUtc = report.Window.FromUtc,
                toUtc = report.Window.ToUtc,
                firstLocalDate = report.Window.FirstLocalDate?.ToString("yyyy-MM-dd"),
                lastLocalDate = report.Window.LastLocalDate.ToString("yyyy-MM-dd")
            },
            summary = TotalsView(report.Summary),
            trackedSummary = TotalsView(report.TrackedSummary),
            days = report.Days.Select(DayView).ToArray(),
            calendarWindow = new
            {
                fromUtc = report.CalendarWindow.FromUtc,
                toUtc = report.CalendarWindow.ToUtc,
                firstLocalDate = report.CalendarWindow.FirstLocalDate?.ToString("yyyy-MM-dd"),
                lastLocalDate = report.CalendarWindow.LastLocalDate.ToString("yyyy-MM-dd")
            },
            calendarDays = report.CalendarDays.Select(DayView).ToArray(),
            models = report.Models.Select(model => new
            {
                modelId = model.ModelId,
                modelName = model.ModelName,
                totals = TotalsView(model.Totals),
                cacheStatistics = TotalsView(model.CacheStatistics),
                trackedTokenShare = report.TrackedSummary.TotalTokens > 0
                    ? model.CacheStatistics.TotalTokens / (double)report.TrackedSummary.TotalTokens
                    : (double?)null,
                updatedAt = model.UpdatedAt
            }).ToArray(),
            dimensions = new
            {
                models = report.Dimensions.Models.Select(DimensionView).ToArray(),
                launchProfiles = report.Dimensions.LaunchProfiles.Select(DimensionView).ToArray(),
                runtimes = report.Dimensions.Runtimes.Select(DimensionView).ToArray()
            },
            insights = new
            {
                activeDays = report.Insights.ActiveDays,
                averageTokensPerActiveDay = report.Insights.AverageTokensPerActiveDay,
                peakDate = report.Insights.PeakDate?.ToString("yyyy-MM-dd"),
                peakTotals = TotalsView(report.Insights.PeakTotals)
            },
            trackingStartedAt = report.TrackingStartedAt,
            includesLegacyTotals = report.IncludesLegacyTotals
        };

    private static object TotalsView(UsageMetricTotals totals)
        => new
        {
            promptTokens = totals.PromptTokens,
            cachedPromptTokens = totals.CachedPromptTokens,
            inputTokens = totals.InputTokens,
            generatedTokens = totals.GeneratedTokens,
            totalTokens = totals.TotalTokens,
            cacheCounterObserved = totals.CacheCounterObserved,
            cacheHitRate = totals.CacheHitRate,
            promptSeconds = totals.PromptSeconds,
            generatedSeconds = totals.GeneratedSeconds,
            timingCounterObserved = totals.TimingCounterObserved,
            averagePromptTokensPerSecond = totals.AveragePromptTokensPerSecond,
            averageGeneratedTokensPerSecond = totals.AverageGeneratedTokensPerSecond,
            requestCount = totals.RequestCount,
            successfulRequestCount = totals.SuccessfulRequestCount,
            failedRequestCount = totals.FailedRequestCount,
            requestCounterObserved = totals.RequestCounterObserved,
            averageInputTokensPerRequest = totals.AverageInputTokensPerRequest,
            averageGeneratedTokensPerRequest = totals.AverageGeneratedTokensPerRequest,
            requestSuccessRate = totals.RequestSuccessRate
        };

    private static object DayView(UsageMetricDay day)
        => new
        {
            date = day.Date.ToString("yyyy-MM-dd"),
            totals = TotalsView(day.Totals),
            isTracked = day.IsTracked
        };

    private static object DimensionView(UsageMetricDimension dimension)
        => new { id = dimension.Id, name = dimension.Name };

    private static string Query(LocalControlRequest request, string name, string fallback)
        => request.Query.TryGetValue(name, out var value) ? value.Trim() : fallback;

    private static bool AcceptedRange(string value)
        => value.Trim().ToLowerInvariant() is "1" or "1d" or "day" or "today"
            or "7" or "7d" or "week"
            or "month" or "current-month" or "currentmonth"
            or "30" or "30d"
            or "90" or "90d" or "quarter"
            or "all" or "lifetime";

    private static bool TryParseDates(
        string value,
        out IReadOnlyList<DateOnly> dates,
        out string error)
    {
        dates = [];
        error = "";
        if (string.IsNullOrWhiteSpace(value)) return true;
        var parsed = new List<DateOnly>();
        foreach (var item in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!DateOnly.TryParseExact(
                    item,
                    "yyyy-MM-dd",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var date))
            {
                error = $"Invalid date '{item}'. Dates must use YYYY-MM-DD.";
                return false;
            }
            parsed.Add(date);
        }
        dates = UsageMetricsService.NormalizeDates(parsed);
        if (dates.Count <= 366) return true;
        error = "At most 366 exact dates may be selected.";
        dates = [];
        return false;
    }

    private static TimeZoneInfo ResolveTimeZone(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return TimeZoneInfo.Local;
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(value);
        }
        catch (TimeZoneNotFoundException)
        {
            throw new InvalidOperationException($"Time zone '{value}' was not found.");
        }
        catch (InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"Time zone '{value}' is invalid.");
        }
    }
}
