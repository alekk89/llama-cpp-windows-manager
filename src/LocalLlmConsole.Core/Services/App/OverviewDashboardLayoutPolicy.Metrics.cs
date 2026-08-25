namespace LocalLlmConsole.Services;

public static partial class OverviewDashboardLayoutPolicy
{
    private static bool IsChartableMetricId(string metricId)
        => !string.Equals(metricId, OverviewDashboardMetricIds.ModelStatus, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.KvCacheAllocation, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.KvCacheCapacity, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.RamClock, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.GeneratedTokens, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.PromptTokens, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.MtpGeneratedTokens, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.MtpAcceptedTokens, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.ContextShifts, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.GatewayRequests, StringComparison.Ordinal)
           && !string.Equals(metricId, OverviewDashboardMetricIds.GatewayFailures, StringComparison.Ordinal)
           && !OverviewDashboardMetricIds.TryParseGpuPowerLimit(metricId, out _)
           && !OverviewDashboardMetricIds.TryParseGpuThrottling(metricId, out _)
           && !OverviewDashboardMetricIds.IsObservedGpuMetric(metricId);

    private static string MigrateObservedEnergyMetricId(string metricId, int layoutVersion)
    {
        if (layoutVersion >= ObservedEnergyLayoutVersion) return metricId;
        if (string.Equals(metricId, OverviewDashboardMetricIds.LegacySessionGpuEnergyTotal, StringComparison.Ordinal))
            return OverviewDashboardMetricIds.ObservedGpuEnergyTotal;
        if (OverviewDashboardMetricIds.TryParseLegacySessionGpuEnergy(metricId, out var gpuIndex))
            return OverviewDashboardMetricIds.ObservedGpuEnergy(gpuIndex);
        if (string.Equals(metricId, OverviewDashboardMetricIds.LegacySessionGpuElectricityCostTotal, StringComparison.Ordinal))
            return OverviewDashboardMetricIds.ObservedGpuElectricityCostTotal;
        return OverviewDashboardMetricIds.TryParseLegacySessionGpuElectricityCost(metricId, out gpuIndex)
            ? OverviewDashboardMetricIds.ObservedGpuElectricityCost(gpuIndex)
            : metricId;
    }
}
