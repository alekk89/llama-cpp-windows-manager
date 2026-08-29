namespace LocalLlmConsole.Services;

public sealed record RuntimeLogTailRequest(
    string LogPath,
    bool IsRuntimeRunning,
    RuntimeSlotSnapshot? SlotSnapshot,
    int MaxCharacters = 16000,
    bool NewestFirst = true);

public sealed record RuntimeLogTailResult(
    string Text,
    bool HasActiveLog);

public sealed record RuntimeLogTailCapture(
    string LogPath,
    bool Exists,
    string RawTail,
    RuntimeMtpTokenSnapshot? MtpTokenStats,
    string Error);

public sealed class RuntimeLogTailService
{
    private const int MaximumCachedTails = 8;
    private readonly object _captureCacheGate = new();
    private readonly Dictionary<RuntimeLogTailCacheKey, RuntimeLogTailCacheEntry> _captureCache = [];

    public Task<RuntimeLogTailCapture> CaptureAsync(
        string logPath,
        int maxCharacters = 16000,
        CancellationToken cancellationToken = default)
        => Task.Run(() => Capture(logPath, maxCharacters), cancellationToken);

    public RuntimeLogTailCapture Capture(string logPath, int maxCharacters = 16000)
    {
        if (string.IsNullOrWhiteSpace(logPath) || !File.Exists(logPath))
            return new RuntimeLogTailCapture(logPath, Exists: false, "", null, "");

        try
        {
            var fullPath = Path.GetFullPath(logPath);
            var file = new FileInfo(fullPath);
            file.Refresh();
            if (!file.Exists)
                return new RuntimeLogTailCapture(logPath, Exists: false, "", null, "");
            var cacheKey = new RuntimeLogTailCacheKey(fullPath, maxCharacters);
            lock (_captureCacheGate)
            {
                if (_captureCache.TryGetValue(cacheKey, out var cached)
                    && cached.Length == file.Length
                    && cached.LastWriteTimeUtc == file.LastWriteTimeUtc)
                    return cached.Capture;
            }

            var rawTail = LogFileService.Tail(logPath, maxCharacters);
            var capture = new RuntimeLogTailCapture(
                logPath,
                Exists: true,
                rawTail,
                RuntimeDashboardService.ParseMtpTokenStats(rawTail),
                "");
            lock (_captureCacheGate)
            {
                if (_captureCache.Count >= MaximumCachedTails && !_captureCache.ContainsKey(cacheKey))
                    _captureCache.Clear();
                _captureCache[cacheKey] = new RuntimeLogTailCacheEntry(
                    file.Length,
                    file.LastWriteTimeUtc,
                    capture);
            }
            return capture;
        }
        catch (Exception ex)
        {
            return new RuntimeLogTailCapture(logPath, Exists: true, "", null, ex.Message);
        }
    }

    public RuntimeLogTailResult Build(RuntimeLogTailRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        return Build(request, Capture(request.LogPath, request.MaxCharacters));
    }

    public RuntimeLogTailResult Build(RuntimeLogTailRequest request, RuntimeLogTailCapture capture)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(capture);

        if (!capture.Exists)
        {
            return new RuntimeLogTailResult(
                request.IsRuntimeRunning
                    ? "Runtime log file has not been created yet."
                    : "No runtime log is active.",
                HasActiveLog: false);
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(capture.Error))
                throw new IOException(capture.Error);
            var heading = request.IsRuntimeRunning
                ? $"Live log: {request.LogPath}"
                : $"Last runtime log: {request.LogPath}";
            var slotStatus = SlotStatus(request.SlotSnapshot);
            var logTail = OrderedLogText(
                LogFileService.CollapseIdleSlotNoise(capture.RawTail),
                request.NewestFirst);
            var text = string.IsNullOrWhiteSpace(slotStatus)
                ? $"{heading}{Environment.NewLine}{Environment.NewLine}{logTail}"
                : $"{heading}{Environment.NewLine}{slotStatus}{Environment.NewLine}{Environment.NewLine}{logTail}";
            return new RuntimeLogTailResult(text, HasActiveLog: true);
        }
        catch (Exception ex)
        {
            return new RuntimeLogTailResult(
                $"Could not read runtime log yet.{Environment.NewLine}{Environment.NewLine}{ex.Message}",
                HasActiveLog: false);
        }
    }

    private static string SlotStatus(RuntimeSlotSnapshot? slotSnapshot)
    {
        if (slotSnapshot is null) return "";
        if (!slotSnapshot.IsProcessing) return "Slot status: idle";

        var promptTotal = slotSnapshot.PromptTokens?.ToString("N0") ?? "?";
        return $"Slot status: processing | Prompt {slotSnapshot.PromptTokensProcessed:N0}/{promptTotal} | Gen {slotSnapshot.GeneratedTokens:N0}";
    }

    private static string OrderedLogText(string value, bool newestFirst)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        var lines = value.TrimEnd('\r', '\n')
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n');
        if (newestFirst) Array.Reverse(lines);
        return string.Join(Environment.NewLine, lines);
    }

    private readonly record struct RuntimeLogTailCacheKey(string FullPath, int MaxCharacters);

    private sealed record RuntimeLogTailCacheEntry(
        long Length,
        DateTime LastWriteTimeUtc,
        RuntimeLogTailCapture Capture);
}
