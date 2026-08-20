namespace LocalLlmConsole.Services;

internal sealed class RuntimeMetricSummaryState
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

internal sealed record RuntimeSlotCounterState(
    string TaskId,
    double PromptTokensProcessed,
    double GeneratedTokens,
    double? MtpGeneratedTokens,
    double? MtpAcceptedTokens);

internal sealed record SlotAggregateObservation(
    double? PromptRate,
    double? GenerationRate,
    double? PromptTokens,
    double? GeneratedTokens,
    double? MtpGeneratedTokens,
    double? MtpAcceptedTokens);
