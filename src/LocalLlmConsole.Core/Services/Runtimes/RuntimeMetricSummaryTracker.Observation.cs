using static LocalLlmConsole.Services.RuntimeMetricSummaryCalculations;
using static LocalLlmConsole.Services.RuntimeSlotAggregateTracker;

namespace LocalLlmConsole.Services;

public sealed partial class RuntimeMetricSummaryTracker
{
    private static RuntimeMetricObservation ObserveCurrentPoll(
        RuntimeMetricSummaryState state,
        IReadOnlyList<PrometheusSample> samples,
        AppSettings metricsSettings,
        RuntimeSlotSnapshot? slotSnapshot,
        RuntimeMtpTokenSnapshot? mtpTokenSnapshot,
        DateTimeOffset now)
    {
        var predictedTokens = RuntimeDashboardService.GeneratedTokenCounter(samples);
        var predictedSeconds = RuntimeMetrics.Sum(samples, ["tokens", "predicted", "seconds", "total"], [])
            ?? RuntimeMetrics.Sum(samples, ["tokens", "generated", "seconds", "total"], [])
            ?? RuntimeMetrics.Sum(samples, ["eval", "time"], ["prompt"]);
        var promptTokensProcessed = RuntimeDashboardService.PromptTokensProcessedCounter(samples);
        var promptTokensCached = RuntimeDashboardService.PromptCachedTokenCounter(samples);
        var promptCacheActivity = RuntimeDashboardService.SumNullable(promptTokensProcessed, promptTokensCached);
        var promptSeconds = RuntimeMetrics.Sum(samples, ["prompt", "seconds", "total"], [])
            ?? RuntimeMetrics.Sum(samples, ["prompt", "time"], []);
        var slotObservation = ObserveSlots(state, slotSnapshot, now);
        var observedMtpGeneratedTokens = RuntimeDashboardService.MtpGeneratedTokenCounter(samples)
            ?? mtpTokenSnapshot?.GeneratedTokens
            ?? slotObservation.MtpGeneratedTokens;
        var observedMtpAcceptedTokens = RuntimeDashboardService.MtpAcceptedTokenCounter(samples)
            ?? mtpTokenSnapshot?.AcceptedTokens
            ?? slotObservation.MtpAcceptedTokens;
        var mtpGeneratedSeconds = RuntimeDashboardService.MtpGeneratedSecondsCounter(samples)
            ?? mtpTokenSnapshot?.GeneratedSeconds;
        var mtpAcceptedSeconds = RuntimeDashboardService.MtpAcceptedSecondsCounter(samples)
            ?? mtpTokenSnapshot?.AcceptedSeconds
            ?? mtpGeneratedSeconds;

        var liveGenerationRate = CounterRateAndRemember(
            predictedTokens,
            ref state.LastPredictedTokenCounter,
            ref state.LastPredictedTokenPollAt,
            now);
        var livePromptRate = CounterRateAndRemember(
            promptTokensProcessed,
            ref state.LastPromptTokenCounter,
            ref state.LastPromptTokenPollAt,
            now);
        var liveMtpGeneratedRate = CounterRateAndRemember(
            observedMtpGeneratedTokens,
            ref state.LastMtpGeneratedTokenCounter,
            ref state.LastMtpGeneratedTokenPollAt,
            now);
        var liveMtpAcceptedRate = CounterRateAndRemember(
            observedMtpAcceptedTokens,
            ref state.LastMtpAcceptedTokenCounter,
            ref state.LastMtpAcceptedTokenPollAt,
            now);

        // Prefer rates based on active processing time so idle gaps do not dilute
        // throughput. Wall-clock and slot observations remain fallbacks.
        liveGenerationRate = SecondsBasedCounterRate(
                predictedTokens,
                predictedSeconds,
                ref state.LastPredictedTokenCounterForSeconds,
                ref state.LastPredictedSecondsCounter)
            ?? liveGenerationRate;
        livePromptRate = SecondsBasedCounterRate(
                promptTokensProcessed,
                promptSeconds,
                ref state.LastPromptTokenCounterForSeconds,
                ref state.LastPromptSecondsCounter)
            ?? livePromptRate;
        if (predictedTokens is null) liveGenerationRate = slotObservation.GenerationRate ?? liveGenerationRate;
        if (promptTokensProcessed is null) livePromptRate = slotObservation.PromptRate ?? livePromptRate;

        var reportedAverageGenerationRate = RuntimeMetrics.Sum(samples, ["predicted", "tokens", "second"], ["total"])
            ?? RuntimeMetrics.Sum(samples, ["generation", "tokens", "second"], ["total"]);
        var reportedAveragePromptRate = RuntimeMetrics.Sum(samples, ["prompt", "tokens", "second"], ["total"]);

        return new RuntimeMetricObservation
        {
            GeneratedTokens = predictedTokens ?? slotObservation.GeneratedTokens,
            PromptTokens = promptTokensProcessed ?? slotObservation.PromptTokens,
            MtpGeneratedTokens = observedMtpGeneratedTokens,
            MtpAcceptedTokens = observedMtpAcceptedTokens,
            LiveGenerationRate = liveGenerationRate,
            LivePromptRate = livePromptRate,
            LiveMtpGeneratedRate = liveMtpGeneratedRate,
            LiveMtpAcceptedRate = liveMtpAcceptedRate,
            AverageGenerationRate = RuntimeDashboardService.Rate(predictedTokens, predictedSeconds)
                ?? (reportedAverageGenerationRate is > 0 ? reportedAverageGenerationRate : null)
                ?? liveGenerationRate,
            AveragePromptRate = RuntimeDashboardService.Rate(promptTokensProcessed, promptSeconds)
                ?? (reportedAveragePromptRate is > 0 ? reportedAveragePromptRate : null)
                ?? livePromptRate,
            AverageMtpGeneratedRate = RuntimeDashboardService.Rate(observedMtpGeneratedTokens, mtpGeneratedSeconds),
            AverageMtpAcceptedRate = RuntimeDashboardService.Rate(observedMtpAcceptedTokens, mtpAcceptedSeconds),
            PromptTokensCached = promptTokensCached,
            PromptCacheReusePercent = promptTokensCached is { } cached && promptCacheActivity is > 0
                ? Math.Clamp(100 * cached / promptCacheActivity.Value, 0, 100)
                : null,
            DraftAcceptancePercent = observedMtpAcceptedTokens is { } accepted && observedMtpGeneratedTokens is > 0
                ? Math.Clamp(100 * accepted / observedMtpGeneratedTokens.Value, 0, 100)
                : null,
            PeakContextTokens = RuntimeDashboardService.PeakContextTokenCounter(samples),
            ContextShiftCount = RuntimeDashboardService.ContextShiftCounter(samples),
            Capacity = ObserveCapacity(samples, slotSnapshot, metricsSettings)
        };
    }

    private sealed class RuntimeMetricObservation
    {
        public double? GeneratedTokens { get; init; }
        public double? PromptTokens { get; init; }
        public double? MtpGeneratedTokens { get; init; }
        public double? MtpAcceptedTokens { get; init; }
        public double? LiveGenerationRate { get; init; }
        public double? LivePromptRate { get; init; }
        public double? LiveMtpGeneratedRate { get; init; }
        public double? LiveMtpAcceptedRate { get; init; }
        public double? AverageGenerationRate { get; init; }
        public double? AveragePromptRate { get; init; }
        public double? AverageMtpGeneratedRate { get; init; }
        public double? AverageMtpAcceptedRate { get; init; }
        public double? PromptTokensCached { get; init; }
        public double? PromptCacheReusePercent { get; init; }
        public double? DraftAcceptancePercent { get; init; }
        public double? PeakContextTokens { get; init; }
        public double? ContextShiftCount { get; init; }
        public required RuntimeCapacityObservation Capacity { get; init; }
    }
}
