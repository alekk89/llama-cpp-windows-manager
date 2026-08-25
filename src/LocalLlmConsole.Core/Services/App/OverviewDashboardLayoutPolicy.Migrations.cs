namespace LocalLlmConsole.Services;

public static partial class OverviewDashboardLayoutPolicy
{
    private static IEnumerable<string> ExpandMetricId(string? metricId, int layoutVersion)
    {
        if (string.IsNullOrWhiteSpace(metricId)) return [];
        metricId = MigrateObservedEnergyMetricId(metricId, layoutVersion);
        if (layoutVersion < CuratedMetricsLayoutVersion
            && string.Equals(metricId, OverviewDashboardMetricIds.AverageMtpAcceptedRate, StringComparison.Ordinal))
            metricId = OverviewDashboardMetricIds.DraftAcceptance;
        if (layoutVersion >= AtomicMetricLayoutVersion) return [metricId];

        return metricId switch
        {
            OverviewDashboardMetricIds.LegacyHardware => HardwareMetricIds,
            OverviewDashboardMetricIds.LegacySlots => SlotMetricIds,
            OverviewDashboardMetricIds.LegacyTokens => TokenMetricIds,
            OverviewDashboardMetricIds.LegacyMtpTokens => MtpMetricIds,
            OverviewDashboardMetricIds.LegacyKvCache => KvCacheMetricIds,
            _ => [metricId]
        };
    }

    private static string MigrateChartMetricId(string? metricId, int layoutVersion)
    {
        if (string.IsNullOrWhiteSpace(metricId)) return "";
        if (layoutVersion < CuratedMetricsLayoutVersion
            && string.Equals(metricId, OverviewDashboardMetricIds.AverageMtpAcceptedRate, StringComparison.Ordinal))
            metricId = OverviewDashboardMetricIds.DraftAcceptance;
        if (layoutVersion < AverageRateLayoutVersion)
        {
            metricId = metricId switch
            {
                OverviewDashboardMetricIds.GenerationRate => OverviewDashboardMetricIds.AverageGenerationRate,
                OverviewDashboardMetricIds.PromptRate => OverviewDashboardMetricIds.AveragePromptRate,
                OverviewDashboardMetricIds.MtpGeneratedRate => OverviewDashboardMetricIds.AverageMtpGeneratedRate,
                OverviewDashboardMetricIds.MtpAcceptedRate => OverviewDashboardMetricIds.AverageMtpAcceptedRate,
                _ => metricId
            };
        }
        if (layoutVersion >= AtomicMetricLayoutVersion) return metricId;
        return metricId switch
        {
            OverviewDashboardMetricIds.LegacyTokens => OverviewDashboardMetricIds.AverageGenerationRate,
            OverviewDashboardMetricIds.LegacyMtpTokens => OverviewDashboardMetricIds.AverageMtpGeneratedRate,
            OverviewDashboardMetricIds.LegacyKvCache => OverviewDashboardMetricIds.KvCacheUsage,
            _ => metricId
        };
    }
}
