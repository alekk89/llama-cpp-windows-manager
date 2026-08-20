using static LocalLlmConsole.Services.RuntimeSlotAggregateTracker;
using static LocalLlmConsole.Services.RuntimeMetricSummaryCalculations;

namespace LocalLlmConsole.Services;

public sealed record RuntimeMetricDisplaySnapshot(
    string RuntimeKey,
    IReadOnlyList<PrometheusSample> Samples,
    string Tokens,
    string GenerationRate,
    string TotalTokens,
    string MtpTokens,
    string Slots,
    string KvCache,
    DateTimeOffset CapturedAt,
    double? GeneratedTokens,
    double? PromptTokens,
    double? MtpGeneratedTokens,
    double? MtpAcceptedTokens,
    double? AverageGenerationRate,
    double? AveragePromptRate,
    double? AverageMtpGeneratedRate,
    double? AverageMtpAcceptedRate,
    DateTimeOffset? GeneratedTokensCapturedAt,
    DateTimeOffset? PromptTokensCapturedAt,
    DateTimeOffset? MtpGeneratedTokensCapturedAt,
    DateTimeOffset? MtpAcceptedTokensCapturedAt,
    DateTimeOffset? AverageGenerationRateCapturedAt,
    DateTimeOffset? AveragePromptRateCapturedAt,
    DateTimeOffset? AverageMtpGeneratedRateCapturedAt,
    DateTimeOffset? AverageMtpAcceptedRateCapturedAt);

public sealed record RuntimeMetricSummaryResult(
    string Tokens,
    string GenerationRate,
    string TotalTokens,
    string MtpTokens,
    string Slots,
    string KvCache,
    bool UsedLastKnown,
    DateTimeOffset? LastKnownCapturedAt,
    RuntimeMetricGraphSample GraphSample);

public sealed record RuntimeMetricGraphSample(
    string RuntimeKey,
    double? GenerationRate,
    double? PromptRate,
    double? SpeculativeGeneratedRate,
    double? SpeculativeAcceptedRate,
    double? KvCacheUsagePercent);

public sealed class RuntimeMetricSummaryTracker
{
    private readonly Dictionary<string, RuntimeMetricSummaryState> _states = new(StringComparer.Ordinal);

    public RuntimeMetricSummaryResult Apply(
        string runtimeKey,
        IReadOnlyList<PrometheusSample> samples,
        AppSettings metricsSettings,
        RuntimeSlotSnapshot? slotSnapshot,
        RuntimeMtpTokenSnapshot? mtpTokenSnapshot,
        DateTimeOffset? capturedAt = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(runtimeKey);
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(metricsSettings);

        var state = StateFor(runtimeKey);
        var previous = state.LastDisplay;

        if (samples.Count == 0
            && slotSnapshot is null
            && mtpTokenSnapshot is null
            && previous is { } snapshot)
        {
            return new RuntimeMetricSummaryResult(
                snapshot.Tokens,
                snapshot.GenerationRate,
                snapshot.TotalTokens,
                snapshot.MtpTokens,
                snapshot.Slots,
                snapshot.KvCache,
                UsedLastKnown: true,
                LastKnownCapturedAt(snapshot),
                new RuntimeMetricGraphSample(runtimeKey, null, null, null, null, null));
        }

        var now = capturedAt ?? DateTimeOffset.UtcNow;
        var predictedTokens = RuntimeDashboardService.GeneratedTokenCounter(samples);
        var predictedSeconds = RuntimeMetrics.Sum(samples, ["tokens", "predicted", "seconds", "total"], [])
            ?? RuntimeMetrics.Sum(samples, ["tokens", "generated", "seconds", "total"], [])
            ?? RuntimeMetrics.Sum(samples, ["eval", "time"], ["prompt"]);
        var promptTokensProcessed = RuntimeDashboardService.PromptTokensProcessedCounter(samples);
        var promptTokensCached = RuntimeDashboardService.PromptCachedTokenCounter(samples);
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

        var liveGenerationRate = CounterRateAndRemember(predictedTokens, ref state.LastPredictedTokenCounter, ref state.LastPredictedTokenPollAt, now);
        var livePromptRate = CounterRateAndRemember(promptTokensProcessed, ref state.LastPromptTokenCounter, ref state.LastPromptTokenPollAt, now);
        var liveMtpGeneratedRate = CounterRateAndRemember(observedMtpGeneratedTokens, ref state.LastMtpGeneratedTokenCounter, ref state.LastMtpGeneratedTokenPollAt, now);
        var liveMtpAcceptedRate = CounterRateAndRemember(observedMtpAcceptedTokens, ref state.LastMtpAcceptedTokenCounter, ref state.LastMtpAcceptedTokenPollAt, now);

        // Compute generation-time-based rates (uses actual active generation seconds, not wall clock).
        // This avoids dilution during idle gaps between requests where the wall-clock counter rate
        // would divide tokens by total elapsed time instead of active generation time.
        var secondsBasedGenerationRate = SecondsBasedCounterRate(predictedTokens, predictedSeconds, ref state.LastPredictedTokenCounterForSeconds, ref state.LastPredictedSecondsCounter);
        var secondsBasedPromptRate = SecondsBasedCounterRate(promptTokensProcessed, promptSeconds, ref state.LastPromptTokenCounterForSeconds, ref state.LastPromptSecondsCounter);
        // Prefer the seconds-based rate when available; fall back to wall-clock counter rate
        liveGenerationRate = secondsBasedGenerationRate ?? liveGenerationRate;
        livePromptRate = secondsBasedPromptRate ?? livePromptRate;

        if (predictedTokens is null) liveGenerationRate = slotObservation.GenerationRate ?? liveGenerationRate;
        if (promptTokensProcessed is null) livePromptRate = slotObservation.PromptRate ?? livePromptRate;

        var reportedAverageGenerationRate = RuntimeMetrics.Sum(samples, ["predicted", "tokens", "second"], ["total"])
            ?? RuntimeMetrics.Sum(samples, ["generation", "tokens", "second"], ["total"]);
        var reportedAveragePromptRate = RuntimeMetrics.Sum(samples, ["prompt", "tokens", "second"], ["total"]);
        var observedAverageGenerationRate = RuntimeDashboardService.Rate(predictedTokens, predictedSeconds)
            ?? (reportedAverageGenerationRate is > 0 ? reportedAverageGenerationRate : null)
            ?? liveGenerationRate;
        var observedAveragePromptRate = RuntimeDashboardService.Rate(promptTokensProcessed, promptSeconds)
            ?? (reportedAveragePromptRate is > 0 ? reportedAveragePromptRate : null)
            ?? livePromptRate;
        var observedAverageMtpGeneratedRate = RuntimeDashboardService.Rate(observedMtpGeneratedTokens, mtpGeneratedSeconds);
        var observedAverageMtpAcceptedRate = RuntimeDashboardService.Rate(observedMtpAcceptedTokens, mtpAcceptedSeconds);
        var displayAverageGenerationRate = observedAverageGenerationRate ?? previous?.AverageGenerationRate;
        var displayAveragePromptRate = observedAveragePromptRate ?? previous?.AveragePromptRate;
        var displayAverageMtpGeneratedRate = observedAverageMtpGeneratedRate ?? previous?.AverageMtpGeneratedRate;
        var displayAverageMtpAcceptedRate = observedAverageMtpAcceptedRate ?? previous?.AverageMtpAcceptedRate;
        var kvUsage = RuntimeMetrics.First(samples, ["kv", "cache", "usage"], []);
        var kvTokens = RuntimeMetrics.Sum(samples, ["kv", "cache", "tokens"], [])
            ?? RuntimeMetrics.Sum(samples, ["kv", "tokens"], []);
        var contextSize = RuntimeMetrics.First(samples, ["context", "size"], [])
            ?? RuntimeMetrics.First(samples, ["ctx", "size"], [])
            ?? slotSnapshot?.ContextSize
            ?? (metricsSettings.ContextSize > 0 ? (double?)metricsSettings.ContextSize : null);
        kvTokens ??= slotSnapshot?.ContextTokens;
        var contextCapacityTokens = slotSnapshot?.ContextCapacityTokens
            ?? (metricsSettings.ContextSize > 0 ? metricsSettings.ContextSize : contextSize);
        var kvUsagePercent = RuntimeDashboardService.KvCacheUsagePercent(kvUsage, kvTokens, contextCapacityTokens);

        var observedGeneratedTokens = predictedTokens ?? slotObservation.GeneratedTokens;
        var observedPromptTokens = promptTokensProcessed ?? slotObservation.PromptTokens;
        var displayGeneratedTokens = RuntimeDashboardService.MaxNullable(observedGeneratedTokens, previous?.GeneratedTokens);
        var displayPromptTokens = RuntimeDashboardService.MaxNullable(observedPromptTokens, previous?.PromptTokens);
        var displayMtpGeneratedTokens = RuntimeDashboardService.MaxNullable(observedMtpGeneratedTokens, previous?.MtpGeneratedTokens);
        var displayMtpAcceptedTokens = RuntimeDashboardService.MaxNullable(observedMtpAcceptedTokens, previous?.MtpAcceptedTokens);
        var usedPreviousGeneratedTokens = UsedPreviousCounter(observedGeneratedTokens, previous?.GeneratedTokens, displayGeneratedTokens);
        var usedPreviousPromptTokens = UsedPreviousCounter(observedPromptTokens, previous?.PromptTokens, displayPromptTokens);
        var usedPreviousMtpGeneratedTokens = UsedPreviousCounter(observedMtpGeneratedTokens, previous?.MtpGeneratedTokens, displayMtpGeneratedTokens);
        var usedPreviousMtpAcceptedTokens = UsedPreviousCounter(observedMtpAcceptedTokens, previous?.MtpAcceptedTokens, displayMtpAcceptedTokens);
        var usedPreviousAverageGenerationRate = UsedPreviousAverage(observedAverageGenerationRate, previous?.AverageGenerationRate);
        var usedPreviousAveragePromptRate = UsedPreviousAverage(observedAveragePromptRate, previous?.AveragePromptRate);
        var usedPreviousAverageMtpGeneratedRate = UsedPreviousAverage(observedAverageMtpGeneratedRate, previous?.AverageMtpGeneratedRate);
        var usedPreviousAverageMtpAcceptedRate = UsedPreviousAverage(observedAverageMtpAcceptedRate, previous?.AverageMtpAcceptedRate);
        var usedLastKnown = usedPreviousGeneratedTokens
            || usedPreviousPromptTokens
            || usedPreviousMtpGeneratedTokens
            || usedPreviousMtpAcceptedTokens
            || usedPreviousAverageGenerationRate
            || usedPreviousAveragePromptRate
            || usedPreviousAverageMtpGeneratedRate
            || usedPreviousAverageMtpAcceptedRate;
        var generatedTokensCapturedAt = DisplayValueCapturedAt(observedGeneratedTokens, displayGeneratedTokens, previous?.GeneratedTokensCapturedAt ?? previous?.CapturedAt, now);
        var promptTokensCapturedAt = DisplayValueCapturedAt(observedPromptTokens, displayPromptTokens, previous?.PromptTokensCapturedAt ?? previous?.CapturedAt, now);
        var mtpGeneratedTokensCapturedAt = DisplayValueCapturedAt(observedMtpGeneratedTokens, displayMtpGeneratedTokens, previous?.MtpGeneratedTokensCapturedAt ?? previous?.CapturedAt, now);
        var mtpAcceptedTokensCapturedAt = DisplayValueCapturedAt(observedMtpAcceptedTokens, displayMtpAcceptedTokens, previous?.MtpAcceptedTokensCapturedAt ?? previous?.CapturedAt, now);
        var averageGenerationRateCapturedAt = DisplayValueCapturedAt(observedAverageGenerationRate, displayAverageGenerationRate, previous?.AverageGenerationRateCapturedAt ?? previous?.CapturedAt, now);
        var averagePromptRateCapturedAt = DisplayValueCapturedAt(observedAveragePromptRate, displayAveragePromptRate, previous?.AveragePromptRateCapturedAt ?? previous?.CapturedAt, now);
        var averageMtpGeneratedRateCapturedAt = DisplayValueCapturedAt(observedAverageMtpGeneratedRate, displayAverageMtpGeneratedRate, previous?.AverageMtpGeneratedRateCapturedAt ?? previous?.CapturedAt, now);
        var averageMtpAcceptedRateCapturedAt = DisplayValueCapturedAt(observedAverageMtpAcceptedRate, displayAverageMtpAcceptedRate, previous?.AverageMtpAcceptedRateCapturedAt ?? previous?.CapturedAt, now);
        var lastKnownCapturedAt = OldestCapturedAt(
            usedPreviousGeneratedTokens ? generatedTokensCapturedAt : null,
            usedPreviousPromptTokens ? promptTokensCapturedAt : null,
            usedPreviousMtpGeneratedTokens ? mtpGeneratedTokensCapturedAt : null,
            usedPreviousMtpAcceptedTokens ? mtpAcceptedTokensCapturedAt : null,
            usedPreviousAverageGenerationRate ? averageGenerationRateCapturedAt : null,
            usedPreviousAveragePromptRate ? averagePromptRateCapturedAt : null,
            usedPreviousAverageMtpGeneratedRate ? averageMtpGeneratedRateCapturedAt : null,
            usedPreviousAverageMtpAcceptedRate ? averageMtpAcceptedRateCapturedAt : null);

        var generationRateText = $"Gen {RuntimeDashboardService.RateLabel(liveGenerationRate, displayAverageGenerationRate)}\nPrompt {RuntimeDashboardService.RateLabel(livePromptRate, displayAveragePromptRate)}";
        var totalTokensText = RuntimeDashboardService.TokenSummaryLabel(displayGeneratedTokens, displayPromptTokens);
        var tokensText = RuntimeDashboardService.TokenAverageAndTotalSummaryLabel(
            displayAverageGenerationRate,
            displayAveragePromptRate,
            displayGeneratedTokens,
            displayPromptTokens,
            promptTokensCached);
        var mtpTokensText = MtpTokensText(
            metricsSettings,
            liveMtpGeneratedRate,
            displayAverageMtpGeneratedRate,
            liveMtpAcceptedRate,
            displayAverageMtpAcceptedRate,
            displayMtpGeneratedTokens,
            displayMtpAcceptedTokens);
        var slotsText = RuntimeDashboardService.RuntimeSlotsLabel(samples, slotSnapshot, metricsSettings.ParallelSlots);
        var settingsText = RuntimeDashboardService.RuntimeKvCacheLabel(
            kvUsage,
            kvTokens,
            contextCapacityTokens,
            metricsSettings.KvUnified);
        var snapshotCapturedAt = usedLastKnown && previous is not null ? previous.CapturedAt : now;

        Remember(
            state,
            runtimeKey,
            samples,
            tokensText,
            generationRateText,
            totalTokensText,
            mtpTokensText,
            slotsText,
            settingsText,
            displayGeneratedTokens,
            displayPromptTokens,
            displayMtpGeneratedTokens,
            displayMtpAcceptedTokens,
            displayAverageGenerationRate,
            displayAveragePromptRate,
            displayAverageMtpGeneratedRate,
            displayAverageMtpAcceptedRate,
            generatedTokensCapturedAt,
            promptTokensCapturedAt,
            mtpGeneratedTokensCapturedAt,
            mtpAcceptedTokensCapturedAt,
            averageGenerationRateCapturedAt,
            averagePromptRateCapturedAt,
            averageMtpGeneratedRateCapturedAt,
            averageMtpAcceptedRateCapturedAt,
            snapshotCapturedAt);
        return new RuntimeMetricSummaryResult(
            tokensText,
            generationRateText,
            totalTokensText,
            mtpTokensText,
            slotsText,
            settingsText,
            usedLastKnown,
            usedLastKnown ? lastKnownCapturedAt : null,
            new RuntimeMetricGraphSample(
                runtimeKey,
                displayAverageGenerationRate,
                displayAveragePromptRate,
                liveMtpGeneratedRate ?? observedAverageMtpGeneratedRate,
                liveMtpAcceptedRate ?? observedAverageMtpAcceptedRate,
                kvUsagePercent));
    }

    public IReadOnlyList<PrometheusSample> LastKnownSamples(string runtimeKey)
        => _states.TryGetValue(runtimeKey, out var state)
           && state.LastDisplay is { Samples.Count: > 0 } snapshot
            ? snapshot.Samples
            : [];

    public void Reset()
    {
        _states.Clear();
    }

    private static void Remember(
        RuntimeMetricSummaryState state,
        string runtimeKey,
        IReadOnlyList<PrometheusSample> samples,
        string tokensText,
        string generationRateText,
        string totalTokensText,
        string mtpTokensText,
        string slotsText,
        string settingsText,
        double? displayGeneratedTokens,
        double? displayPromptTokens,
        double? displayMtpGeneratedTokens,
        double? displayMtpAcceptedTokens,
        double? averageGenerationRate,
        double? averagePromptRate,
        double? averageMtpGeneratedRate,
        double? averageMtpAcceptedRate,
        DateTimeOffset? generatedTokensCapturedAt,
        DateTimeOffset? promptTokensCapturedAt,
        DateTimeOffset? mtpGeneratedTokensCapturedAt,
        DateTimeOffset? mtpAcceptedTokensCapturedAt,
        DateTimeOffset? averageGenerationRateCapturedAt,
        DateTimeOffset? averagePromptRateCapturedAt,
        DateTimeOffset? averageMtpGeneratedRateCapturedAt,
        DateTimeOffset? averageMtpAcceptedRateCapturedAt,
        DateTimeOffset capturedAt)
    {
        if (displayGeneratedTokens is null
            && displayPromptTokens is null
            && displayMtpGeneratedTokens is null
            && displayMtpAcceptedTokens is null
            && averageGenerationRate is null
            && averagePromptRate is null
            && averageMtpGeneratedRate is null
            && averageMtpAcceptedRate is null
            && samples.Count == 0)
            return;

        var cachedSamples = samples.Count > 0
            ? samples.ToArray()
            : state.LastDisplay is { } previous
                ? previous.Samples
                : [];

        state.LastDisplay = new RuntimeMetricDisplaySnapshot(
            runtimeKey,
            cachedSamples,
            tokensText,
            generationRateText,
            totalTokensText,
            mtpTokensText,
            slotsText,
            settingsText,
            capturedAt,
            displayGeneratedTokens,
            displayPromptTokens,
            displayMtpGeneratedTokens,
            displayMtpAcceptedTokens,
            averageGenerationRate,
            averagePromptRate,
            averageMtpGeneratedRate,
            averageMtpAcceptedRate,
            generatedTokensCapturedAt,
            promptTokensCapturedAt,
            mtpGeneratedTokensCapturedAt,
            mtpAcceptedTokensCapturedAt,
            averageGenerationRateCapturedAt,
            averagePromptRateCapturedAt,
            averageMtpGeneratedRateCapturedAt,
            averageMtpAcceptedRateCapturedAt);
    }

    private static string MtpTokensText(
        AppSettings metricsSettings,
        double? liveGeneratedRate,
        double? averageGeneratedRate,
        double? liveAcceptedRate,
        double? averageAcceptedRate,
        double? generatedTotal,
        double? acceptedTotal)
    {
        if (generatedTotal is null && acceptedTotal is null && !MtpConfigured(metricsSettings))
            return "Inactive";

        return RuntimeDashboardService.MtpTokenSummaryLabel(
            liveGeneratedRate,
            averageGeneratedRate,
            liveAcceptedRate,
            averageAcceptedRate,
            generatedTotal,
            acceptedTotal);
    }

    private static bool MtpConfigured(AppSettings metricsSettings)
    {
        var speculativeType = SpeculativeTypePolicy.Normalize(metricsSettings.SpeculativeType);
        return speculativeType.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)
            || speculativeType.Contains("mtp", StringComparison.OrdinalIgnoreCase);
    }

    private RuntimeMetricSummaryState StateFor(string runtimeKey)
    {
        if (!_states.TryGetValue(runtimeKey, out var state))
        {
            state = new RuntimeMetricSummaryState();
            _states[runtimeKey] = state;
        }

        return state;
    }

}
