namespace LocalLlmConsole.Services;

public sealed record RuntimePackageJobPayload(
    RuntimePackagePreset Preset,
    string Action,
    string InstallDir,
    string Message,
    RuntimeBackend Backend,
    RuntimeMode Mode,
    string SourcePresetId,
    string ReleaseApiUrl,
    string ReleasePageUrl,
    string PackageSourceLabel,
    string PackageSourceKey,
    string RepositoryUrl);

public sealed class RuntimePackageJobService
{
    private readonly JobEngine _jobs;

    public RuntimePackageJobService(JobEngine jobs)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
    }

    public async Task<JobRecord> CreateInstallJobAsync(RuntimePackagePreset preset, CancellationToken cancellationToken = default)
        => await _jobs.CreateAsync("runtime-package-download", Payload(preset, "install", "", "Queued."), cancellationToken);

    public async Task<JobRecord> CreateCheckJobAsync(RuntimePackagePreset preset, CancellationToken cancellationToken = default)
        => await _jobs.CreateAsync("runtime-package-update-check", Payload(preset, "check", "", "Queued."), cancellationToken);

    public async Task UpdateAsync(
        JobRecord job,
        JobStatus status,
        RuntimePackagePreset preset,
        string action,
        string installDir,
        string message,
        long maxLogBytes,
        CancellationToken cancellationToken = default)
    {
        await RuntimeBuildJobService.AppendJobLogAsync(job.LogPath, status, message, maxLogBytes);
        await _jobs.UpdateAsync(job, status, Payload(preset, action, installDir, message), cancellationToken);
    }

    public static string Payload(RuntimePackagePreset preset, string action, string installDir, string message) => JsonSerializer.Serialize(new
    {
        preset = preset.Id,
        label = preset.Label,
        backend = RuntimeBuildCatalogService.BackendKey(preset.Backend),
        mode = RuntimeBuildCatalogService.ModeKey(preset.Mode),
        sourcePresetId = preset.SourcePresetId,
        releaseApiUrl = RuntimePackageSourceCatalog.ReleaseApiUrlFor(preset),
        releasePageUrl = RuntimePackageSourceCatalog.ReleasePageUrlFor(preset),
        packageSourceLabel = RuntimePackageSourceCatalog.PackageSourceLabel(preset),
        packageSourceKey = RuntimePackageSourceCatalog.PackageSourceKey(preset),
        repositoryUrl = RuntimePackageSourceCatalog.RepositoryUrlFor(preset),
        installDir,
        action,
        message
    });

    public static RuntimePackageJobPayload? ParsePayload(string payloadJson)
    {
        try
        {
            var node = JsonNode.Parse(payloadJson);
            if (node is null) return null;
            var presetId = node["preset"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(presetId)) return null;
            var backend = ParseBackend(node["backend"]?.ToString() ?? "");
            var mode = ParseMode(node["mode"]?.ToString() ?? "");
            var preset = new RuntimePackagePreset(
                presetId,
                node["label"]?.ToString() ?? presetId,
                backend,
                mode,
                node["sourcePresetId"]?.ToString() ?? "",
                node["releaseApiUrl"]?.ToString() ?? "",
                node["releasePageUrl"]?.ToString() ?? "",
                node["packageSourceLabel"]?.ToString() ?? "",
                node["packageSourceKey"]?.ToString() ?? "",
                node["repositoryUrl"]?.ToString() ?? "");
            return new RuntimePackageJobPayload(
                preset,
                node["action"]?.ToString() ?? "",
                node["installDir"]?.ToString() ?? "",
                node["message"]?.ToString() ?? "",
                backend,
                mode,
                preset.SourcePresetId,
                RuntimePackageSourceCatalog.ReleaseApiUrlFor(preset),
                RuntimePackageSourceCatalog.ReleasePageUrlFor(preset),
                RuntimePackageSourceCatalog.PackageSourceLabel(preset),
                RuntimePackageSourceCatalog.PackageSourceKey(preset),
                RuntimePackageSourceCatalog.RepositoryUrlFor(preset));
        }
        catch
        {
            return null;
        }
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

    private static RuntimeBackend ParseBackend(string value)
        => (value ?? "").Trim().ToLowerInvariant() switch
        {
            "cuda" => RuntimeBackend.Cuda,
            "vulkan" => RuntimeBackend.Vulkan,
            "sycl" => RuntimeBackend.Sycl,
            _ => RuntimeBackend.Cpu
        };
}
