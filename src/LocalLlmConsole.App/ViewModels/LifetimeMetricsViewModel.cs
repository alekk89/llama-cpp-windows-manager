using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed record LifetimeMetricFilterOption(string Id, string Label)
{
    public override string ToString() => Label;
}

public sealed record LifetimeMetricsSelection(
    UsageMetricsRange Range,
    string ModelId,
    string LaunchProfileId,
    string RuntimeId,
    IReadOnlyList<DateOnly> Dates,
    UsageCalendarMetric CalendarMetric = UsageCalendarMetric.TotalTokens)
{
    public static LifetimeMetricsSelection Default { get; } = new(UsageMetricsRange.All, "", "", "", []);

    public UsageMetricsQuery Query => new(Range, ModelId, LaunchProfileId, RuntimeId, Dates);
}

public sealed record LifetimeMetricsPresentation(
    string Total,
    string Input,
    string InputDetail,
    string Output,
    string CacheHit,
    string CacheDetail,
    string Requests,
    string RequestsDetail,
    string ActiveDays,
    string AveragePerActiveDay,
    string PromptRate,
    string GenerationRate,
    string PeakDay,
    string PeakDayDetail,
    string HistoryNote,
    string DateSelectionSummary,
    IReadOnlyList<UsageMetricDay> CalendarDays,
    IReadOnlyList<LifetimeMetricFilterOption> ModelOptions,
    IReadOnlyList<LifetimeMetricFilterOption> ProfileOptions,
    IReadOnlyList<LifetimeMetricFilterOption> RuntimeOptions,
    LifetimeMetricsSelection Selection,
    bool HasRows,
    bool HasDateSelection);

public sealed class LifetimeMetricsViewModel
{
    public ObservableCollection<UiRow> Rows { get; } = new();

    public LifetimeMetricsSelection Selection { get; private set; } = LifetimeMetricsSelection.Default;

    public void SetSelection(LifetimeMetricsSelection selection)
        => Selection = selection ?? LifetimeMetricsSelection.Default;

    public LifetimeMetricsPresentation ReplaceReport(UsageMetricsReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        Rows.Clear();
        foreach (var row in report.Models)
        {
            var tracked = row.CacheStatistics;
            Rows.Add(new UiRow
            {
                C1 = row.ModelName,
                C2 = RequestCount(tracked),
                C3 = row.Totals.InputTokens.ToString("N0"),
                C4 = row.Totals.CachedPromptTokens.ToString("N0"),
                C5 = row.Totals.GeneratedTokens.ToString("N0"),
                C6 = row.Totals.TotalTokens.ToString("N0"),
                C7 = Share(tracked.TotalTokens, report.TrackedSummary.TotalTokens),
                C8 = Rate(tracked.AverageGeneratedTokensPerSecond),
                C9 = Loc.T("Lifetime.ResetButton"),
                T1 = Loc.T("Lifetime.ResetModelTooltip", row.ModelName),
                B1 = true,
                Data = new JsonObject
                {
                    ["ModelId"] = row.ModelId,
                    ["ModelName"] = row.ModelName,
                    ["Kind"] = "model"
                }
            });
        }

        Selection = new LifetimeMetricsSelection(
            report.Query.Range,
            Existing(report.Query.ModelId, report.Dimensions.Models),
            Existing(report.Query.LaunchProfileId, report.Dimensions.LaunchProfiles),
            Existing(report.Query.RuntimeId, report.Dimensions.Runtimes),
            UsageMetricsService.NormalizeDates(report.Query.Dates),
            Selection.CalendarMetric);
        return new LifetimeMetricsPresentation(
            report.Summary.TotalTokens.ToString("N0"),
            report.Summary.InputTokens.ToString("N0"),
            Loc.T("Lifetime.InputDetail", report.Summary.PromptTokens.ToString("N0"), report.Summary.CachedPromptTokens.ToString("N0")),
            report.Summary.GeneratedTokens.ToString("N0"),
            Percent(report.TrackedSummary.CacheHitRate),
            CacheDetail(report.TrackedSummary),
            RequestCount(report.TrackedSummary),
            RequestDetail(report.TrackedSummary),
            report.Insights.ActiveDays.ToString("N0"),
            report.Insights.ActiveDays == 0
                ? Loc.T("Lifetime.NotAvailable")
                : report.Insights.AverageTokensPerActiveDay.ToString("N0"),
            Rate(report.TrackedSummary.AveragePromptTokensPerSecond),
            Rate(report.TrackedSummary.AverageGeneratedTokensPerSecond),
            report.Insights.PeakDate?.ToString("d MMM") ?? Loc.T("Lifetime.NotAvailable"),
            report.Insights.PeakDate is null
                ? Loc.T("Lifetime.NoTrackedActivity")
                : Loc.T("Lifetime.PeakDayDetail", report.Insights.PeakTotals.TotalTokens.ToString("N0")),
            HistoryNote(report),
            DateSelectionSummary(Selection.Dates),
            report.CalendarDays,
            Options(report.Dimensions.Models, Loc.T("Lifetime.Filter.AllModels")),
            Options(report.Dimensions.LaunchProfiles, Loc.T("Lifetime.Filter.AllProfiles")),
            Options(report.Dimensions.Runtimes, Loc.T("Lifetime.Filter.AllRuntimes")),
            Selection,
            report.Models.Count > 0,
            Selection.Dates.Count > 0);
    }

    public void ReplaceRows(IReadOnlyList<TokenUsageRecord> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        var service = new UsageMetricsService();
        var now = DateTimeOffset.UtcNow;
        var report = service.BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.All),
            [],
            rows,
            UsageMetricDimensions.Empty,
            now,
            TimeZoneInfo.Local);
        ReplaceReport(report);
    }

    private static IReadOnlyList<LifetimeMetricFilterOption> Options(
        IReadOnlyList<UsageMetricDimension> dimensions,
        string allLabel)
        => new[] { new LifetimeMetricFilterOption("", allLabel) }
            .Concat(dimensions.Select(value => new LifetimeMetricFilterOption(value.Id, value.Name)))
            .ToArray();

    private static string Existing(string selected, IReadOnlyList<UsageMetricDimension> dimensions)
        => string.IsNullOrWhiteSpace(selected)
           || dimensions.Any(value => value.Id.Equals(selected, StringComparison.OrdinalIgnoreCase))
            ? selected
            : "";

    private static string Percent(double? rate)
        => rate is null ? Loc.T("Lifetime.NotAvailable") : rate.Value.ToString("P1");

    private static string Rate(double? rate)
        => rate is null ? Loc.T("Lifetime.NotAvailable") : Loc.T("Lifetime.TokensPerSecond", rate.Value.ToString("N1"));

    private static string RequestCount(UsageMetricTotals totals)
        => totals.RequestCounterObserved
            ? totals.RequestCount.ToString("N0")
            : Loc.T("Lifetime.NotAvailable");

    private static string RequestDetail(UsageMetricTotals totals)
        => totals.RequestCounterObserved
            ? Loc.T(
                "Lifetime.RequestDetail",
                totals.SuccessfulRequestCount.ToString("N0"),
                totals.FailedRequestCount.ToString("N0"))
            : Loc.T("Lifetime.RequestUnavailable");

    private static string Share(long value, long total)
        => total <= 0 ? Loc.T("Lifetime.NotAvailable") : (value / (double)total).ToString("P1");

    private static string CacheDetail(UsageMetricTotals tracked)
        => tracked.CacheHitRate is null
            ? Loc.T("Lifetime.CacheUnavailable")
            : Loc.T("Lifetime.CacheDetail", tracked.CachedPromptTokens.ToString("N0"), tracked.InputTokens.ToString("N0"));

    private static string HistoryNote(UsageMetricsReport report)
    {
        if (report.TrackingStartedAt is null)
            return Loc.T("Lifetime.HistoryStartsAfterUsage");
        var local = report.TrackingStartedAt.Value.ToLocalTime().ToString("d");
        return report.IncludesLegacyTotals
            ? Loc.T("Lifetime.LegacyHistoryNote", local)
            : Loc.T("Lifetime.TrackingSince", local);
    }

    private static string DateSelectionSummary(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0) return Loc.T("Lifetime.Selection.None");
        if (dates.Count == 1)
            return Loc.T("Lifetime.Selection.One", dates[0].ToString("D"));

        var ordered = dates.Order().ToArray();
        var contiguous = ordered[^1].DayNumber - ordered[0].DayNumber + 1 == ordered.Length;
        return contiguous
            ? Loc.T(
                "Lifetime.Selection.Range",
                ordered.Length,
                ordered[0].ToString("d MMM"),
                ordered[^1].ToString("d MMM yyyy"))
            : Loc.T("Lifetime.Selection.Many", ordered.Length);
    }
}
