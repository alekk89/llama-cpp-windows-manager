
namespace LocalLlmConsole.Services;

public sealed record RuntimeBuildPlan(
    string Action,
    string SourceDir,
    string BuildDir,
    string InstallDir,
    string ProcessMarker,
    string QueuedMessage);

public sealed record RuntimeBuildJobPayload(
    RuntimeBuildPreset Preset,
    string Action,
    string InstallDir,
    string Message,
    string ProcessMarker,
    string WslDistro,
    RuntimeMode Mode,
    string SourceDir);

public static class RuntimeBuildJobService
{
    public static RuntimeBuildPlan CreatePlan(
        RuntimeBuildPreset preset,
        bool update,
        RuntimeSourceEntry? source,
        AppSettings settings,
        DateTimeOffset now,
        string processMarker = "")
    {
        var stamp = now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var action = update ? "update" : "build";
        return new RuntimeBuildPlan(
            action,
            source?.SourceDir ?? Path.Combine(settings.CacheRoot, "runtime-sources", preset.Id),
            Path.Combine(settings.CacheRoot, "runtime-builds", $"{preset.Id}-{stamp}"),
            Path.Combine(settings.RuntimeRoot, $"{preset.Id}-{stamp}"),
            string.IsNullOrWhiteSpace(processMarker) ? $"local-llm-console-build-{Guid.NewGuid():N}" : processMarker,
            source is null ? "Queued." : $"Queued build from downloaded source {RuntimeMetadataService.ShortCommit(source.Commit)}.");
    }

    public static string Payload(RuntimeBuildPreset preset, string action, string installDir, string message, string processMarker = "", string wslDistro = "", string sourceDir = "") => JsonSerializer.Serialize(new
    {
        preset = preset.Id,
        label = preset.Label,
        repoUrl = preset.RepoUrl,
        branch = preset.Branch,
        cuda = preset.Cuda,
        backend = RuntimeBuildCatalogService.BackendKey(preset),
        mode = RuntimeBuildCatalogService.ModeKey(preset.Mode),
        installDir,
        wslDistro,
        processMarker,
        sourceDir,
        action,
        message
    });

    public static RuntimeBuildJobPayload? ParsePayload(string payloadJson)
    {
        try
        {
            var node = JsonNode.Parse(payloadJson);
            if (node is null) return null;
            var presetId = node["preset"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(presetId)) return null;
            var preset = new RuntimeBuildPreset(
                presetId,
                node["label"]?.ToString() ?? presetId,
                node["repoUrl"]?.ToString() ?? "",
                node["branch"]?.ToString() ?? "",
                BoolValue(node["cuda"]),
                Backend: node["backend"]?.ToString() ?? "",
                Mode: ParseMode(node["mode"]?.ToString() ?? ""));
            return new RuntimeBuildJobPayload(
                preset,
                node["action"]?.ToString() ?? "",
                node["installDir"]?.ToString() ?? "",
                node["message"]?.ToString() ?? "",
                node["processMarker"]?.ToString() ?? "",
                node["wslDistro"]?.ToString() ?? "",
                preset.Mode,
                node["sourceDir"]?.ToString() ?? "");
        }
        catch
        {
            return null;
        }
    }

    public static bool CanCancel(JobRecord job)
        => IsRuntimeBuildJob(job)
            && job.Status is JobStatus.Queued or JobStatus.Running
            && !string.IsNullOrWhiteSpace(ParsePayload(job.PayloadJson)?.ProcessMarker);

    public static bool CanRetry(JobRecord job)
    {
        if (!IsRuntimeBuildJob(job) || job.Status is not (JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted))
            return false;
        var action = ParsePayload(job.PayloadJson)?.Action ?? "";
        return action.Equals("build", StringComparison.OrdinalIgnoreCase)
            || action.Equals("update", StringComparison.OrdinalIgnoreCase);
    }

    public static bool CanClear(JobRecord job)
        => IsRuntimeJob(job)
            && job.Status is JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted;

    private static bool IsRuntimeBuildJob(JobRecord job)
        => job.Kind.Equals("runtime-build", StringComparison.OrdinalIgnoreCase);

    private static bool IsRuntimeJob(JobRecord job)
        => job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase);

    private static bool BoolValue(JsonNode? node)
    {
        if (node is null) return false;
        if (node is JsonValue value && value.TryGetValue<bool>(out var boolean)) return boolean;
        return bool.TryParse(node.ToString(), out var parsed) && parsed;
    }

    private static RuntimeMode ParseMode(string value)
    {
        var normalized = (value ?? "").Trim();
        return normalized.Equals("native", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("windows", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(nameof(RuntimeMode.Native), StringComparison.OrdinalIgnoreCase)
                ? RuntimeMode.Native
                : RuntimeMode.Wsl;
    }

    public static async Task AppendJobLogAsync(string logPath, JobStatus status, string message, long maxLogBytes)
        => await BoundedLogFile.AppendAsync(logPath, $"[{DateTimeOffset.Now:O}] {status}: {message}{Environment.NewLine}", maxLogBytes);

    public static async Task AppendRecoveryLogAsync(string logPath, string message, long maxLogBytes)
        => await BoundedLogFile.AppendAsync(logPath, $"[{DateTimeOffset.Now:O}] Recovery: {message}{Environment.NewLine}", maxLogBytes);

    public static async Task StampManagedMetadataAsync(string installDir, RuntimeBuildPreset preset, bool update)
    {
        var metadataPath = Path.Combine(installDir, "local-llm-runtime.json");
        var metadata = File.Exists(metadataPath)
            ? JsonNode.Parse(await File.ReadAllTextAsync(metadataPath))?.AsObject() ?? new JsonObject()
            : new JsonObject();
        metadata["managedPresetId"] = preset.Id;
        metadata["managedPresetLabel"] = preset.Label;
        metadata["managedMode"] = RuntimeBuildCatalogService.ModeKey(preset.Mode);
        metadata["managedAction"] = update ? "update" : "build";
        metadata["packageSource"] = "Local source build";
        metadata["repoUrl"] = preset.RepoUrl;
        metadata["sourceUrl"] = preset.RepoUrl;
        metadata["runtimeVersion"] = metadata["commit"]?.ToString() ?? preset.Branch;
        metadata["checksumStatus"] = "not-applicable-local-build";
        metadata["signatureStatus"] = "not-applicable-local-build";
        metadata["managedInstalledAt"] = DateTimeOffset.UtcNow.ToString("O");
        await RuntimeInstallationVerificationService.StampManifestAsync(installDir, metadata);
        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    public static string RedactCommandArgument(string value)
        => value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            ? RedactUrl(value)
            : value;

    public static string RedactUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        if (string.IsNullOrWhiteSpace(uri.UserInfo)) return value;
        var builder = new UriBuilder(uri) { UserName = "redacted", Password = "redacted" };
        return builder.Uri.ToString();
    }
}
