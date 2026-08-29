using static LocalLlmConsole.Services.RuntimeMetricSummaryCalculations;

namespace LocalLlmConsole.Services;

public sealed partial class RuntimeMetricSummaryTracker
{
    private static RuntimeCapacityObservation ObserveCapacity(
        IReadOnlyList<PrometheusSample> samples,
        RuntimeSlotSnapshot? slotSnapshot,
        AppSettings metricsSettings)
    {
        var kvUsage = RuntimeMetrics.First(samples, ["kv", "cache", "usage"], []);
        var kvTokens = RuntimeMetrics.Sum(samples, ["kv", "cache", "tokens"], [])
            ?? RuntimeMetrics.Sum(samples, ["kv", "tokens"], [])
            ?? slotSnapshot?.ContextTokens;
        var contextSize = RuntimeMetrics.First(samples, ["context", "size"], [])
            ?? RuntimeMetrics.First(samples, ["ctx", "size"], [])
            ?? slotSnapshot?.ContextSize
            ?? (metricsSettings.ContextSize > 0 ? (double?)metricsSettings.ContextSize : null);
        var contextCapacityTokens = slotSnapshot?.ContextCapacityTokens
            ?? (metricsSettings.ContextSize > 0 ? metricsSettings.ContextSize : contextSize);
        var kvUsagePercent = RuntimeDashboardService.KvCacheUsagePercent(
            kvUsage,
            kvTokens,
            contextCapacityTokens);
        var activeSlots = RuntimeMetrics.First(samples, ["requests", "processing"], [])
            ?? SlotProcessingCount(slotSnapshot)
            ?? 0;
        var queuedRequests = RuntimeMetrics.First(samples, ["requests", "deferred"], []) ?? 0;
        var busyDecodeSlots = RuntimeMetrics.First(samples, ["busy", "slots", "decode"], [])
            ?? RuntimeMetrics.First(samples, ["n", "busy", "slots", "per", "decode"], [])
            ?? SlotProcessingCount(slotSnapshot)
            ?? 0;
        var slotCapacity = Math.Max(
            Math.Max(metricsSettings.ParallelSlots, slotSnapshot?.SlotCounters?.Count ?? 0),
            (int)Math.Ceiling(Math.Max(0, activeSlots)));
        return new RuntimeCapacityObservation(
            kvTokens,
            contextCapacityTokens,
            kvUsagePercent,
            activeSlots,
            queuedRequests,
            busyDecodeSlots,
            slotCapacity,
            RuntimeDashboardService.RuntimeSlotsLabel(samples, slotSnapshot, metricsSettings.ParallelSlots),
            RuntimeDashboardService.RuntimeKvCacheLabel(
                kvUsage,
                kvTokens,
                contextCapacityTokens,
                metricsSettings.KvUnified));
    }

    private sealed record RuntimeCapacityObservation(
        double? KvTokens,
        double? ContextCapacityTokens,
        double? KvUsagePercent,
        double ActiveSlots,
        double QueuedRequests,
        double BusyDecodeSlots,
        int SlotCapacity,
        string SlotsText,
        string SettingsText);
}
