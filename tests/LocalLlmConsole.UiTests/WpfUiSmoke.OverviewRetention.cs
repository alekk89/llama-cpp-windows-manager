using LocalLlmConsole.Models;

namespace LocalLlmConsole.UiTests;

public sealed partial class WpfUiSmokeTests
{
    private static void AssertOverviewSurfaceRetention(
        LocalLlmConsole.OverviewPageState state,
        LocalLlmConsole.OverviewPageControls overview,
        LocalLlmConsole.OverviewDashboardController dashboard,
        AppSettings settings)
    {
        Assert.True(state.IsAvailable);
        Assert.Same(overview.Scroller, state.Scroller);
        Assert.Equal(320, overview.RuntimeLogBox.Height);
        Assert.Equal(24, overview.RuntimeLogBox.MaxLines);
        var retainedCard = dashboard.Cards[0];
        var equivalentLayout = dashboard.Layout with
        {
            Cards = dashboard.Layout.Cards.Select(card => card with
            {
                MetricIds = card.MetricIds.ToArray(),
                ChartMetricIds = card.ChartMetricIds?.ToArray()
            }).ToArray()
        };

        state.ApplyUiPreferences(settings with { OverviewDashboardLayout = equivalentLayout });

        Assert.Same(retainedCard, dashboard.Cards[0]);
    }
}
