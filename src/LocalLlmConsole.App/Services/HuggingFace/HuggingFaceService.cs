using System.Collections.Concurrent;

namespace LocalLlmConsole.Services;

public sealed partial class HuggingFaceService : IHuggingFaceDownloadOperations, IDisposable
{
    private const long DownloadProgressUpdateBytes = 32L * 1024L * 1024L;
    private static readonly TimeSpan DownloadProgressUpdateInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan DownloadReadIdleTimeout = TimeSpan.FromSeconds(60);

    private sealed record RepoInfo(
        string Repo,
        long Downloads,
        string PipelineTag,
        string LibraryName,
        string[] Tags,
        string[] Siblings,
        bool HasVisionProjector,
        bool HasConfig,
        bool HasTokenizer,
        bool HasAdapter,
        bool HasDraftOrMtp,
        string CapabilityHints,
        string Revision,
        string License,
        string VisionProjectorPath,
        string VisionProjectorName,
        long VisionProjectorSizeBytes,
        string VisionProjectorSha256,
        JsonNode? Detail);
    private sealed record CachedRepoInfo(RepoInfo Info, DateTimeOffset CachedAt);
    private sealed record SuggestedLaunchProfile(ModelLaunchSettings Settings, string Source);
    private sealed record VisionProjectorDownloadResult(string LocalPath, string Error);

    private sealed class ActiveDownload
    {
        public CancellationTokenSource Cancellation { get; } = new();
        public JobStatus RequestedStopStatus { get; set; } = JobStatus.Cancelled;
        public Task Completion { get; set; } = Task.CompletedTask;
        public required string Destination { get; init; }
    }

    private const int RepoInfoCacheLimit = 256;
    private static readonly TimeSpan RepoInfoCacheTtl = TimeSpan.FromMinutes(30);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly StateStore _store;
    private readonly JobEngine _jobs;
    private readonly ModelCatalogService _catalog;
    private readonly ConcurrentDictionary<string, ActiveDownload> _activeDownloads = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _activeDownloadDestinations = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedRepoInfo> _repoInfoCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly HashSet<string> ReservedWindowsFileNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    public HuggingFaceService(StateStore store, JobEngine jobs, ModelCatalogService catalog)
    {
        _store = store;
        _jobs = jobs;
        _catalog = catalog;
    }

    public void Dispose()
        => _http.Dispose();

}
