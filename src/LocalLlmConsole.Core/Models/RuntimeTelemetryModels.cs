namespace LocalLlmConsole.Services;

public sealed record RuntimeMtpTokenSnapshot(
    double? GeneratedTokens,
    double? AcceptedTokens,
    double? GeneratedSeconds = null,
    double? AcceptedSeconds = null);

public sealed record RuntimeSlotCounterSnapshot(
    string SlotId,
    string TaskId,
    double PromptTokensProcessed,
    double GeneratedTokens,
    bool IsProcessing,
    double? MtpGeneratedTokens = null,
    double? MtpAcceptedTokens = null);

public sealed record RuntimeSlotSnapshot(
    double PromptTokensProcessed,
    double GeneratedTokens,
    bool IsProcessing,
    double? PromptTokens,
    double? ContextTokens,
    double? ContextSize,
    double? MtpGeneratedTokens = null,
    double? MtpAcceptedTokens = null,
    IReadOnlyList<RuntimeSlotCounterSnapshot>? SlotCounters = null,
    double? ContextCapacityTokens = null);

public sealed record RuntimeMetricPollResult(
    LoadedModelSessionSnapshot Session,
    string RuntimeKey,
    IReadOnlyList<PrometheusSample> Samples,
    RuntimeSlotSnapshot? SlotSnapshot,
    string Error,
    bool EndpointResponded = true);
