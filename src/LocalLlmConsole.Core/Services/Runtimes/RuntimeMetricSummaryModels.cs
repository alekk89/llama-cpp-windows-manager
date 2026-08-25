namespace LocalLlmConsole.Services;

public sealed record RuntimeMetricAtomicSnapshot(
    double? GenerationRate,
    double? PromptRate,
    double? AverageGenerationRate,
    double? AveragePromptRate,
    double? GeneratedTokens,
    double? PromptTokens,
    double? MtpGeneratedRate,
    double? MtpAcceptedRate,
    double? AverageMtpGeneratedRate,
    double? AverageMtpAcceptedRate,
    double? MtpGeneratedTokens,
    double? MtpAcceptedTokens,
    double ActiveSlots,
    double SlotCapacity,
    double QueuedRequests,
    double BusyDecodeSlots,
    double? KvCacheUsedTokens,
    double? KvCacheCapacityTokens,
    double? KvCacheUsagePercent,
    string KvCacheAllocation,
    double? RecentGenerationRate = null,
    double? RecentPromptRate = null,
    double? PromptCachedTokens = null,
    double? PromptCacheReusePercent = null,
    double? DraftAcceptancePercent = null,
    double? PeakContextTokens = null,
    double? ContextShiftCount = null)
{
    public static RuntimeMetricAtomicSnapshot Empty { get; } = new(
        null, null, null, null, null, null, null, null, null, null, null, null,
        0, 0, 0, 0, null, null, null, "Automatic");
}

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
    DateTimeOffset? AverageMtpAcceptedRateCapturedAt,
    RuntimeMetricAtomicSnapshot Atomic);

public sealed record RuntimeMetricSummaryResult(
    string Tokens,
    string GenerationRate,
    string TotalTokens,
    string MtpTokens,
    string Slots,
    string KvCache,
    bool UsedLastKnown,
    DateTimeOffset? LastKnownCapturedAt,
    RuntimeMetricGraphSample GraphSample,
    RuntimeMetricAtomicSnapshot Atomic);

public sealed record RuntimeMetricGraphSample(
    string RuntimeKey,
    double? GenerationRate,
    double? PromptRate,
    double? SpeculativeGeneratedRate,
    double? SpeculativeAcceptedRate,
    double? KvCacheUsagePercent);
