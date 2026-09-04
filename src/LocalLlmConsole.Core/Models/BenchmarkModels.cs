namespace LocalLlmConsole.Models;

public enum BenchmarkFailurePolicy
{
    Stop,
    Continue,
    RetryOnceThenStop,
    RetryOnceThenContinue
}

public enum BenchmarkRunOutcome
{
    Success,
    Partial,
    Failed,
    Cancelled,
    Interrupted
}

public enum BenchmarkResultClassification
{
    Unknown,
    PromptProcessing,
    TokenGeneration,
    PromptAndGeneration
}

public enum BenchmarkExecutionMode
{
    LlamaBench,
    ProfileServing
}

public sealed record BenchmarkPromptGenerationPair(int PromptTokens, int GenerationTokens);

public sealed record BenchmarkScopeSelection(string ModelId, string ProfileId, string RuntimeId = "");

public sealed record BenchmarkGpuConfiguration(string Mode, string Split = "");

public sealed record BenchmarkSpeculativeConfiguration(string Type, string Head = "profile");

public sealed record BenchmarkOptionSet
{
    public IReadOnlyList<int> Threads { get; init; } = [];
    public IReadOnlyList<int> BatchSizes { get; init; } = [];
    public IReadOnlyList<int> MicroBatchSizes { get; init; } = [];
    public IReadOnlyList<int> GpuLayers { get; init; } = [];
    public IReadOnlyList<int> CpuMoeLayers { get; init; } = [];
    public IReadOnlyList<string> FlashAttention { get; init; } = [];
    public IReadOnlyList<string> CacheTypesK { get; init; } = [];
    public IReadOnlyList<string> CacheTypesV { get; init; } = [];
    public IReadOnlyList<string> CacheTypesKv { get; init; } = [];
    public IReadOnlyList<string> KvOffload { get; init; } = [];
    public IReadOnlyList<BenchmarkGpuConfiguration> GpuConfigurations { get; init; } = [];
    // Legacy independent dimensions. New plans should use GpuConfigurations so
    // split mode and tensor distribution cannot be recombined accidentally.
    public IReadOnlyList<string> SplitModes { get; init; } = [];
    public IReadOnlyList<int> MainGpus { get; init; } = [];
    public IReadOnlyList<string> Devices { get; init; } = [];
    public IReadOnlyList<string> TensorSplits { get; init; } = [];
    public IReadOnlyList<string> LoadModes { get; init; } = [];
    public IReadOnlyList<int> FitTargetsMiB { get; init; } = [];
    public IReadOnlyList<int> FitContexts { get; init; } = [];
    public IReadOnlyList<string> NumaModes { get; init; } = [];
    public IReadOnlyList<int> Priorities { get; init; } = [];
    public IReadOnlyList<string> CpuMasks { get; init; } = [];
    public IReadOnlyList<string> CpuStrict { get; init; } = [];
    public IReadOnlyList<int> PollValues { get; init; } = [];
    public IReadOnlyList<string> Embeddings { get; init; } = [];
    public IReadOnlyList<string> NoOpOffload { get; init; } = [];
    public IReadOnlyList<string> NoHost { get; init; } = [];
    public IReadOnlyList<string> TensorOverrides { get; init; } = [];
    public IReadOnlyList<string> AdditionalArguments { get; init; } = [];
}

public sealed record BenchmarkServingOptions
{
    public IReadOnlyList<int> ContextSizes { get; init; } = [];
    public IReadOnlyList<BenchmarkSpeculativeConfiguration> SpeculativeConfigurations { get; init; } = [];
    // Legacy independent dimensions. New plans should use SpeculativeConfigurations
    // so a speculative type and its companion/head source remain an exact pair.
    public IReadOnlyList<string> SpeculativeTypes { get; init; } = [];
    public IReadOnlyList<string> SpeculativeCompanionModes { get; init; } = [];
    public IReadOnlyList<int> Concurrencies { get; init; } = [1];
    public int ReadyTimeoutSeconds { get; init; } = 600;
    public int RequestTimeoutSeconds { get; init; } = 600;
    public bool RequireSpeculativeMetrics { get; init; } = true;
    public int Seed { get; init; } = 42;
    public double Temperature { get; init; }
}

public sealed record BenchmarkPlan
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public string Name { get; init; } = "Benchmark run";
    public BenchmarkExecutionMode ExecutionMode { get; init; } = BenchmarkExecutionMode.LlamaBench;
    public IReadOnlyList<string> ModelIds { get; init; } = [];
    public IReadOnlyList<string> ProfileIds { get; init; } = [];
    public IReadOnlyList<string> RuntimeIds { get; init; } = [];
    public IReadOnlyList<BenchmarkScopeSelection> ScopeSelections { get; init; } = [];
    public bool AllModels { get; init; }
    public bool AllProfiles { get; init; }
    public bool AllRuntimes { get; init; }
    public bool UseProfileRuntime { get; init; } = true;
    public string WslDistro { get; init; } = "";
    public bool RepeatEquivalentProfiles { get; init; }
    public IReadOnlyList<int> PromptSizes { get; init; } = [512, 2048];
    public IReadOnlyList<int> GenerationSizes { get; init; } = [128];
    public IReadOnlyList<BenchmarkPromptGenerationPair> PromptGenerationPairs { get; init; } = [];
    public IReadOnlyList<int> Depths { get; init; } = [0];
    public int Repetitions { get; init; } = 5;
    public bool Warmup { get; init; } = true;
    public int DelaySeconds { get; init; }
    public int CooldownSeconds { get; init; }
    public BenchmarkFailurePolicy FailurePolicy { get; init; } = BenchmarkFailurePolicy.Stop;
    public bool StopActiveSessions { get; init; }
    public bool PreventSystemSleep { get; init; } = true;
    public BenchmarkOptionSet Options { get; init; } = new();
    public BenchmarkServingOptions Serving { get; init; } = new();
}

public sealed record BenchmarkEffectiveOptions(
    IReadOnlyList<int> Threads,
    IReadOnlyList<int> BatchSizes,
    IReadOnlyList<int> MicroBatchSizes,
    IReadOnlyList<int> GpuLayers,
    IReadOnlyList<int> CpuMoeLayers,
    IReadOnlyList<string> FlashAttention,
    IReadOnlyList<string> CacheTypesK,
    IReadOnlyList<string> CacheTypesV,
    IReadOnlyList<string> KvOffload,
    IReadOnlyList<string> SplitModes,
    IReadOnlyList<int> MainGpus,
    IReadOnlyList<string> Devices,
    IReadOnlyList<string> TensorSplits,
    IReadOnlyList<string> LoadModes,
    IReadOnlyList<int> FitTargetsMiB,
    IReadOnlyList<int> FitContexts,
    IReadOnlyList<string> NumaModes,
    IReadOnlyList<int> Priorities,
    IReadOnlyList<string> CpuMasks,
    IReadOnlyList<string> CpuStrict,
    IReadOnlyList<int> PollValues,
    IReadOnlyList<string> Embeddings,
    IReadOnlyList<string> NoOpOffload,
    IReadOnlyList<string> NoHost,
    IReadOnlyList<string> TensorOverrides,
    IReadOnlyList<string> AdditionalArguments);

public sealed record BenchmarkWorkItem(
    string Key,
    string ModelId,
    string ModelName,
    string ModelPath,
    string ModelFingerprint,
    IReadOnlyList<string> ProfileIds,
    IReadOnlyList<string> ProfileNames,
    string RuntimeId,
    string RuntimeName,
    RuntimeMode RuntimeMode,
    RuntimeBackend RuntimeBackend,
    string RuntimeExecutablePath,
    string WslDistro,
    BenchmarkEffectiveOptions Options,
    string EffectiveCommandSignature,
    int ExpectedResultRows,
    BenchmarkExecutionMode ExecutionMode = BenchmarkExecutionMode.LlamaBench,
    ModelLaunchSettings? LaunchSettings = null);

public sealed record BenchmarkPlanPreview(
    bool IsValid,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<BenchmarkWorkItem> WorkItems,
    int ExpectedResultRows,
    int TimedRepetitions,
    int DeduplicatedWorkItems);

public enum BenchmarkWorkItemStatus
{
    Pending,
    Running,
    Passed,
    Failed,
    Cancelled,
    Skipped
}

public sealed record BenchmarkWorkItemCheckpoint(
    string Key,
    BenchmarkWorkItemStatus Status = BenchmarkWorkItemStatus.Pending,
    int Attempt = 0,
    int ResultRows = 0,
    string Error = "");

public sealed record BenchmarkJobPayload(
    BenchmarkPlan Plan,
    IReadOnlyList<BenchmarkWorkItem> WorkItems,
    IReadOnlyList<BenchmarkWorkItemCheckpoint> Checkpoints,
    BenchmarkRunOutcome? Outcome,
    int CurrentWorkItemIndex,
    int CompletedWorkItems,
    int FailedWorkItems,
    int ResultRows,
    long Revision,
    string Message,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt);

public sealed record BenchmarkParsedResult(
    BenchmarkResultClassification Classification,
    string RawJson,
    string WorkloadSignature,
    string EnvironmentSignature,
    string ManagerVersion,
    string OperatingEnvironment,
    string BuildCommit,
    int BuildNumber,
    string CpuInfo,
    string GpuInfo,
    string Backends,
    string ModelFilename,
    string ModelType,
    long ModelSize,
    long ModelParameterCount,
    int PromptTokens,
    int GenerationTokens,
    int Depth,
    int BatchSize,
    int MicroBatchSize,
    int Threads,
    string CpuMask,
    bool CpuStrict,
    int Poll,
    int GpuLayers,
    int CpuMoeLayers,
    string CacheTypeK,
    string CacheTypeV,
    string SplitMode,
    int MainGpu,
    bool NoKvOffload,
    string FlashAttention,
    string Devices,
    string TensorSplit,
    string TensorBufferOverrides,
    string LoadMode,
    bool Embeddings,
    bool NoOpOffload,
    bool NoHost,
    long FitTarget,
    int FitMinimumContext,
    long AverageNanoseconds,
    long StandardDeviationNanoseconds,
    double AverageTokensPerSecond,
    double StandardDeviationTokensPerSecond,
    string TestTime,
    BenchmarkExecutionMode ExecutionMode = BenchmarkExecutionMode.LlamaBench,
    string ProfileId = "",
    string ProfileName = "",
    string SpeculativeType = "",
    int Concurrency = 1,
    int RequestCount = 0,
    int FailedRequestCount = 0,
    double AveragePromptTokensPerSecond = 0,
    double AverageLatencyMilliseconds = 0,
    double StandardDeviationLatencyMilliseconds = 0,
    long DraftTokens = 0,
    long AcceptedDraftTokens = 0,
    double DraftAcceptancePercent = 0,
    bool SpeculativeMetricsObserved = false,
    int ContextSize = 0,
    long ObservedGpuMemoryUsedMiB = 0,
    IReadOnlyList<BenchmarkGpuMemoryPeak>? GpuMemoryPeaks = null,
    int GpuMemorySampleIntervalMilliseconds = 0,
    int VulkanAllocationBlockSizeMiB = 0,
    string GpuMemoryMeasurementWindow = "");
