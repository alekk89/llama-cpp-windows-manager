namespace LocalLlmConsole;

public sealed partial class OverviewDashboardMetricRegistry
{
    private static IEnumerable<OverviewDashboardMetricDefinition> EfficiencyDefinitions()
    {
        // Most runtimes do not report verification steps, so the speed-up row
        // stays out of the picker until the runtime exposes that counter.
        yield return Explained(Metric(OverviewDashboardMetricIds.SpeculativeSpeedup, "SpeculativeSpeedup", "Core", true, "Accent",
            presentation: OverviewDashboardMetricPresentation.Count, requiresObservedValue: true), "Dashboard.Tooltip.SpeculativeSpeedup");
        yield return Explained(Metric(OverviewDashboardMetricIds.PromptCacheHitRate, "PromptCacheHitRate", "Core", true,
            FixedMaximum: 100, presentation: OverviewDashboardMetricPresentation.Percentage), "Dashboard.Tooltip.PromptCacheHitRate");
    }

    private static OverviewDashboardMetricDefinition Explained(OverviewDashboardMetricDefinition definition, string explanationKey)
        => definition with
        {
            Tooltip = $"{definition.DisplayName}\n{Loc.T(explanationKey)}\n{Loc.T("Dashboard.Tooltip.TechnicalMetric", definition.Id)}"
        };
}
