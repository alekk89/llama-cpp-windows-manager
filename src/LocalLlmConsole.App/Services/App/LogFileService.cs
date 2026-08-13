
namespace LocalLlmConsole.Services;

public sealed record LogDeletionPlan(IReadOnlyList<string> DeletablePaths, int SkippedCount);

public static class LogFileService
{
    public static (string Type, string Related) Describe(string path, JobRecord? job, bool activeRuntime, string activeModel)
    {
        var name = Path.GetFileName(path);
        if (activeRuntime)
            return ("Model runtime", string.IsNullOrWhiteSpace(activeModel) ? "Current model" : $"Current model: {activeModel}");

        if (job is not null)
            return (JobType(job), $"{job.Status}: {FormatJobPayload(job)}");

        if (name.StartsWith("llama-server-", StringComparison.OrdinalIgnoreCase))
        {
            var model = InferRuntimeModel(path);
            return ("Model runtime", string.IsNullOrWhiteSpace(model) ? "llama.cpp server" : $"Model: {model}");
        }

        if (name.StartsWith("app-", StringComparison.OrdinalIgnoreCase))
            return ("App", "Application events and caught errors");

        if (name.StartsWith("control-api", StringComparison.OrdinalIgnoreCase))
            return ("Control API", "Authenticated local API requests");

        if (name.StartsWith("runtime-build-", StringComparison.OrdinalIgnoreCase))
            return ("Runtime build", "llama.cpp build job");

        if (name.StartsWith("runtime-update-check-", StringComparison.OrdinalIgnoreCase))
            return ("Runtime update", "Runtime update check");

        return ("Log", "");
    }

    public static string JobType(JobRecord job)
    {
        if (job.Kind.Contains("runtime-package", StringComparison.OrdinalIgnoreCase)) return "Runtime download";
        if (job.Kind.Contains("runtime-build", StringComparison.OrdinalIgnoreCase)) return "Runtime build";
        if (job.Kind.Contains("runtime-update", StringComparison.OrdinalIgnoreCase)) return "Runtime update";
        if (job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase) && job.Kind.Contains("download", StringComparison.OrdinalIgnoreCase)) return "Runtime download";
        if (job.Kind.Contains("download", StringComparison.OrdinalIgnoreCase)) return "Model download";
        return "App job";
    }

    public static string FormatJobPayload(JobRecord job)
    {
        try
        {
            var node = JsonNode.Parse(job.PayloadJson);
            var message = node?["message"]?.ToString();
            if (!string.IsNullOrWhiteSpace(message)) return message;

            if (job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase))
            {
                var label = node?["label"]?.ToString()
                    ?? node?["Label"]?.ToString()
                    ?? node?["presetLabel"]?.ToString()
                    ?? node?["preset"]?.ToString()
                    ?? "runtime";
                var action = node?["action"]?.ToString() ?? "build";
                var installDir = node?["installDir"]?.ToString();
                return string.IsNullOrWhiteSpace(installDir)
                    ? $"{action}: {label}"
                    : $"{action}: {label} -> {installDir}";
            }
        }
        catch
        {
            // Fall back to the raw payload below.
        }

        return job.PayloadJson;
    }

    public static string RuntimeJobProgressSummary(JobRecord job, int maxChars = 180)
    {
        var payload = FormatJobPayload(job);
        if (!job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase)
            || job.Status is not (JobStatus.Queued or JobStatus.Running)
            || string.IsNullOrWhiteSpace(job.LogPath)
            || !File.Exists(job.LogPath))
            return payload;

        try
        {
            var line = LastMeaningfulLogLine(Tail(job.LogPath, 12000));
            if (string.IsNullOrWhiteSpace(line)) return payload;
            var summary = string.IsNullOrWhiteSpace(payload) ? line : $"{payload} | {line}";
            return TrimSummary(summary, maxChars);
        }
        catch
        {
            return payload;
        }
    }

    private static string LastMeaningfulLogLine(string text)
    {
        var lines = (text ?? "").Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        for (var i = lines.Length - 1; i >= 0; i--)
        {
            var line = StripAnsi(lines[i]).Trim();
            if (line.Length == 0) continue;
            if (line.Contains("all slots are idle", StringComparison.OrdinalIgnoreCase)) continue;
            var timestampEnd = line.IndexOf("] ", StringComparison.Ordinal);
            if (line.StartsWith("[", StringComparison.Ordinal) && timestampEnd > 0 && timestampEnd + 2 < line.Length)
                line = line[(timestampEnd + 2)..].Trim();
            return TrimSummary(line, 140);
        }

        return "";
    }

    private static string StripAnsi(string text)
        => Regex.Replace(text ?? "", @"\x1B\[[0-?]*[ -/]*[@-~]", "");

    private static string TrimSummary(string text, int maxChars)
    {
        var value = (text ?? "").Trim();
        return value.Length <= maxChars ? value : value[..Math.Max(0, maxChars - 3)] + "...";
    }

    public static string InferRuntimeModel(string path)
    {
        try
        {
            var sample = Head(path, 16000);
            var match = Regex.Match(sample, @"loading model\s+'([^']+)'", RegexOptions.IgnoreCase);
            if (!match.Success) return "";
            var fileName = Path.GetFileName(match.Groups[1].Value.Replace('\\', '/'));
            return string.IsNullOrWhiteSpace(fileName) ? match.Groups[1].Value : fileName;
        }
        catch
        {
            return "";
        }
    }

    public static string RedactSensitiveText(string text, string apiKey)
    {
        var redacted = text ?? "";
        var trimmedApiKey = (apiKey ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(trimmedApiKey))
            redacted = redacted.Replace(trimmedApiKey, "[redacted]", StringComparison.Ordinal);
        redacted = Regex.Replace(redacted, @"(?i)(--api-key(?:=|\s+))\S+", "$1[redacted]");
        redacted = Regex.Replace(redacted, @"(?i)(Authorization\s*:\s*Bearer\s+)\S+", "$1[redacted]");
        redacted = Regex.Replace(redacted, @"(?i)(Bearer\s+)[A-Za-z0-9._~+/=-]{8,}", "$1[redacted]");
        return redacted;
    }

    public static bool TryValidateWorkspaceLogFile(string workspaceRoot, string path, out string fullPath, out string error)
    {
        fullPath = "";
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Select a log file first.";
            return false;
        }

        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            error = "The selected log path is invalid.";
            return false;
        }

        var logRoot = WorkspaceLogRoot(workspaceRoot);
        if (!fullPath.StartsWith(logRoot, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(Path.GetExtension(fullPath), ".log", StringComparison.OrdinalIgnoreCase))
        {
            error = "Only app log files under the workspace logs folder can be opened.";
            return false;
        }

        if (!File.Exists(fullPath))
        {
            error = "That log file is no longer available.";
            return false;
        }

        try
        {
            if ((File.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
            {
                error = "Refusing to open a log file that is a symlink or junction.";
                return false;
            }
        }
        catch
        {
            error = "Could not inspect the selected log file.";
            return false;
        }

        return true;
    }

    public static LogDeletionPlan BuildDeletionPlan(string workspaceRoot, IEnumerable<string> candidates, string activeRuntimeLogPath)
        => BuildDeletionPlan(workspaceRoot, candidates, string.IsNullOrWhiteSpace(activeRuntimeLogPath) ? [] : [activeRuntimeLogPath]);

    public static LogDeletionPlan BuildDeletionPlan(string workspaceRoot, IEnumerable<string> candidates, IEnumerable<string> activeRuntimeLogPaths)
    {
        var activePaths = activeRuntimeLogPaths
            .Select(NormalizePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var deletable = new List<string>();
        var skipped = 0;

        foreach (var candidate in candidates)
        {
            if (!TryValidateWorkspaceLogFile(workspaceRoot, candidate, out var fullPath, out _)
                || activePaths.Contains(fullPath))
            {
                skipped++;
                continue;
            }

            if (seen.Add(fullPath))
                deletable.Add(fullPath);
        }

        return new LogDeletionPlan(deletable, skipped);
    }

    public static int DeleteLogs(IEnumerable<string> paths)
    {
        var deleted = 0;
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            File.Delete(path);
            deleted++;
        }

        return deleted;
    }

    public static string FormatDeletionStatus(int deleted, int skipped, string deletedNoun)
    {
        var deletedText = CountLabel(deleted, deletedNoun);
        return skipped == 0
            ? $"Deleted {deletedText}."
            : $"Deleted {deletedText}; skipped {CountLabel(skipped, "active or unsafe file")}.";
    }

    public static string Tail(string path, int maxChars)
    {
        var maxBytes = Math.Max(4096, maxChars * 4);
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var length = stream.Length;
        if (length > maxBytes) stream.Seek(-maxBytes, SeekOrigin.End);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        return text.Length <= maxChars ? text : text[^maxChars..];
    }

    public static string Head(string path, int maxChars)
    {
        if (maxChars <= 0) return "";
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var buffer = new char[maxChars];
        var read = reader.ReadBlock(buffer, 0, buffer.Length);
        return new string(buffer, 0, read);
    }

    public static string CollapseIdleSlotNoise(string text)
    {
        var lines = (text ?? "").Split(["\r\n", "\n"], StringSplitOptions.None);
        var result = new List<string>(lines.Length);
        var skippedIdle = 0;

        void FlushSkipped()
        {
            if (skippedIdle <= 0) return;
            result.Add(skippedIdle == 1
                ? "... omitted 1 repeated 'all slots are idle' line"
                : $"... omitted {skippedIdle:N0} repeated 'all slots are idle' lines");
            skippedIdle = 0;
        }

        foreach (var line in lines)
        {
            if (line.Contains("all slots are idle", StringComparison.OrdinalIgnoreCase))
            {
                skippedIdle++;
                continue;
            }

            FlushSkipped();
            result.Add(line);
        }

        FlushSkipped();
        return string.Join(Environment.NewLine, result);
    }

    public static string NormalizePath(string path)
        => string.IsNullOrWhiteSpace(path) ? "" : Path.GetFullPath(path);

    private static string CountLabel(int count, string noun)
        => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static string WorkspaceLogRoot(string workspaceRoot)
        => Path.GetFullPath(Path.Combine(workspaceRoot, "logs")).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
}
