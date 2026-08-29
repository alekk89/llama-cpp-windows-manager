namespace LocalLlmConsole.Services;

public sealed record RuntimeCustomRepositoryDraft(string Label, string RepoUrl, string Branch, string BackendLabel);

public sealed record RuntimeCustomRepositoryResult(
    bool Success,
    string StatusMessage,
    RuntimeBuildPreset? Preset = null,
    RuntimeBuildPreset? ExistingPreset = null);

public sealed class RuntimeCustomRepositoryService
{
    public static readonly string[] BackendOptions =
    [
        "CPU Windows",
        "CUDA Windows",
        "Vulkan Windows",
        "SYCL Windows",
        "CPU WSL",
        "CUDA WSL",
        "Vulkan WSL",
        "SYCL WSL"
    ];

    public RuntimeCustomRepositoryResult BuildPreset(RuntimeCustomRepositoryDraft draft)
    {
        var label = (draft.Label ?? "").Trim();
        var repoUrl = (draft.RepoUrl ?? "").Trim();
        var branch = (draft.Branch ?? "").Trim();
        var backend = BackendFromLabel(draft.BackendLabel);
        var mode = ModeFromLabel(draft.BackendLabel);
        if (string.IsNullOrWhiteSpace(label))
            return Failed("Enter a display name for the custom runtime repository.");
        if (string.IsNullOrWhiteSpace(repoUrl))
            return Failed("Enter an HTTPS Git repository URL for the custom runtime repository.");

        var preset = new RuntimeBuildPreset(
            RuntimeBuildCatalogService.CustomPresetId(label, repoUrl, branch, RuntimeBuildCatalogService.BackendKey(backend), mode),
            label,
            repoUrl,
            branch,
            backend == RuntimeBackend.Cuda,
            Custom: true,
            RuntimeBuildCatalogService.BackendKey(backend),
            mode);

        if (!RuntimeBuildCatalogService.IsSafeUiCustomPreset(preset))
            return Failed("Custom runtime repository must be an HTTPS Git URL without embedded credentials and with a safe branch/ref. Local, file, and SSH sources are reserved for manual advanced configuration.");

        return new RuntimeCustomRepositoryResult(true, "", preset);
    }

    public async Task<RuntimeCustomRepositoryResult> AddAsync(
        string runtimeRoot,
        RuntimeCustomRepositoryDraft draft,
        CancellationToken cancellationToken = default)
    {
        var built = BuildPreset(draft);
        if (!built.Success || built.Preset is null)
            return built;

        var existing = RuntimeBuildCatalogService.PresetRows(runtimeRoot)
            .FirstOrDefault(candidate => RuntimeBuildCatalogService.SameRepository(candidate, built.Preset));
        if (existing is not null)
        {
            return new RuntimeCustomRepositoryResult(
                false,
                $"That repository is already listed as {existing.Label}.",
                built.Preset,
                existing);
        }

        var customPresets = RuntimeBuildCatalogService.ReadCustomPresets(runtimeRoot).ToList();
        customPresets.Add(built.Preset);
        await RuntimeBuildCatalogService.SaveCustomPresetsAsync(runtimeRoot, customPresets, cancellationToken);
        return new RuntimeCustomRepositoryResult(
            true,
            $"Added custom runtime repository: {built.Preset.Label}",
            built.Preset);
    }

    public static RuntimeBackend BackendFromLabel(string label)
    {
        if (label.Contains("sycl", StringComparison.OrdinalIgnoreCase)
            || label.Contains("intel", StringComparison.OrdinalIgnoreCase))
            return RuntimeBackend.Sycl;
        if (label.Contains("vulkan", StringComparison.OrdinalIgnoreCase)) return RuntimeBackend.Vulkan;
        if (label.Contains("cuda", StringComparison.OrdinalIgnoreCase)) return RuntimeBackend.Cuda;
        return RuntimeBackend.Cpu;
    }

    public static RuntimeMode ModeFromLabel(string label)
        => label.Contains("windows", StringComparison.OrdinalIgnoreCase)
            || label.Contains("native", StringComparison.OrdinalIgnoreCase)
                ? RuntimeMode.Native
                : RuntimeMode.Wsl;

    private static RuntimeCustomRepositoryResult Failed(string message)
        => new(false, message);
}
