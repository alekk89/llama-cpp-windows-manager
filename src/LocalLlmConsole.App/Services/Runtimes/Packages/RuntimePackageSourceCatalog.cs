namespace LocalLlmConsole.Services;

public static class RuntimePackageSourceCatalog
{
    public const string LatestReleaseApiUrl = "https://api.github.com/repos/ggml-org/llama.cpp/releases/latest";
    public const string ReleasesUrl = "https://github.com/ggml-org/llama.cpp/releases";
    public const string OfficialRepositoryUrl = "https://github.com/ggml-org/llama.cpp";
    public const string AtomicTurboQuantRepositoryUrl = "https://github.com/AtomicBot-ai/atomic-llama-cpp-turboquant";
    public const string AtomicTurboQuantHuggingFaceApiUrl = "https://huggingface.co/api/models/atomicmilkshake/llama-cpp-turboquant-binaries";
    public const string AtomicTurboQuantHuggingFacePageUrl = "https://huggingface.co/atomicmilkshake/llama-cpp-turboquant-binaries";
    public const string TheTomTurboQuantRepositoryUrl = "https://github.com/TheTom/llama-cpp-turboquant";
    public const string TheTomTurboQuantReleaseApiUrl = "https://api.github.com/repos/TheTom/llama-cpp-turboquant/releases/latest";
    public const string TheTomTurboQuantReleasesUrl = "https://github.com/TheTom/llama-cpp-turboquant/releases";

    private static readonly RuntimePackagePreset[] DefaultPresets =
    [
        new("official-prebuilt-windows-cuda", "Official llama.cpp CUDA Windows", RuntimeBackend.Cuda, RuntimeMode.Native, "official-windows-cuda"),
        new("official-prebuilt-cuda", "Official llama.cpp CUDA WSL", RuntimeBackend.Cuda, RuntimeMode.Wsl, "official-cuda"),
        new("official-prebuilt-windows-vulkan", "Official llama.cpp Vulkan Windows", RuntimeBackend.Vulkan, RuntimeMode.Native, "official-windows-vulkan"),
        new("official-prebuilt-vulkan", "Official llama.cpp Vulkan WSL", RuntimeBackend.Vulkan, RuntimeMode.Wsl, "official-vulkan"),
        new("official-prebuilt-windows-sycl", "Official llama.cpp SYCL Windows (Intel Arc)", RuntimeBackend.Sycl, RuntimeMode.Native, "official-windows-sycl"),
        new("official-prebuilt-sycl", "Official llama.cpp SYCL WSL (Intel Arc)", RuntimeBackend.Sycl, RuntimeMode.Wsl, "official-sycl"),
        new("official-prebuilt-windows-cpu", "Official llama.cpp CPU Windows", RuntimeBackend.Cpu, RuntimeMode.Native, "official-windows-cpu"),
        new("official-prebuilt-cpu", "Official llama.cpp CPU WSL", RuntimeBackend.Cpu, RuntimeMode.Wsl, "official-cpu"),
        new("atomic-prebuilt-windows-cuda", "Atomic llama.cpp TurboQuant CUDA Windows", RuntimeBackend.Cuda, RuntimeMode.Native, "atomic-windows-turboquant-cuda", AtomicTurboQuantHuggingFaceApiUrl, AtomicTurboQuantHuggingFacePageUrl, "Atomic llama.cpp prebuilt", "atomic-prebuilt", AtomicTurboQuantRepositoryUrl),
        new("atomic-prebuilt-cuda", "Atomic llama.cpp TurboQuant CUDA WSL", RuntimeBackend.Cuda, RuntimeMode.Wsl, "atomic-turboquant-cuda", AtomicTurboQuantHuggingFaceApiUrl, AtomicTurboQuantHuggingFacePageUrl, "Atomic llama.cpp prebuilt", "atomic-prebuilt", AtomicTurboQuantRepositoryUrl),
        new("thetom-prebuilt-windows-cuda", "TheTom TurboQuant CUDA Windows", RuntimeBackend.Cuda, RuntimeMode.Native, "thetom-windows-turboquant-cuda", TheTomTurboQuantReleaseApiUrl, TheTomTurboQuantReleasesUrl, "TheTom TurboQuant prebuilt", "thetom-prebuilt", TheTomTurboQuantRepositoryUrl),
        new("thetom-prebuilt-vulkan", "TheTom TurboQuant Vulkan WSL", RuntimeBackend.Vulkan, RuntimeMode.Wsl, "thetom-turboquant-vulkan", TheTomTurboQuantReleaseApiUrl, TheTomTurboQuantReleasesUrl, "TheTom TurboQuant prebuilt", "thetom-prebuilt", TheTomTurboQuantRepositoryUrl),
        new("thetom-prebuilt-cpu", "TheTom TurboQuant CPU WSL", RuntimeBackend.Cpu, RuntimeMode.Wsl, "thetom-turboquant-cpu", TheTomTurboQuantReleaseApiUrl, TheTomTurboQuantReleasesUrl, "TheTom TurboQuant prebuilt", "thetom-prebuilt", TheTomTurboQuantRepositoryUrl)
    ];

    public static IReadOnlyList<RuntimePackagePreset> PresetRows() => DefaultPresets;

    public static string ReleaseApiUrlFor(RuntimePackagePreset? preset)
        => string.IsNullOrWhiteSpace(preset?.ReleaseApiUrl) ? LatestReleaseApiUrl : preset.ReleaseApiUrl;

    public static string ReleasePageUrlFor(RuntimePackagePreset? preset)
        => string.IsNullOrWhiteSpace(preset?.ReleasePageUrl) ? ReleasesUrl : preset.ReleasePageUrl;

    public static string RepositoryUrlFor(RuntimePackagePreset preset)
        => string.IsNullOrWhiteSpace(preset.RepositoryUrl) ? OfficialRepositoryUrl : preset.RepositoryUrl;

    public static string PackageSourceLabel(RuntimePackagePreset preset)
        => string.IsNullOrWhiteSpace(preset.PackageSourceLabel) ? "official llama.cpp" : preset.PackageSourceLabel;

    public static string PackageSourceKey(RuntimePackagePreset preset)
        => string.IsNullOrWhiteSpace(preset.PackageSourceKey) ? "official-prebuilt" : preset.PackageSourceKey;

    public static string PackageRuntimeLabel(RuntimePackagePreset preset)
        => IsOfficialPackage(preset) ? "official prebuilt llama.cpp runtime" : $"{PackageSourceLabel(preset)} runtime";

    public static bool IsOfficialPackage(RuntimePackagePreset preset)
        => string.IsNullOrWhiteSpace(preset.PackageSourceKey)
            || preset.PackageSourceKey.Equals("official-prebuilt", StringComparison.OrdinalIgnoreCase);

    public static string BackendLabel(RuntimePackagePreset preset)
        => $"{BackendName(preset.Backend)} {RuntimeBuildCatalogService.ModeLabel(preset.Mode)}";

    private static string BackendName(RuntimeBackend backend) => backend switch
    {
        RuntimeBackend.Cuda => "CUDA",
        RuntimeBackend.Vulkan => "Vulkan",
        RuntimeBackend.Sycl => "SYCL",
        _ => "CPU"
    };
}
