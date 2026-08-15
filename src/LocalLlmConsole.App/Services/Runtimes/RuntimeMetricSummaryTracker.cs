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

    private static SlotAggregateObservation ObserveSlots(
        RuntimeMetricSummaryState state,
        RuntimeSlotSnapshot? snapshot,
        DateTimeOffset now)
    {
        if (snapshot is null)
            return new SlotAggregateObservation(null, null, null, null, null, null);

        var counters = SlotCounters(snapshot);
        if (!state.SlotCountersInitialized)
        {
            state.CumulativeSlotPromptTokens = counters.Sum(counter => Math.Max(0, counter.PromptTokensProcessed));
            state.CumulativeSlotGeneratedTokens = counters.Sum(counter => Math.Max(0, counter.GeneratedTokens));
            state.CumulativeSlotMtpGeneratedTokens = SumOptional(counters.Select(counter => counter.MtpGeneratedTokens));
            state.CumulativeSlotMtpAcceptedTokens = SumOptional(counters.Select(counter => counter.MtpAcceptedTokens));
            state.SlotCountersInitialized = true;
            RememberSlotCounters(state, counters, now);
            return new SlotAggregateObservation(
                null,
                null,
                state.CumulativeSlotPromptTokens,
                state.CumulativeSlotGeneratedTokens,
                state.CumulativeSlotMtpGeneratedTokens,
                state.CumulativeSlotMtpAcceptedTokens);
        }

        double promptDelta = 0;
        double generationDelta = 0;
        double? mtpGeneratedDelta = null;
        double? mtpAcceptedDelta = null;
        foreach (var counter in counters)
        {
            var hadPrevious = state.LastSlotCounters.TryGetValue(counter.SlotId, out var previous);
            promptDelta += hadPrevious
                ? SlotCounterDelta(counter.PromptTokensProcessed, previous!.PromptTokensProcessed, counter.TaskId, previous.TaskId)
                : Math.Max(0, counter.PromptTokensProcessed);
            generationDelta += hadPrevious
                ? SlotCounterDelta(counter.GeneratedTokens, previous!.GeneratedTokens, counter.TaskId, previous.TaskId)
                : Math.Max(0, counter.GeneratedTokens);
            mtpGeneratedDelta = RuntimeDashboardService.SumNullable(
                mtpGeneratedDelta,
                OptionalSlotCounterDelta(counter.MtpGeneratedTokens, hadPrevious ? previous!.MtpGeneratedTokens : null, counter.TaskId, hadPrevious ? previous!.TaskId : null));
            mtpAcceptedDelta = RuntimeDashboardService.SumNullable(
                mtpAcceptedDelta,
                OptionalSlotCounterDelta(counter.MtpAcceptedTokens, hadPrevious ? previous!.MtpAcceptedTokens : null, counter.TaskId, hadPrevious ? previous!.TaskId : null));
        }

        state.CumulativeSlotPromptTokens += promptDelta;
        state.CumulativeSlotGeneratedTokens += generationDelta;
        state.CumulativeSlotMtpGeneratedTokens = RuntimeDashboardService.SumNullable(state.CumulativeSlotMtpGeneratedTokens, mtpGeneratedDelta);
        state.CumulativeSlotMtpAcceptedTokens = RuntimeDashboardService.SumNullable(state.CumulativeSlotMtpAcceptedTokens, mtpAcceptedDelta);
        var elapsed = state.LastSlotPollAt is { } previousPollAt ? (now - previousPollAt).TotalSeconds : 0;
        RememberSlotCounters(state, counters, now);
        return new SlotAggregateObservation(
            elapsed >= 0.25 ? promptDelta / elapsed : null,
            elapsed >= 0.25 ? generationDelta / elapsed : null,
            state.CumulativeSlotPromptTokens,
            state.CumulativeSlotGeneratedTokens,
            state.CumulativeSlotMtpGeneratedTokens,
            state.CumulativeSlotMtpAcceptedTokens);
    }

    private static IReadOnlyList<RuntimeSlotCounterSnapshot> SlotCounters(RuntimeSlotSnapshot snapshot)
        => snapshot.SlotCounters is { Count: > 0 } counters
            ? counters
            : [new RuntimeSlotCounterSnapshot("aggregate", "", snapshot.PromptTokensProcessed, snapshot.GeneratedTokens, snapshot.IsProcessing)];

    private static double SlotCounterDelta(double current, double previous, string currentTaskId, string previousTaskId)
    {
        if (current >= previous && string.Equals(currentTaskId, previousTaskId, StringComparison.Ordinal))
            return current - previous;

        return Math.Max(0, current);
    }

    private static double? OptionalSlotCounterDelta(double? current, double? previous, string currentTaskId, string? previousTaskId)
    {
        if (current is null) return null;
        if (previous is not null && current >= previous && string.Equals(currentTaskId, previousTaskId, StringComparison.Ordinal))
            return current - previous;
        return Math.Max(0, current.Value);
    }

    private static double? SumOptional(IEnumerable<double?> values)
    {
        double? total = null;
        foreach (var value in values)
            total = RuntimeDashboardService.SumNullable(total, value);
        return total;
    }

    private static void RememberSlotCounters(
        RuntimeMetricSummaryState state,
        IReadOnlyList<RuntimeSlotCounterSnapshot> counters,
        DateTimeOffset capturedAt)
    {
        foreach (var counter in counters)
        {
            state.LastSlotCounters[counter.SlotId] = new RuntimeSlotCounterState(
                counter.TaskId,
                counter.PromptTokensProcessed,
                counter.GeneratedTokens,
                counter.MtpGeneratedTokens,
                counter.MtpAcceptedTokens);
        }
        state.LastSlotPollAt = capturedAt;
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
        var speculativeType = LaunchSettingMetadataService.NormalizeSpeculativeType(metricsSettings.SpeculativeType);
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

    private static double? CounterRateAndRemember(
        double? current,
        ref double? previous,
        ref DateTimeOffset? previousPollAt,
        DateTimeOffset now)
    {
        var rate = RuntimeDashboardService.CounterRate(current, previous, now, previousPollAt, 0.5);
        if (current is not null)
        {
            previous = current;
            previousPollAt = now;
        }

        return rate;
    }

    /// <summary>Computes a live rate using active generation-time deltas instead of wall-clock time.
    /// This prevents rate dilution during idle gaps between requests.</summary>
    private static double? SecondsBasedCounterRate(
        double? currentTokens,
        double? currentSeconds,
        ref double? previousTokens,
        ref double? previousSeconds)
    {
        if (currentTokens is null || currentSeconds is null
            || previousTokens is null || previousSeconds is null
            || currentTokens.Value < previousTokens.Value
            || currentSeconds.Value <= previousSeconds.Value)
        {
            // Remember current values for next poll
            if (currentTokens is not null) previousTokens = currentTokens;
            if (currentSeconds is not null) previousSeconds = currentSeconds;
            return null;
        }

        var tokensDelta = currentTokens.Value - previousTokens.Value;
        var secondsDelta = currentSeconds.Value - previousSeconds.Value;
        previousTokens = currentTokens;
        previousSeconds = currentSeconds;

        return secondsDelta > 0 ? tokensDelta / secondsDelta : null;
    }

    private static bool UsedPreviousCounter(double? observed, double? previous, double? display)
        => previous is not null
           && display == previous
           && (observed is null || observed.Value < previous.Value);

    private static bool UsedPreviousAverage(double? observed, double? previous)
        => observed is null && previous is not null;

    private static DateTimeOffset? DisplayValueCapturedAt(
        double? observed,
        double? display,
        DateTimeOffset? previousCapturedAt,
        DateTimeOffset now)
    {
        if (display is null) return null;
        return observed is not null && observed.Value == display.Value ? now : previousCapturedAt;
    }

    private static DateTimeOffset? LastKnownCapturedAt(RuntimeMetricDisplaySnapshot snapshot)
        => OldestCapturedAt(
               snapshot.GeneratedTokensCapturedAt,
               snapshot.PromptTokensCapturedAt,
               snapshot.MtpGeneratedTokensCapturedAt,
               snapshot.MtpAcceptedTokensCapturedAt,
               snapshot.AverageGenerationRateCapturedAt,
               snapshot.AveragePromptRateCapturedAt,
               snapshot.AverageMtpGeneratedRateCapturedAt,
               snapshot.AverageMtpAcceptedRateCapturedAt)
           ?? snapshot.CapturedAt;

    private static DateTimeOffset? OldestCapturedAt(params DateTimeOffset?[] capturedAt)
    {
        DateTimeOffset? oldest = null;
        foreach (var timestamp in capturedAt)
        {
            if (timestamp is null) continue;
            if (oldest is null || timestamp.Value < oldest.Value)
                oldest = timestamp;
        }

        return oldest;
    }

    private sealed class RuntimeMetricSummaryState
    {
        public double? LastPredictedTokenCounter;
        public DateTimeOffset? LastPredictedTokenPollAt;
        public double? LastPredictedTokenCounterForSeconds;
        public double? LastPromptTokenCounter;
        public DateTimeOffset? LastPromptTokenPollAt;
        public double? LastPromptTokenCounterForSeconds;
        public double? LastPredictedSecondsCounter;
        public double? LastPromptSecondsCounter;
        public double? LastMtpGeneratedTokenCounter;
        public DateTimeOffset? LastMtpGeneratedTokenPollAt;
        public double? LastMtpAcceptedTokenCounter;
        public DateTimeOffset? LastMtpAcceptedTokenPollAt;
        public DateTimeOffset? LastSlotPollAt;
        public bool SlotCountersInitialized;
        public double CumulativeSlotPromptTokens;
        public double CumulativeSlotGeneratedTokens;
        public double? CumulativeSlotMtpGeneratedTokens;
        public double? CumulativeSlotMtpAcceptedTokens;
        public Dictionary<string, RuntimeSlotCounterState> LastSlotCounters { get; } = new(StringComparer.Ordinal);
        public RuntimeMetricDisplaySnapshot? LastDisplay;
    }

    private sealed record RuntimeSlotCounterState(
        string TaskId,
        double PromptTokensProcessed,
        double GeneratedTokens,
        double? MtpGeneratedTokens,
        double? MtpAcceptedTokens);

    private sealed record SlotAggregateObservation(
        double? PromptRate,
        double? GenerationRate,
        double? PromptTokens,
        double? GeneratedTokens,
        double? MtpGeneratedTokens,
        double? MtpAcceptedTokens);
}
