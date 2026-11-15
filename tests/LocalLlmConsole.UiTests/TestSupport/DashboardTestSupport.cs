using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

/// <summary>A clock that only moves when a test advances it.</summary>
public sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;
}

public abstract partial class WpfUiTestBase
{
    protected const string DashboardRuntimeKey = "model|runtime|8081";

    protected static OverviewDashboardController SingleCardDashboard(
        IReadOnlyList<string> metricIds,
        TimeProvider? clock = null,
        string cardId = "card")
    {
        var layout = new OverviewDashboardLayout(
            OverviewDashboardLayoutPolicy.CurrentVersion,
            [new OverviewDashboardCardLayout(cardId, metricIds, Bounds: new OverviewDashboardCardBounds(0, 0, 6, 240))]);
        var dashboard = new OverviewDashboardController(
            layout,
            new OverviewDashboardControllerActions(_ => Task.CompletedTask, action => action()),
            clock);
        dashboard.DashboardGrid.Measure(new Size(900, 800));
        dashboard.DashboardGrid.Arrange(new Rect(0, 0, 900, 800));
        dashboard.DashboardGrid.UpdateLayout();
        return dashboard;
    }

    protected static RuntimeMetricSummaryPresentation DashboardSummary(
        RuntimeMetricAtomicSnapshot atomic,
        DateTimeOffset? lastKnownCapturedAt = null)
        => new(
            "",
            "",
            "",
            "",
            lastKnownCapturedAt,
            new RuntimeMetricGraphSample(DashboardRuntimeKey, atomic.AverageGenerationRate, atomic.AveragePromptRate,
                null, null, atomic.KvCacheUsagePercent),
            [],
            atomic);

    protected static RuntimeMetricAtomicSnapshot DashboardAtomic(
        double generationRate = 35,
        double queuedRequests = 0,
        double? kvCacheUsagePercent = 50,
        double? draftAcceptancePercent = null,
        double? contextShiftCount = null)
        => RuntimeMetricAtomicSnapshot.Empty with
        {
            AverageGenerationRate = generationRate,
            AveragePromptRate = 17.5,
            SlotCapacity = 1,
            QueuedRequests = queuedRequests,
            KvCacheUsagePercent = kvCacheUsagePercent,
            DraftAcceptancePercent = draftAcceptancePercent,
            ContextShiftCount = contextShiftCount
        };

    protected static OverviewDashboardMetricRowView DashboardRow(OverviewDashboardController dashboard, string metricId)
        => dashboard.Cards.SelectMany(card => card.MetricRows.Values).Single(row => row.MetricId == metricId);

    protected static Grid DashboardValueLine(OverviewDashboardMetricRowView row)
        => row.Root.Children.OfType<Grid>().Single();

    protected static IReadOnlyList<string> DashboardRowTexts(OverviewDashboardMetricRowView row)
        => VisualDescendants<TextBlock>(row.Root)
            .Where(text => text.IsVisible || text.Visibility == Visibility.Visible)
            .Select(text => text.Text)
            .ToArray();
}
