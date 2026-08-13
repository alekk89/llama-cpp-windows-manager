namespace LocalLlmConsole.Models;

public sealed record AppSettings(
    string WorkspaceRoot,
    string ModelsRoot,
    string RuntimeRoot,
    string CacheRoot,
    string ThemeMode,
    string MinimizeBehavior,
    bool StartWithWindows,
    string ModelAccessMode,
    bool AutoLoadGatewayEnabled,
    int AutoLoadGatewayPort,
    string AutoLoadGatewayPolicy,
    string Host,
    bool RequireApiKeyAuth,
    string ModelApiKey,
    string ModelApiKeyBackup,
    string WslDistro,
    int Port,
    int ContextSize,
    int GpuLayers,
    bool EnableMetrics,
    int MaxLogFileSizeMb,
    int AutoUnloadIdleMinutes,
    bool DeleteRuntimeSourceAfterSuccessfulBuild,
    string ReasoningMode,
    string ReasoningFormat,
    int ReasoningBudget,
    string VisionMode,
    string VisionProjectorPath,
    int VisionImageMinTokens,
    int VisionImageMaxTokens,
    string FlashAttention,
    string CacheTypeK,
    string CacheTypeV,
    string KvOffload,
    string KvUnified,
    string ContinuousBatching,
    string JinjaMode,
    int ParallelSlots,
    int BatchSize,
    int MicroBatchSize,
    int Threads,
    string MmapMode,
    string MlockMode,
    double Temperature,
    int TopK,
    double TopP,
    double MinP,
    int MaxTokens,
    int Seed,
    int RepeatLastN,
    double RepeatPenalty,
    double PresencePenalty,
    double FrequencyPenalty,
    string RopeScaling,
    double RopeScale,
    double RopeFreqBase,
    double RopeFreqScale,
    string SpeculativeType,
    string SpecDraftModelPath,
    string MtpHeadPath,
    int SpecDraftGpuLayers,
    int SpecDraftMinTokens,
    int SpecDraftMaxTokens,
    double SpecDraftPSplit,
    double SpecDraftPMin,
    string SpecDraftCacheTypeK,
    string SpecDraftCacheTypeV,
    string CudaPackagePreference,
    string PromptCacheMode = "auto",
    int PromptCacheRamMb = 8192,
    string ContextCheckpointsMode = "auto",
    int ContextCheckpointCount = 32,
    int ContextCheckpointEveryNTokens = 256,
    string CustomParameters = "",
    string UiCulture = "en",
    string GpuMode = "auto",
    string GpuDevices = "",
    string GpuSplit = "")
{
    public const int DefaultContextSize = 131_072;
    public const int DefaultGpuLayers = 999;
    public const int DefaultBatchSize = 4096;
    public const string DefaultCacheType = "q8_0";
    public const double DefaultTemperature = 0.65;
    public const int DefaultMaxTokens = -1;
    public const int DefaultSeed = -1;
    public const int DefaultRepeatLastN = 64;
    public const double DefaultRepeatPenalty = 1.0;
    public const double DefaultPresencePenalty = 0.0;
    public const double DefaultFrequencyPenalty = 0.0;
    public const string DefaultRopeScaling = "auto";
    public const double DefaultRopeScale = 0.0;
    public const double DefaultRopeFreqBase = 0.0;
    public const double DefaultRopeFreqScale = 0.0;
    public const string DefaultSpeculativeType = "none";
    public const string DefaultMtpHeadPath = "";
    public const int DefaultSpecDraftGpuLayers = -1;
    public const int DefaultSpecDraftMinTokens = 0;
    public const int DefaultSpecDraftMaxTokens = 0;
    public const double DefaultSpecDraftPSplit = -1.0;
    public const double DefaultSpecDraftPMin = -1.0;
    public const int DefaultVisionImageMinTokens = 0;
    public const int DefaultVisionImageMaxTokens = 0;
    public const string DefaultCudaPackagePreference = "latest";
    public const string DefaultPromptCacheMode = "auto";
    public const int DefaultPromptCacheRamMb = 8192;
    public const string DefaultContextCheckpointsMode = "auto";
    public const int DefaultContextCheckpointCount = 32;
    public const int DefaultContextCheckpointEveryNTokens = 256;
    public const string DefaultGpuMode = "auto";

    public static AppSettings CreateDefault(string workspaceRoot) => new(
        workspaceRoot,
        Path.Combine(workspaceRoot, "models"),
        Path.Combine(workspaceRoot, "runtimes"),
        Path.Combine(workspaceRoot, "cache"),
        "system",
        "taskbarOnly",
        false,
        "local",
        true,
        8082,
        "singleActive",
        "127.0.0.1",
        true,
        "",
        "",
        "Ubuntu-24.04",
        8081,
        DefaultContextSize,
        DefaultGpuLayers,
        true,
        1,
        0,
        true,
        "auto",
        "auto",
        -1,
        "auto",
        "",
        DefaultVisionImageMinTokens,
        DefaultVisionImageMaxTokens,
        "auto",
        DefaultCacheType,
        DefaultCacheType,
        "auto",
        "auto",
        "on",
        "auto",
        1,
        DefaultBatchSize,
        512,
        0,
        "auto",
        "off",
        DefaultTemperature,
        40,
        0.95,
        0.05,
        DefaultMaxTokens,
        DefaultSeed,
        DefaultRepeatLastN,
        DefaultRepeatPenalty,
        DefaultPresencePenalty,
        DefaultFrequencyPenalty,
        DefaultRopeScaling,
        DefaultRopeScale,
        DefaultRopeFreqBase,
        DefaultRopeFreqScale,
        DefaultSpeculativeType,
        "",
        DefaultMtpHeadPath,
        DefaultSpecDraftGpuLayers,
        DefaultSpecDraftMinTokens,
        DefaultSpecDraftMaxTokens,
        DefaultSpecDraftPSplit,
        DefaultSpecDraftPMin,
        DefaultCacheType,
        DefaultCacheType,
        DefaultCudaPackagePreference,
        DefaultPromptCacheMode,
        DefaultPromptCacheRamMb,
        DefaultContextCheckpointsMode,
        DefaultContextCheckpointCount,
        DefaultContextCheckpointEveryNTokens,
        "",
        "en",
        DefaultGpuMode,
        "",
        "");
}
