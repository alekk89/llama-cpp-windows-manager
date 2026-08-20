
namespace LocalLlmConsole.Models;

public enum OwnershipKind
{
    AppOwned,
    External,
    RegistryOnly
}

public enum JobStatus
{
    Queued,
    Running,
    Paused,
    Cancelled,
    Failed,
    Completed,
    Interrupted
}

public enum RuntimeMode
{
    Native,
    Wsl
}

public enum RuntimeBackend
{
    Cpu,
    Cuda,
    Vulkan,
    Metal,
    Sycl
}

public enum LoadedModelSessionStatus
{
    Stopped,
    Loading,
    Running,
    Warm,
    Unreachable,
    Stopping,
    Failed
}

public enum RuntimeEndpointHealth
{
    Unknown,
    Healthy,
    Unreachable
}

public sealed record ModelRecord(
    string Id,
    string Name,
    string ModelPath,
    OwnershipKind Ownership,
    string MetadataJson,
    DateTimeOffset UpdatedAt);

public sealed record ActiveRuntimeSession(
    string ModelId,
    string RuntimeId,
    AppSettings LaunchSettings,
    string LogPath,
    DateTimeOffset StartedAt,
    string ProcessMarker = "",
    int ProcessId = 0,
    string SessionId = "",
    bool IsSelected = true,
    string LaunchProfileId = "",
    string LaunchProfileName = "");

public sealed record LoadedModelSessionSnapshot(
    string SessionId,
    string ModelId,
    string ModelName,
    string RuntimeId,
    string RuntimeName,
    RuntimeMode Mode,
    RuntimeBackend Backend,
    AppSettings LaunchSettings,
    string LogPath,
    DateTimeOffset StartedAt,
    string ProcessMarker,
    int ProcessId,
    LoadedModelSessionStatus Status,
    bool IsRunning,
    bool IsSelected,
    long ModelSizeBytes = 0,
    string LaunchProfileId = "",
    string LaunchProfileName = "",
    RuntimeEndpointHealth EndpointHealth = RuntimeEndpointHealth.Unknown,
    int ConsecutiveEndpointFailures = 0,
    string StatusReason = "",
    DateTimeOffset? StoppedAt = null)
{
    public string Endpoint => RuntimeEndpointService.LocalOpenAiBaseUrl(LaunchSettings);
    public string EndpointDisplay => RuntimeEndpointService.EndpointDisplay(LaunchSettings);
    public string ModelSize => DisplayFormatService.Bytes(ModelSizeBytes);
}

public sealed record TokenUsageRecord(
    string ModelId,
    string ModelName,
    long PromptTokens,
    long GeneratedTokens,
    DateTimeOffset UpdatedAt,
    long CachedPromptTokens = 0,
    bool CacheCounterObserved = false);
