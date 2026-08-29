using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public abstract partial class WpfUiTestBase
{
    protected static void AssertLifetimeUsageSurface()
    {
        var rangeChanges = 0;
        var dateSelectionChanges = 0;
        var page = LocalLlmConsole.LifetimePageFactory.Create(new LocalLlmConsole.LifetimePageRequest(
            Array.Empty<LifetimeMetricRow>(),
            LifetimeMetricsSelection.Default,
            new LocalLlmConsole.LifetimePageActions(
                (_, _) => rangeChanges++,
                (_, _) => { },
                (_, _) => dateSelectionChanges++,
                (_, _) => { },
                (_, _) => { },
                (_, _) => { })));
        page.Content.Measure(new Size(900, 680));
        page.Content.Arrange(new Rect(0, 0, 900, 680));
        page.Content.UpdateLayout();

        Assert.Equal(UsageMetricsRange.All, page.Controls.RangeSelector.SelectedRange);
        Assert.Equal(
            ["1D", "7D", "30D", "All"],
            page.Controls.RangeSelector.Children.OfType<Button>().Select(button => button.Content).ToArray());
        Assert.Equal(6, page.Controls.CalendarMetric.Items.Count);
        Assert.NotNull(page.Controls.GpuEnergyValue);
        Assert.NotNull(page.Controls.GpuEnergyDetail);
        var energyReport = new UsageMetricsService().BuildReport(
            new UsageMetricsQuery(UsageMetricsRange.OneDay),
            [],
            [],
            UsageMetricDimensions.Empty,
            DateTimeOffset.UtcNow,
            TimeZoneInfo.Utc,
            energyBuckets:
            [
                new GpuEnergyBucket(DateTimeOffset.UtcNow.AddMinutes(-10), 1250, 600, true, 2, 2, DateTimeOffset.UtcNow)
            ],
            energyDeviceBuckets:
            [
                new GpuEnergyDeviceBucket(DateTimeOffset.UtcNow.AddMinutes(-10), "GPU 0: NVIDIA", 0, "NVIDIA", 750, 600, DateTimeOffset.UtcNow),
                new GpuEnergyDeviceBucket(DateTimeOffset.UtcNow.AddMinutes(-10), "GPU 1: AMD", 1, "AMD", 500, 600, DateTimeOffset.UtcNow)
            ],
            electricityTariff: new ElectricityTariff("GBP", .30, .30, new TimeOnly(0, 0), new TimeOnly(7, 0)));
        var lifetimeState = new LifetimePageState();
        lifetimeState.Apply(page.Controls);
        lifetimeState.ApplyPresentation(new LifetimeMetricsViewModel().ReplaceReport(energyReport));
        Assert.Contains("kWh", page.Controls.GpuEnergyValue.Text, StringComparison.Ordinal);
        Assert.Contains("GBP", page.Controls.GpuEnergyDetail.Text, StringComparison.Ordinal);
        Assert.Equal(UsageCalendarMetric.TotalTokens, page.Controls.HistoryCalendar.Metric);
        page.Controls.ClearDateSelectionButton.Visibility = Visibility.Visible;
        page.Content.UpdateLayout();
        Assert.Equal(32, page.Controls.ClearDateSelectionButton.ActualHeight, precision: 1);
        Assert.Equal(
            page.Controls.CalendarMetric.ActualHeight,
            page.Controls.ClearDateSelectionButton.ActualHeight,
            precision: 1);
        Assert.Equal(
            page.Controls.CalendarMetric.TranslatePoint(new Point(0, 0), page.Content).Y,
            page.Controls.ClearDateSelectionButton.TranslatePoint(new Point(0, 0), page.Content).Y,
            precision: 1);
        page.Controls.CalendarMetric.SelectedIndex = 3;
        Assert.Equal(UsageCalendarMetric.CachedPromptTokens, page.Controls.HistoryCalendar.Metric);
        page.Controls.RangeSelector.SetRange(UsageMetricsRange.SevenDays, raiseEvent: true);
        Assert.Equal(UsageMetricsRange.SevenDays, page.Controls.RangeSelector.SelectedRange);
        Assert.Equal(1, rangeChanges);

        var days = Enumerable.Range(0, 8)
                .Select(index => new UsageMetricDay(
                    DateOnly.FromDateTime(DateTime.Today).AddDays(index - 7),
                    new UsageMetricTotals(index + 1, index, index * 2, true),
                    IsTracked: index > 0))
                .ToArray();
        page.Controls.HistoryCalendar.SetData(days);
        page.Controls.HistoryCalendar.Metric = UsageCalendarMetric.Requests;
        page.Content.UpdateLayout();
        Assert.Equal(8, page.Controls.HistoryCalendar.DayCount);
        Assert.False(page.Controls.HistoryCalendar.ApplySelection(days[0].Date, UsageDateSelectionMode.Replace));
        Assert.True(page.Controls.HistoryCalendar.ApplySelection(days[1].Date, UsageDateSelectionMode.Replace));
        Assert.Single(page.Controls.HistoryCalendar.SelectedDates);
        Assert.True(page.Controls.HistoryCalendar.ApplySelection(days[1].Date, UsageDateSelectionMode.Replace));
        Assert.Empty(page.Controls.HistoryCalendar.SelectedDates);
        Assert.True(page.Controls.HistoryCalendar.ApplySelection(days[2].Date, UsageDateSelectionMode.Replace));
        Assert.True(page.Controls.HistoryCalendar.ApplySelection(days[4].Date, UsageDateSelectionMode.Toggle));
        Assert.True(page.Controls.HistoryCalendar.ApplySelection(days[6].Date, UsageDateSelectionMode.Range));
        Assert.Equal(days[4..7].Select(day => day.Date), page.Controls.HistoryCalendar.SelectedDates);
        Assert.Equal(5, dateSelectionChanges);
        var energyDay = days[0] with
        {
            GpuEnergy = new GpuEnergyTotals(1250, 3600, true, true, 1, 1)
        };
        page.Controls.HistoryCalendar.SetData([energyDay]);
        page.Controls.CalendarMetric.SelectedIndex = 5;
        Assert.Equal(UsageCalendarMetric.GpuEnergy, page.Controls.HistoryCalendar.Metric);
        Assert.True(page.Controls.HistoryCalendar.ApplySelection(energyDay.Date, UsageDateSelectionMode.Replace));
        Assert.Contains("calendar", AutomationProperties.GetName(page.Controls.HistoryCalendar), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ScrollBarVisibility.Disabled, page.Controls.HistoryScroller.HorizontalScrollBarVisibility);
        Assert.Equal(System.Windows.HorizontalAlignment.Stretch, page.Controls.HistoryScroller.HorizontalContentAlignment);
        Assert.Equal(190, page.Controls.ModelFilter.ActualWidth, precision: 1);
        Assert.Equal(170, page.Controls.ProfileFilter.ActualWidth, precision: 1);
        Assert.Equal(170, page.Controls.RuntimeFilter.ActualWidth, precision: 1);

        page.Content.Measure(new Size(624, 504));
        page.Content.Arrange(new Rect(0, 0, 624, 504));
        page.Content.UpdateLayout();
        var compactRangeContainer = Assert.IsAssignableFrom<FrameworkElement>(page.Controls.RangeSelector.Parent);
        var compactModelContainer = Assert.IsAssignableFrom<FrameworkElement>(page.Controls.ModelFilter.Parent);
        Assert.True(
            compactModelContainer.TranslatePoint(new Point(0, 0), page.Content).Y
            > compactRangeContainer.TranslatePoint(new Point(0, 0), page.Content).Y);
        Assert.All(
            new FrameworkElement[]
            {
                page.Controls.ModelFilter,
                page.Controls.ProfileFilter,
                page.Controls.RuntimeFilter,
                page.Controls.ResetVisibleButton
            },
            control =>
            {
                var point = control.TranslatePoint(new Point(0, 0), page.Content);
                Assert.InRange(point.X, 0, 624 - control.ActualWidth + 1);
            });

        var calendarDays = Enumerable.Range(0, 730)
            .Select(index => new UsageMetricDay(
                DateOnly.FromDateTime(DateTime.Today).AddDays(index - 729),
                new UsageMetricTotals(index + 1, index, index * 2, true),
                IsTracked: true))
            .ToArray();
        page.Controls.HistoryCalendar.SetData(calendarDays);
        page.Content.Measure(new Size(900, 680));
        page.Content.Arrange(new Rect(0, 0, 900, 680));
        page.Content.UpdateLayout();
        var compactCalendarWidth = page.Controls.HistoryCalendar.ActualWidth;
        var compactVisibleWeeks = page.Controls.HistoryCalendar.VisibleWeekCount;

        page.Content.Measure(new Size(2048, 1080));
        page.Content.Arrange(new Rect(0, 0, 2048, 1080));
        page.Content.UpdateLayout();
        Assert.Equal(
            compactRangeContainer.TranslatePoint(new Point(0, 0), page.Content).Y,
            compactModelContainer.TranslatePoint(new Point(0, 0), page.Content).Y,
            precision: 1);
        Assert.True(page.Controls.HistoryCalendar.ActualWidth > compactCalendarWidth);
        Assert.True(page.Controls.HistoryCalendar.VisibleWeekCount > compactVisibleWeeks);
        Assert.True(page.Controls.HistoryCalendar.VisibleWeekCount > 53);
        Assert.Equal(page.Controls.HistoryScroller.ViewportWidth, page.Controls.HistoryCalendar.ActualWidth, 1);
        lifetimeState.ReleaseView();
        Assert.Equal(LifetimeMetricsSelection.Default, lifetimeState.Selection);
    }
}

public sealed class WpfLifetimeMetricsTests : WpfUiTestBase
{
    [Fact]
    public async Task LifetimeMetricsRenderIndependently()
        => await RunStaAsync(AssertLifetimeUsageSurface);
}
