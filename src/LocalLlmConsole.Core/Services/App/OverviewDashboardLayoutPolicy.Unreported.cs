namespace LocalLlmConsole.Services;

/// <summary>Where a dashboard metric's value comes from.</summary>
public enum OverviewDashboardMetricSource
{
    Runtime,
    Hardware,
    Gateway
}

public static partial class OverviewDashboardLayoutPolicy
{
    private const string DefaultGpuCardPrefix = "default-gpu-";

    /// <summary>
    /// Returns true when a row may be hidden while its metric is not reported.
    /// That applies only to the optional sensors the app placed on its own
    /// generated cards, so default cards do not fill with empty rows on hardware
    /// without those sensors. Every metric a user added stays visible with a
    /// reason until the user removes it.
    /// </summary>
    public static bool HidesUnreportedMetric(string cardId, string metricId)
    {
        if (string.IsNullOrWhiteSpace(cardId) || string.IsNullOrWhiteSpace(metricId)) return false;
        IReadOnlyList<int> gpuIndices = [];
        if (cardId.StartsWith(DefaultGpuCardPrefix, StringComparison.OrdinalIgnoreCase))
        {
            if (!int.TryParse(cardId[DefaultGpuCardPrefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
                || index is < 0 or >= 16)
                return false;
            gpuIndices = [index];
        }

        var generated = CreateProductionDefault(gpuIndices).Cards
            .FirstOrDefault(card => string.Equals(card.Id, cardId, StringComparison.OrdinalIgnoreCase));
        return generated?.MetricIds.Contains(metricId, StringComparer.Ordinal) == true;
    }

    public static OverviewDashboardMetricSource MetricSource(string metricId)
    {
        if (metricId?.StartsWith("overview.hardware.", StringComparison.Ordinal) == true)
            return OverviewDashboardMetricSource.Hardware;
        if (metricId?.StartsWith("overview.gateway.", StringComparison.Ordinal) == true)
            return OverviewDashboardMetricSource.Gateway;
        return OverviewDashboardMetricSource.Runtime;
    }
}
