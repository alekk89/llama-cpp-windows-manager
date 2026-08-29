namespace LocalLlmConsole.Services;

public static partial class RuntimeBuildCatalogService
{
    public static RuntimeBackend BuildBackend(RuntimeBuildPreset preset)
        => ParseBuildBackend(preset.Backend, preset.Cuda);

    public static RuntimeBackend BuildBackend(RuntimeSourceEntry source)
        => ParseBuildBackend(source.Backend, source.Cuda);

    public static string BackendKey(RuntimeBuildPreset preset)
        => BackendKey(BuildBackend(preset));

    public static string BackendKey(RuntimeBackend backend) => backend switch
    {
        RuntimeBackend.Cuda => "cuda",
        RuntimeBackend.Vulkan => "vulkan",
        RuntimeBackend.Sycl => "sycl",
        _ => "cpu"
    };

    public static RuntimeMode BuildMode(RuntimeBuildPreset preset) => NormalizeBuildMode(preset.Mode);

    public static RuntimeMode BuildMode(RuntimeSourceEntry source) => NormalizeBuildMode(source.Mode);

    public static RuntimeMode NormalizeBuildMode(RuntimeMode mode)
        => mode == RuntimeMode.Native ? RuntimeMode.Native : RuntimeMode.Wsl;

    public static string ModeKey(RuntimeMode mode)
        => NormalizeBuildMode(mode) == RuntimeMode.Native ? "native" : "wsl";

    private static string BackendName(RuntimeBackend backend) => backend switch
    {
        RuntimeBackend.Cuda => "CUDA",
        RuntimeBackend.Vulkan => "Vulkan",
        RuntimeBackend.Sycl => "SYCL",
        _ => "CPU"
    };

    public static string NormalizeBuildBackend(string backend)
        => (backend ?? "").Trim().ToLowerInvariant() switch
        {
            "cuda" => "cuda",
            "vulkan" => "vulkan",
            "sycl" => "sycl",
            _ => "cpu"
        };

    private static RuntimeBackend ParseBuildBackend(string backend, bool cuda)
        => NormalizeBuildBackend(backend) switch
        {
            "cuda" => RuntimeBackend.Cuda,
            "vulkan" => RuntimeBackend.Vulkan,
            "sycl" => RuntimeBackend.Sycl,
            _ when cuda => RuntimeBackend.Cuda,
            _ => RuntimeBackend.Cpu
        };
}
