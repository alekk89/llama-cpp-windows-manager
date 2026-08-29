namespace LocalLlmConsole.Models;

public sealed record RuntimeRecord(
    string Id,
    string Name,
    RuntimeMode Mode,
    RuntimeBackend Backend,
    string ExecutablePath,
    string MetadataJson,
    DateTimeOffset UpdatedAt);

public sealed record JobRecord(
    string Id,
    string Kind,
    JobStatus Status,
    string PayloadJson,
    string LogPath,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record HuggingFaceFile(
    string Repo,
    string Path,
    string Name,
    string Quant,
    long SizeBytes,
    long Downloads,
    string PipelineTag = "",
    string LibraryName = "",
    string[]? Tags = null,
    bool HasVisionProjector = false,
    bool HasConfig = false,
    bool HasTokenizer = false,
    bool HasAdapter = false,
    bool HasDraftOrMtp = false,
    string CapabilityHints = "",
    string Revision = "",
    string Sha256 = "",
    string License = "",
    string VisionProjectorPath = "",
    string VisionProjectorName = "",
    long VisionProjectorSizeBytes = 0,
    string VisionProjectorSha256 = "");

public sealed record DownloadJobPayload(
    HuggingFaceFile File,
    string Destination,
    long DownloadedBytes = 0,
    long TotalBytes = 0,
    string Error = "");

public sealed record RuntimeLaunchRequest
{
    public required RuntimeMode Mode { get; init; }
    public required RuntimeBackend Backend { get; init; }
    public required string ExecutablePath { get; init; }
    public required string ModelPath { get; init; }
    public string? WslDistro { get; init; }
    public string Host { get; init; } = "127.0.0.1";
    public bool AllowNetworkAccess { get; init; }
    public string ApiKey { get; init; } = "";
    public bool RequireApiKeyAuth { get; init; }
    public int Port { get; init; } = 8081;
    public int ContextSize { get; init; }
    public int GpuLayers { get; init; }
    public string GpuMode { get; init; } = AppSettings.DefaultGpuMode;
    public string GpuDevices { get; init; } = "";
    public string GpuSplit { get; init; } = "";
    public int ParallelSlots { get; init; } = 1;
    public int BatchSize { get; init; } = AppSettings.DefaultBatchSize;
    public int MicroBatchSize { get; init; } = 512;
    public int Threads { get; init; }
    public string FlashAttention { get; init; } = "auto";
    public string CacheTypeK { get; init; } = AppSettings.DefaultCacheType;
    public string CacheTypeV { get; init; } = AppSettings.DefaultCacheType;
    public string KvOffload { get; init; } = "auto";
    public string KvUnified { get; init; } = "auto";
    public string PromptCacheMode { get; init; } = AppSettings.DefaultPromptCacheMode;
    public int PromptCacheRamMb { get; init; } = AppSettings.DefaultPromptCacheRamMb;
    public string ContextCheckpointsMode { get; init; } = AppSettings.DefaultContextCheckpointsMode;
    public int ContextCheckpointCount { get; init; } = AppSettings.DefaultContextCheckpointCount;
    public int ContextCheckpointEveryNTokens { get; init; } = AppSettings.DefaultContextCheckpointEveryNTokens;
    public string ContinuousBatching { get; init; } = "on";
    public string ReasoningMode { get; init; } = "auto";
    public string ReasoningFormat { get; init; } = "auto";
    public string ReasoningEffort { get; init; } = "default";
    public int ReasoningBudget { get; init; } = -1;
    public string ReasoningBudgetMessage { get; init; } = "";
    public string ReasoningPreserve { get; init; } = "auto";
    public string JinjaMode { get; init; } = "auto";
    public string VisionMode { get; init; } = "auto";
    public string? VisionProjectorPath { get; init; }
    public bool VisionProjectorEmbedded { get; init; }
    public int VisionImageMinTokens { get; init; }
    public int VisionImageMaxTokens { get; init; }
    public string MmapMode { get; init; } = "auto";
    public string MlockMode { get; init; } = "off";
    public double Temperature { get; init; } = AppSettings.DefaultTemperature;
    public int TopK { get; init; } = 40;
    public double TopP { get; init; } = 0.95;
    public double MinP { get; init; } = 0.05;
    public int MaxTokens { get; init; } = -1;
    public int Seed { get; init; } = -1;
    public int RepeatLastN { get; init; } = 64;
    public double RepeatPenalty { get; init; } = 1.0;
    public double PresencePenalty { get; init; } = AppSettings.DefaultPresencePenalty;
    public double FrequencyPenalty { get; init; } = AppSettings.DefaultFrequencyPenalty;
    public string RopeScaling { get; init; } = "auto";
    public double RopeScale { get; init; } = AppSettings.DefaultRopeScale;
    public double RopeFreqBase { get; init; } = AppSettings.DefaultRopeFreqBase;
    public double RopeFreqScale { get; init; } = AppSettings.DefaultRopeFreqScale;
    public string SpeculativeType { get; init; } = "none";
    public string? SpecDraftModelPath { get; init; }
    public string? MtpHeadPath { get; init; }
    public int SpecDraftGpuLayers { get; init; } = -1;
    public int SpecDraftMinTokens { get; init; } = AppSettings.DefaultSpecDraftMinTokens;
    public int SpecDraftMaxTokens { get; init; } = AppSettings.DefaultSpecDraftMaxTokens;
    public double SpecDraftPSplit { get; init; } = -1.0;
    public double SpecDraftPMin { get; init; } = -1.0;
    public string SpecDraftCacheTypeK { get; init; } = "q8_0";
    public string SpecDraftCacheTypeV { get; init; } = "q8_0";
    public IReadOnlyList<string> ExtraArgs { get; init; } = Array.Empty<string>();
}

public sealed record ValidationResult(bool Ok, IReadOnlyList<string> Errors)
{
    public static ValidationResult Success { get; } = new(true, Array.Empty<string>());
    public static ValidationResult Fail(IEnumerable<string> errors) => new(false, errors.ToArray());
}
