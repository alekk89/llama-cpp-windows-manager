using static LocalLlmConsole.Services.RuntimeSlotAggregateTracker;
using static LocalLlmConsole.Services.RuntimeMetricSummaryCalculations;

namespace LocalLlmConsole.Services;

public sealed partial class RuntimeMetricSummaryTracker
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
                new RuntimeMetricGraphSample(runtimeKey, null, null, null, null, null),
                snapshot.Atomic);
        }

        var now = capturedAt ?? DateTimeOffset.UtcNow;
        var observation = ObserveCurrentPoll(state, samples, metricsSettings, slotSnapshot, mtpTokenSnapshot, now);
        var display = MergeWithPrevious(observation, previous, now);
        var projection = ProjectResult(runtimeKey, metricsSettings, observation, display);

        Remember(state, runtimeKey, samples, projection.Update);
        return projection.Result;
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
        RuntimeMetricDisplayUpdate update)
    {
        if (update.GeneratedTokens is null
            && update.PromptTokens is null
            && update.MtpGeneratedTokens is null
            && update.MtpAcceptedTokens is null
            && update.AverageGenerationRate is null
            && update.AveragePromptRate is null
            && update.AverageMtpGeneratedRate is null
            && update.AverageMtpAcceptedRate is null
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
            update.TokensText,
            update.GenerationRateText,
            update.TotalTokensText,
            update.MtpTokensText,
            update.SlotsText,
            update.SettingsText,
            update.CapturedAt,
            update.GeneratedTokens,
            update.PromptTokens,
            update.MtpGeneratedTokens,
            update.MtpAcceptedTokens,
            update.AverageGenerationRate,
            update.AveragePromptRate,
            update.AverageMtpGeneratedRate,
            update.AverageMtpAcceptedRate,
            update.GeneratedTokensCapturedAt,
            update.PromptTokensCapturedAt,
            update.MtpGeneratedTokensCapturedAt,
            update.MtpAcceptedTokensCapturedAt,
            update.AverageGenerationRateCapturedAt,
            update.AveragePromptRateCapturedAt,
            update.AverageMtpGeneratedRateCapturedAt,
            update.AverageMtpAcceptedRateCapturedAt,
            update.Atomic);
    }

    private sealed class RuntimeMetricDisplayUpdate
    {
        public required string TokensText { get; init; }
        public required string GenerationRateText { get; init; }
        public required string TotalTokensText { get; init; }
        public required string MtpTokensText { get; init; }
        public required string SlotsText { get; init; }
        public required string SettingsText { get; init; }
        public double? GeneratedTokens { get; init; }
        public double? PromptTokens { get; init; }
        public double? MtpGeneratedTokens { get; init; }
        public double? MtpAcceptedTokens { get; init; }
        public double? AverageGenerationRate { get; init; }
        public double? AveragePromptRate { get; init; }
        public double? AverageMtpGeneratedRate { get; init; }
        public double? AverageMtpAcceptedRate { get; init; }
        public DateTimeOffset? GeneratedTokensCapturedAt { get; init; }
        public DateTimeOffset? PromptTokensCapturedAt { get; init; }
        public DateTimeOffset? MtpGeneratedTokensCapturedAt { get; init; }
        public DateTimeOffset? MtpAcceptedTokensCapturedAt { get; init; }
        public DateTimeOffset? AverageGenerationRateCapturedAt { get; init; }
        public DateTimeOffset? AveragePromptRateCapturedAt { get; init; }
        public DateTimeOffset? AverageMtpGeneratedRateCapturedAt { get; init; }
        public DateTimeOffset? AverageMtpAcceptedRateCapturedAt { get; init; }
        public required DateTimeOffset CapturedAt { get; init; }
        public required RuntimeMetricAtomicSnapshot Atomic { get; init; }
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

    private static double? SlotProcessingCount(RuntimeSlotSnapshot? snapshot)
    {
        if (snapshot?.SlotCounters is { Count: > 0 } counters)
            return counters.Count(counter => counter.IsProcessing);
        return snapshot?.IsProcessing == true ? 1 : snapshot is null ? null : 0;
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
