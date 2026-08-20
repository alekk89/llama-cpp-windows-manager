using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task UsageMetricsMigrationPreservesExistingLifetimeTotals()
    {
        var root = CreateTempRoot();
        var database = Path.Combine(root, "state", "local-llm-console.db");
        Directory.CreateDirectory(Path.GetDirectoryName(database)!);
        await using (var connection = new SqliteConnection($"Data Source={database}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
CREATE TABLE migrations (id INTEGER PRIMARY KEY, name TEXT NOT NULL, applied_at TEXT NOT NULL);
INSERT INTO migrations VALUES (1, 'baseline-v1', '2026-01-01T00:00:00Z');
INSERT INTO migrations VALUES (2, 'named-model-launch-profiles', '2026-01-01T00:00:00Z');
INSERT INTO migrations VALUES (3, 'real-default-model-launch-profiles', '2026-01-01T00:00:00Z');
INSERT INTO migrations VALUES (4, 'model-groups-and-retention-priority', '2026-01-01T00:00:00Z');
INSERT INTO migrations VALUES (5, 'launch-profile-group-assignments', '2026-01-01T00:00:00Z');
CREATE TABLE token_usage (
  model_id TEXT PRIMARY KEY,
  model_name TEXT NOT NULL,
  prompt_tokens INTEGER NOT NULL DEFAULT 0,
  generated_tokens INTEGER NOT NULL DEFAULT 0,
  updated_at TEXT NOT NULL
);
INSERT INTO token_usage VALUES ('legacy', 'Legacy Model', 120, 30, '2026-01-01T00:00:00Z');
""";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var store = new StateStore(database);
        await store.InitializeAsync();
        var existing = Assert.Single(await store.ListTokenUsageAsync());
        await store.RecordTokenUsageAsync(new TokenUsageDelta(
            "legacy", "Legacy Model", 10, 5, 4, true, new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero),
            PromptSeconds: 2, GeneratedSeconds: 1, TimingCounterObserved: true,
            RequestCount: 1, RequestCounterObserved: true));
        var updated = Assert.Single(await store.ListTokenUsageAsync());
        var migratedBucket = Assert.Single(await store.ListTokenUsageBucketsAsync());

        Assert.Equal(120, existing.PromptTokens);
        Assert.Equal(30, existing.GeneratedTokens);
        Assert.Equal(0, existing.CachedPromptTokens);
        Assert.False(existing.CacheCounterObserved);
        Assert.Equal(130, updated.PromptTokens);
        Assert.Equal(35, updated.GeneratedTokens);
        Assert.Equal(4, updated.CachedPromptTokens);
        Assert.True(updated.CacheCounterObserved);
        Assert.Equal(2, migratedBucket.PromptSeconds);
        Assert.Equal(1, migratedBucket.GeneratedSeconds);
        Assert.True(migratedBucket.TimingCounterObserved);
        Assert.Equal(1, migratedBucket.RequestCount);
        Assert.True(migratedBucket.RequestCounterObserved);
    }

    [Fact]
    public async Task UsageMetricsPersistHourlyDimensionsAndCacheStatisticsAtomically()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var service = new LifetimeMetricsApplicationService(store);
        var first = new DateTimeOffset(2026, 8, 18, 10, 12, 0, TimeSpan.Zero);
        var second = first.AddMinutes(35);
        var third = first.AddHours(2);

        await service.AddUsageAsync(Delta(first, 100, 40, 20));
        await service.AddUsageAsync(Delta(second, 30, 10, 5));
        await service.AddUsageAsync(Delta(third, 12, 8, 4));

        var lifetime = Assert.Single(await service.ListAsync());
        var buckets = await store.ListTokenUsageBucketsAsync();
        var dimensions = await store.ListTokenUsageDimensionsAsync();
        var report = await service.GetReportAsync(
            new UsageMetricsQuery(UsageMetricsRange.All),
            TimeZoneInfo.Utc,
            third.AddMinutes(1));

        Assert.Equal(142, lifetime.PromptTokens);
        Assert.Equal(58, lifetime.GeneratedTokens);
        Assert.Equal(29, lifetime.CachedPromptTokens);
        Assert.True(lifetime.CacheCounterObserved);
        Assert.Equal(2, buckets.Count);
        Assert.Equal(130, buckets[0].PromptTokens);
        Assert.Equal(25, buckets[0].CachedPromptTokens);
        Assert.Equal(50, buckets[0].GeneratedTokens);
        Assert.Equal(13, buckets[0].PromptSeconds);
        Assert.Equal(10, buckets[0].GeneratedSeconds);
        Assert.Equal(2, buckets[0].RequestCount);
        Assert.Equal(1, buckets[0].FailedRequestCount);
        Assert.Equal("profile-1", Assert.Single(dimensions.LaunchProfiles).Id);
        Assert.Equal("runtime-1", Assert.Single(dimensions.Runtimes).Id);
        Assert.Equal(229, report.Summary.TotalTokens);
        Assert.Equal(29 / 171d, report.TrackedSummary.CacheHitRate!.Value, 8);
        Assert.Equal(10, report.TrackedSummary.AveragePromptTokensPerSecond);
        Assert.Equal(5, report.TrackedSummary.AverageGeneratedTokensPerSecond);
        Assert.Equal(3, report.TrackedSummary.RequestCount);
        Assert.Equal(1, report.TrackedSummary.FailedRequestCount);
        Assert.Equal(2d / 3d, report.TrackedSummary.RequestSuccessRate!.Value, 8);
        Assert.Equal(1, report.Insights.ActiveDays);
        Assert.Equal(new DateOnly(2026, 8, 18), report.Insights.PeakDate);
        Assert.Equal(229, report.Insights.PeakTotals.TotalTokens);
        Assert.Equal(229, report.Insights.AverageTokensPerActiveDay);

        static TokenUsageDelta Delta(DateTimeOffset at, long prompt, long generated, long cached)
            => new(
                "model-1",
                "Model One",
                prompt,
                generated,
                cached,
                CacheCounterObserved: true,
                CapturedAt: at,
                LaunchProfileId: "profile-1",
                LaunchProfileName: "Coding",
                RuntimeId: "runtime-1",
                RuntimeName: "Vulkan",
                RuntimeMode: RuntimeMode.Native,
                RuntimeBackend: RuntimeBackend.Vulkan,
                PromptSeconds: prompt / 10d,
                GeneratedSeconds: generated / 5d,
                TimingCounterObserved: true,
                RequestCount: 1,
                FailedRequestCount: generated == 10 ? 1 : 0,
                RequestCounterObserved: true);
    }

    [Fact]
    public void UsageMetricsAggregateIntoLocalCalendarDaysAndFillEmptyDates()
    {
        var service = new UsageMetricsService();
        var timeZone = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var buckets = new[]
        {
            Bucket(new DateTimeOffset(2026, 8, 18, 22, 0, 0, TimeSpan.Zero), 10, 5, 2),
            Bucket(new DateTimeOffset(2026, 8, 20, 8, 0, 0, TimeSpan.Zero), 20, 10, 4)
        };

        var report = service.BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.SevenDays),
            buckets,
            [],
            UsageMetricDimensions.Empty,
            now,
            timeZone);

        Assert.Equal(7, report.Days.Count);
        Assert.Equal(new DateOnly(2026, 8, 14), report.Days[0].Date);
        Assert.Equal(17, report.Days.Single(day => day.Date == new DateOnly(2026, 8, 19)).Totals.TotalTokens);
        Assert.Equal(34, report.Days.Single(day => day.Date == new DateOnly(2026, 8, 20)).Totals.TotalTokens);
        Assert.Equal(51, report.Summary.TotalTokens);

        static UsageMetricBucket Bucket(DateTimeOffset at, long prompt, long output, long cached)
            => new(at, "model", "Model", "profile", "Profile", "runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, prompt, cached, output, true, at);
    }

    [Fact]
    public void UsageMetricsWindowUsesCalendarMidnightsAcrossDaylightSavingChanges()
    {
        var service = new UsageMetricsService();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        var now = new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.Zero);

        var window = service.ResolveWindow(UsageMetricsRange.SevenDays, now, timeZone);

        Assert.Equal(new DateOnly(2026, 3, 23), window.FirstLocalDate);
        Assert.Equal(new DateOnly(2026, 3, 29), window.LastLocalDate);
        Assert.Equal(TimeSpan.FromHours(167), window.ToUtc - window.FromUtc!.Value);
    }

    [Fact]
    public void UsageMetricsOneDayUsesTheCurrentLocalCalendarDay()
    {
        var service = new UsageMetricsService();
        var timeZone = TimeZoneInfo.FindSystemTimeZoneById("GMT Standard Time");
        var now = new DateTimeOffset(2026, 8, 20, 23, 30, 0, TimeSpan.Zero);

        var window = service.ResolveWindow(UsageMetricsRange.OneDay, now, timeZone);

        Assert.Equal(new DateOnly(2026, 8, 21), window.FirstLocalDate);
        Assert.Equal(window.FirstLocalDate, window.LastLocalDate);
        Assert.Equal(TimeSpan.FromHours(24), window.ToUtc - window.FromUtc!.Value);
    }

    [Fact]
    public void UsageMetricsCurrentMonthIncludesEveryCalendarDay()
    {
        var service = new UsageMetricsService();
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        var window = service.ResolveWindow(UsageMetricsRange.CurrentMonth, now, TimeZoneInfo.Utc);
        var report = service.BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.CurrentMonth),
            [],
            [],
            UsageMetricDimensions.Empty,
            now,
            TimeZoneInfo.Utc);

        Assert.Equal(new DateOnly(2026, 8, 1), window.FirstLocalDate);
        Assert.Equal(new DateOnly(2026, 8, 31), window.LastLocalDate);
        Assert.Equal(new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero), window.FromUtc);
        Assert.Equal(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero), window.ToUtc);
        Assert.Equal(31, report.Days.Count);
        Assert.Equal(new DateOnly(2026, 8, 31), report.Days[^1].Date);
        Assert.All(report.Days, day => Assert.Equal(0, day.Totals.TotalTokens));
    }

    [Fact]
    public void UsageMetricsCalendarSpansTwentyFourMonthsWithoutInventingTrackedHistory()
    {
        var service = new UsageMetricsService();
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var trackingStarted = new DateTimeOffset(2026, 7, 15, 8, 0, 0, TimeSpan.Zero);
        var bucket = new UsageMetricBucket(
            new DateTimeOffset(2026, 7, 15, 9, 0, 0, TimeSpan.Zero),
            "model", "Model", "profile", "Profile", "runtime", "Runtime",
            RuntimeMode.Native, RuntimeBackend.Cpu, 10, 2, 5, true, now);

        var report = service.BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.CurrentMonth),
            [bucket],
            [],
            UsageMetricDimensions.Empty,
            now,
            TimeZoneInfo.Utc,
            trackingStarted);

        Assert.Equal(new DateOnly(2024, 9, 1), report.CalendarWindow.FirstLocalDate);
        Assert.Equal(new DateOnly(2026, 8, 31), report.CalendarWindow.LastLocalDate);
        Assert.Equal(730, report.CalendarDays.Count);
        Assert.False(report.CalendarDays.Single(day => day.Date == new DateOnly(2026, 7, 14)).IsTracked);
        Assert.True(report.CalendarDays.Single(day => day.Date == new DateOnly(2026, 7, 15)).IsTracked);
        Assert.False(report.CalendarDays.Single(day => day.Date == new DateOnly(2026, 8, 21)).IsTracked);
    }

    [Fact]
    public void UsageMetricsExactDatesFilterTotalsWithoutFillingGaps()
    {
        var service = new UsageMetricsService();
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var buckets = new[]
        {
            DayBucket(18, 10),
            DayBucket(19, 20),
            DayBucket(20, 30)
        };
        var dates = new[] { new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 20) };

        var report = service.BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.CurrentMonth, Dates: dates),
            buckets,
            [],
            UsageMetricDimensions.Empty,
            now,
            TimeZoneInfo.Utc);

        Assert.Equal(2, report.Days.Count);
        Assert.Equal(dates, report.Days.Select(day => day.Date));
        Assert.Equal(40, report.Summary.PromptTokens);
        Assert.Equal(40, Assert.Single(report.Models).Totals.PromptTokens);

        UsageMetricBucket DayBucket(int day, long prompt)
        {
            var at = new DateTimeOffset(2026, 8, day, 8, 0, 0, TimeSpan.Zero);
            return new UsageMetricBucket(
                at, "model", "Model", "profile", "Profile", "runtime", "Runtime",
                RuntimeMode.Native, RuntimeBackend.Cpu, prompt, 0, 0, false, at);
        }
    }

    [Fact]
    public void UsageDateSelectionSupportsToggleAndAnchoredRanges()
    {
        var service = new UsageDateSelectionService();
        var dates = Enumerable.Range(1, 10).Select(day => new DateOnly(2026, 8, day)).ToArray();

        var selection = service.Apply(UsageDateSelection.Empty, dates[1], UsageDateSelectionMode.Replace, dates);
        selection = service.Apply(selection, dates[4], UsageDateSelectionMode.Toggle, dates);
        Assert.Equal(new[] { dates[1], dates[4] }, selection.Dates);

        selection = service.Apply(selection, dates[8], UsageDateSelectionMode.Range, dates);
        Assert.Equal(dates[4..9], selection.Dates);

        selection = service.Apply(selection, dates[1], UsageDateSelectionMode.AddRange, dates);
        Assert.Equal(dates[1..9], selection.Dates);

        selection = service.Apply(new UsageDateSelection([dates[3]], dates[3]), dates[3], UsageDateSelectionMode.Replace, dates);
        Assert.Empty(selection.Dates);
        Assert.Null(selection.Anchor);
    }

    [Theory]
    [InlineData("1d", UsageMetricsRange.OneDay)]
    [InlineData("today", UsageMetricsRange.OneDay)]
    [InlineData("month", UsageMetricsRange.CurrentMonth)]
    [InlineData("current-month", UsageMetricsRange.CurrentMonth)]
    [InlineData("30d", UsageMetricsRange.ThirtyDays)]
    public void UsageMetricsRangeParserKeepsMonthAndRollingThirtyDaysDistinct(
        string value,
        UsageMetricsRange expected)
        => Assert.Equal(expected, UsageMetricsService.ParseRange(value));

    [Fact]
    public void UsageMetricsKeepLegacyLifetimeTotalsOutOfDailyHistoryAndFilteredProfiles()
    {
        var service = new UsageMetricsService();
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        var bucket = new UsageMetricBucket(
            now.AddHours(-2), "model", "Model", "profile", "Coding", "runtime", "CUDA",
            RuntimeMode.Native, RuntimeBackend.Cuda, 10, 5, 3, true, now.AddHours(-2));
        var lifetime = new[] { new TokenUsageRecord("model", "Model", 110, 23, now, 5, true) };

        var all = service.BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.All), [bucket], lifetime, UsageMetricDimensions.Empty, now, TimeZoneInfo.Utc);
        var profile = service.BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.All, LaunchProfileId: "profile"), [bucket], lifetime, UsageMetricDimensions.Empty, now, TimeZoneInfo.Utc);

        Assert.True(all.IncludesLegacyTotals);
        Assert.Equal(138, all.Summary.TotalTokens);
        Assert.Equal(18, all.TrackedSummary.TotalTokens);
        Assert.Equal(18, profile.Summary.TotalTokens);
        Assert.False(profile.IncludesLegacyTotals);
    }

    [Fact]
    public void LifetimeCounterTrackerTracksCacheTimingAndRequestCounterResetsWithoutInventingUsage()
    {
        var tracker = new RuntimeLifetimeCounterTracker();
        var at = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);

        Assert.False(tracker.Observe(
            "runtime", "model", "Model", 10, 20, null, 5, at,
            generatedSecondsCounter: 2, promptSecondsCounter: 4, requestCounter: 1, failedRequestCounter: 0).HasActivity);
        var delta = tracker.Observe(
            "runtime", "model", "Model", 14, 28, null, 9, at.AddSeconds(2),
            generatedSecondsCounter: 3, promptSecondsCounter: 6, requestCounter: 3, failedRequestCounter: 1);
        var reset = tracker.Observe(
            "runtime", "model", "Model", 1, 2, null, 1, at.AddSeconds(4),
            generatedSecondsCounter: .5, promptSecondsCounter: .5, requestCounter: 0, failedRequestCounter: 0);

        Assert.Equal(4, delta.GeneratedTokens);
        Assert.Equal(8, delta.PromptTokens);
        Assert.Equal(4, delta.CachedPromptTokens);
        Assert.True(delta.CacheCounterObserved);
        Assert.Equal(2, delta.PromptSeconds);
        Assert.Equal(1, delta.GeneratedSeconds);
        Assert.True(delta.TimingCounterObserved);
        Assert.Equal(2, delta.RequestCount);
        Assert.Equal(1, delta.FailedRequestCount);
        Assert.True(delta.RequestCounterObserved);
        Assert.False(reset.HasActivity);
    }

    [Fact]
    public void ControlCliBuildsHistoricalMetricsQueryWithoutChangingLiveMetricsCommand()
    {
        var live = LocalLlmConsole.ControlCli.Program.BuildRequestForTests("metrics");
        var history = LocalLlmConsole.ControlCli.Program.BuildRequestForTests(
            "metrics", "usage", "--range", "90d", "--model", "model one", "--profile", "coding", "--runtime", "cuda");

        Assert.Equal("/api/v1/metrics", live.Path);
        Assert.Equal("GET", history.Method);
        Assert.Contains("/api/v1/metrics/usage?", history.Path, StringComparison.Ordinal);
        Assert.Contains("range=90d", history.Path, StringComparison.Ordinal);
        Assert.Contains("model=model%20one", history.Path, StringComparison.Ordinal);
        Assert.Contains("profile=coding", history.Path, StringComparison.Ordinal);
        Assert.Contains("runtime=cuda", history.Path, StringComparison.Ordinal);
    }

    [Fact]
    public void ControlCliBuildsExactDateUsageQuery()
    {
        var request = LocalLlmConsole.ControlCli.Program.BuildRequestForTests(
            "metrics", "usage", "--date", "2026-08-18", "--date", "2026-08-20");

        Assert.Contains("dates=2026-08-18%2C2026-08-20", request.Path, StringComparison.Ordinal);
    }
}
