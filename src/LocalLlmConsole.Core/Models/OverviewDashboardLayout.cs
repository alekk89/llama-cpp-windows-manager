namespace LocalLlmConsole.Models;

public static class OverviewDashboardMetricIds
{
    public const string ModelStatus = "overview.model-status";
    public const string Cpu = "overview.hardware.cpu";
    public const string CpuTemperature = "overview.hardware.cpu.temperature";
    public const string CpuCoreClock = "overview.hardware.cpu.core-clock";
    public const string Ram = "overview.hardware.ram";
    public const string RamUsed = "overview.hardware.ram.used";
    public const string RamClock = "overview.hardware.ram.clock";
    public const string GpuPrefix = "overview.hardware.gpu:";
    public const string GpuVramPrefix = "overview.hardware.gpu-vram:";
    public const string GpuPowerPrefix = "overview.hardware.gpu-power:";
    public const string GpuCoreClockPrefix = "overview.hardware.gpu-core-clock:";
    public const string GpuTemperaturePrefix = "overview.hardware.gpu-temperature:";
    public const string GpuVramTemperaturePrefix = "overview.hardware.gpu-vram-temperature:";
    public const string GpuMemoryClockPrefix = "overview.hardware.gpu-memory-clock:";
    public const string GpuMemoryActivityPrefix = "overview.hardware.gpu-memory-activity:";
    public const string GpuFanSpeedPrefix = "overview.hardware.gpu-fan-speed:";
    public const string GpuPowerLimitPrefix = "overview.hardware.gpu-power-limit:";
    public const string GpuThrottlingPrefix = "overview.hardware.gpu-throttling:";
    public const string ServerProcessCpu = "overview.runtime.process.cpu";
    public const string ServerProcessMemory = "overview.runtime.process.memory";
    public const string ObservedGpuEnergyTotal = "overview.hardware.gpu-energy-live.total";
    public const string ObservedGpuEnergyPrefix = "overview.hardware.gpu-energy-live:";
    public const string ObservedGpuElectricityCostTotal = "overview.hardware.gpu-electricity-cost-live.total";
    public const string ObservedGpuElectricityCostPrefix = "overview.hardware.gpu-electricity-cost-live:";

    // Version 6 IDs. Version 7 migrates these session-oriented names to the
    // app-live observed-energy names above without discarding saved cards.
    public const string LegacySessionGpuEnergyTotal = "overview.runtime.gpu-energy.total";
    public const string LegacySessionGpuEnergyPrefix = "overview.runtime.gpu-energy:";
    public const string LegacySessionGpuElectricityCostTotal = "overview.runtime.gpu-electricity-cost.total";
    public const string LegacySessionGpuElectricityCostPrefix = "overview.runtime.gpu-electricity-cost:";
    public const string ActiveSlots = "overview.runtime.slots.active";
    public const string QueuedRequests = "overview.runtime.requests.queued";
    public const string BusyDecodeSlots = "overview.runtime.slots.busy-decode";
    public const string GenerationRate = "overview.runtime.tokens.generation-rate";
    public const string PromptRate = "overview.runtime.tokens.prompt-rate";
    public const string AverageGenerationRate = "overview.runtime.tokens.generation-average";
    public const string AveragePromptRate = "overview.runtime.tokens.prompt-average";
    public const string GeneratedTokens = "overview.runtime.tokens.generated-total";
    public const string PromptTokens = "overview.runtime.tokens.prompt-total";
    public const string MtpGeneratedRate = "overview.runtime.mtp.generated-rate";
    public const string MtpAcceptedRate = "overview.runtime.mtp.accepted-rate";
    public const string AverageMtpGeneratedRate = "overview.runtime.mtp.generated-average";
    public const string AverageMtpAcceptedRate = "overview.runtime.mtp.accepted-average";
    public const string MtpGeneratedTokens = "overview.runtime.mtp.generated-total";
    public const string MtpAcceptedTokens = "overview.runtime.mtp.accepted-total";
    public const string KvCacheUsed = "overview.runtime.kv-cache.used";
    public const string KvCacheCapacity = "overview.runtime.kv-cache.capacity";
    public const string KvCacheUsage = "overview.runtime.kv-cache.usage";
    public const string KvCacheAllocation = "overview.runtime.kv-cache.allocation";
    public const string RecentGenerationRate = "overview.runtime.tokens.generation-recent";
    public const string RecentPromptRate = "overview.runtime.tokens.prompt-recent";
    public const string PromptCacheReuse = "overview.runtime.prompt-cache.reuse";
    public const string DraftAcceptance = "overview.runtime.mtp.acceptance";
    public const string PeakContextUsed = "overview.runtime.context.peak";
    public const string ContextShifts = "overview.runtime.context.shifts";
    public const string GatewayTimeToFirstData = "overview.gateway.time-to-first-data";
    public const string GatewayRequestDuration = "overview.gateway.request-duration";
    public const string GatewayResponseThroughput = "overview.gateway.response-throughput";
    public const string GatewayRequests = "overview.gateway.requests";
    public const string GatewayFailures = "overview.gateway.failures";
    public const string GatewayFailureRate = "overview.gateway.failure-rate";
    public const string PrometheusPrefix = "prometheus:";

    // Version 1 layout IDs. They are accepted only by the v1-to-v2 migration.
    public const string LegacyHardware = "overview.hardware";
    public const string LegacySlots = "overview.slots";
    public const string LegacyTokens = "overview.tokens";
    public const string LegacyMtpTokens = "overview.mtp-tokens";
    public const string LegacyKvCache = "overview.kv-cache";

    public static string Gpu(int index)
        => IndexedGpuMetric(GpuPrefix, index);

    public static bool TryParseGpu(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, GpuPrefix, out index);

    public static string GpuVram(int index)
        => IndexedGpuMetric(GpuVramPrefix, index);

    public static bool TryParseGpuVram(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, GpuVramPrefix, out index);

    public static string GpuPower(int index)
        => IndexedGpuMetric(GpuPowerPrefix, index);

    public static bool TryParseGpuPower(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, GpuPowerPrefix, out index);

    public static string GpuCoreClock(int index)
        => IndexedGpuMetric(GpuCoreClockPrefix, index);

    public static bool TryParseGpuCoreClock(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, GpuCoreClockPrefix, out index);

    public static string GpuTemperature(int index)
        => IndexedGpuMetric(GpuTemperaturePrefix, index);

    public static bool TryParseGpuTemperature(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, GpuTemperaturePrefix, out index);

    public static string GpuVramTemperature(int index)
        => IndexedGpuMetric(GpuVramTemperaturePrefix, index);

    public static bool TryParseGpuVramTemperature(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, GpuVramTemperaturePrefix, out index);

    public static string GpuMemoryClock(int index) => IndexedGpuMetric(GpuMemoryClockPrefix, index);
    public static bool TryParseGpuMemoryClock(string metricId, out int index) => TryParseIndexedGpuMetric(metricId, GpuMemoryClockPrefix, out index);
    public static string GpuMemoryActivity(int index) => IndexedGpuMetric(GpuMemoryActivityPrefix, index);
    public static bool TryParseGpuMemoryActivity(string metricId, out int index) => TryParseIndexedGpuMetric(metricId, GpuMemoryActivityPrefix, out index);
    public static string GpuFanSpeed(int index) => IndexedGpuMetric(GpuFanSpeedPrefix, index);
    public static bool TryParseGpuFanSpeed(string metricId, out int index) => TryParseIndexedGpuMetric(metricId, GpuFanSpeedPrefix, out index);
    public static string GpuPowerLimit(int index) => IndexedGpuMetric(GpuPowerLimitPrefix, index);
    public static bool TryParseGpuPowerLimit(string metricId, out int index) => TryParseIndexedGpuMetric(metricId, GpuPowerLimitPrefix, out index);
    public static string GpuThrottling(int index) => IndexedGpuMetric(GpuThrottlingPrefix, index);
    public static bool TryParseGpuThrottling(string metricId, out int index) => TryParseIndexedGpuMetric(metricId, GpuThrottlingPrefix, out index);

    public static string ObservedGpuEnergy(int index)
        => IndexedGpuMetric(ObservedGpuEnergyPrefix, index);

    public static bool TryParseObservedGpuEnergy(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, ObservedGpuEnergyPrefix, out index);

    public static bool IsObservedGpuEnergyMetric(string metricId)
        => string.Equals(metricId, ObservedGpuEnergyTotal, StringComparison.Ordinal)
           || TryParseObservedGpuEnergy(metricId, out _);

    public static string ObservedGpuElectricityCost(int index)
        => IndexedGpuMetric(ObservedGpuElectricityCostPrefix, index);

    public static bool TryParseObservedGpuElectricityCost(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, ObservedGpuElectricityCostPrefix, out index);

    public static bool IsObservedGpuElectricityCostMetric(string metricId)
        => string.Equals(metricId, ObservedGpuElectricityCostTotal, StringComparison.Ordinal)
           || TryParseObservedGpuElectricityCost(metricId, out _);

    public static bool IsObservedGpuMetric(string metricId)
        => IsObservedGpuEnergyMetric(metricId) || IsObservedGpuElectricityCostMetric(metricId);

    public static bool TryParseLegacySessionGpuEnergy(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, LegacySessionGpuEnergyPrefix, out index);

    public static bool TryParseLegacySessionGpuElectricityCost(string metricId, out int index)
        => TryParseIndexedGpuMetric(metricId, LegacySessionGpuElectricityCostPrefix, out index);

    public static bool IsGpuMetric(string metricId)
        => TryParseGpu(metricId, out _)
           || TryParseGpuVram(metricId, out _)
           || TryParseGpuPower(metricId, out _)
           || TryParseGpuCoreClock(metricId, out _)
           || TryParseGpuTemperature(metricId, out _)
           || TryParseGpuVramTemperature(metricId, out _)
           || TryParseGpuMemoryClock(metricId, out _)
           || TryParseGpuMemoryActivity(metricId, out _)
           || TryParseGpuFanSpeed(metricId, out _)
           || TryParseGpuPowerLimit(metricId, out _)
           || TryParseGpuThrottling(metricId, out _);

    private static string IndexedGpuMetric(string prefix, int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        return $"{prefix}{index}";
    }

    private static bool TryParseIndexedGpuMetric(string metricId, string prefix, out int index)
    {
        index = -1;
        return !string.IsNullOrWhiteSpace(metricId)
               && metricId.StartsWith(prefix, StringComparison.Ordinal)
               && int.TryParse(metricId[prefix.Length..], NumberStyles.None, CultureInfo.InvariantCulture, out index)
               && index is >= 0 and < 16;
    }

    public static string Prometheus(string name, string labels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return $"{PrometheusPrefix}{Uri.EscapeDataString(name.Trim())}|{Uri.EscapeDataString(labels?.Trim() ?? "")}";
    }

    public static bool TryParsePrometheus(string metricId, out string name, out string labels)
    {
        name = "";
        labels = "";
        if (string.IsNullOrWhiteSpace(metricId)
            || !metricId.StartsWith(PrometheusPrefix, StringComparison.Ordinal))
            return false;

        var payload = metricId[PrometheusPrefix.Length..];
        var separator = payload.IndexOf('|');
        if (separator < 1) return false;
        try
        {
            name = Uri.UnescapeDataString(payload[..separator]);
            labels = Uri.UnescapeDataString(payload[(separator + 1)..]);
            return !string.IsNullOrWhiteSpace(name);
        }
        catch (UriFormatException)
        {
            name = "";
            labels = "";
            return false;
        }
    }
}

public enum OverviewDashboardCardHeight
{
    Compact,
    Standard,
    Tall
}

/// <summary>
/// Persisted free-form dashboard bounds. Horizontal values use a responsive
/// twelve-unit surface; vertical values use device-independent pixels.
/// </summary>
public sealed record OverviewDashboardCardBounds(
    double X,
    double Y,
    double Width,
    double Height);

public sealed record OverviewDashboardCardLayout(
    string Id,
    IReadOnlyList<string> MetricIds,
    int ColumnSpan = 1,
    OverviewDashboardCardHeight Height = OverviewDashboardCardHeight.Standard,
    string ChartMetricId = "",
    OverviewDashboardCardBounds? Bounds = null,
    IReadOnlyList<string>? ChartMetricIds = null,
    string Title = "");

public sealed record OverviewDashboardLayout(
    int Version,
    IReadOnlyList<OverviewDashboardCardLayout> Cards,
    bool CardSizesLocked = false,
    double LockedSurfaceWidth = 0);

public sealed record OverviewDashboardLegacyVisibility(
    bool ModelStatus,
    bool Hardware,
    bool Slots,
    bool Tokens,
    bool MtpTokens,
    bool KvCache);
