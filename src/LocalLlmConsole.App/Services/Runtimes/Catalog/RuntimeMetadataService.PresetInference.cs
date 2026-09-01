namespace LocalLlmConsole.Services;

public static partial class RuntimeMetadataService
{
    public static string ManagedPresetId(RuntimeRecord runtime)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            var explicitId = metadata?["managedPresetId"]?.ToString()
                ?? metadata?["runtimeMetadata"]?["managedPresetId"]?.ToString()
                ?? metadata?["packaged"]?["managedPresetId"]?.ToString()
                ?? "";
            if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId;

            var text = string.Join(" ", new[]
            {
                runtime.Name,
                runtime.ExecutablePath,
                metadata?["folder"]?.ToString() ?? "",
                metadata?["repoUrl"]?.ToString() ?? "",
                metadata?["runtimeMetadata"]?["repoUrl"]?.ToString() ?? "",
                metadata?["runtimeMetadata"]?["sourcePath"]?.ToString() ?? "",
                metadata?["runtimeMetadata"]?["build"]?.ToString() ?? "",
                metadata?["runtimeMetadata"]?["name"]?.ToString() ?? "",
                metadata?["runtimeMetadata"]?["source"]?.ToString() ?? "",
                metadata?["runtimeMetadata"]?["releaseTag"]?.ToString() ?? "",
                PackagedMetadataText(Folder(runtime))
            }).Replace('\\', '/');

            var isNative = runtime.Mode == RuntimeMode.Native
                || text.Contains(" native", StringComparison.OrdinalIgnoreCase)
                || text.Contains("runtime-native", StringComparison.OrdinalIgnoreCase);
            if (text.Contains("AtomicBot-ai/atomic-llama-cpp-turboquant", StringComparison.OrdinalIgnoreCase)
                || text.Contains("atomic-llama-cpp-turboquant", StringComparison.OrdinalIgnoreCase))
                return isNative ? "atomic-windows-turboquant-cuda" : "atomic-turboquant-cuda";
            if (text.Contains("TheTom/llama-cpp-turboquant", StringComparison.OrdinalIgnoreCase))
                return ForkPresetId("thetom-turboquant", runtime.Backend, isNative);
            if (text.Contains("ikawrakow/ik_llama.cpp", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ik_llama.cpp", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ik-llama", StringComparison.OrdinalIgnoreCase))
                return ForkPresetId("ik-llama", runtime.Backend, isNative);
            if (text.Contains("ggml-org/llama.cpp", StringComparison.OrdinalIgnoreCase)
                || text.Contains("ggerganov/llama.cpp", StringComparison.OrdinalIgnoreCase)
                || text.Contains("llama.cpp", StringComparison.OrdinalIgnoreCase))
            {
                if (runtime.Backend == RuntimeBackend.Rocm || text.Contains("rocm", StringComparison.OrdinalIgnoreCase)) return isNative ? "official-windows-rocm" : "official-rocm";
                if (runtime.Backend == RuntimeBackend.Sycl || text.Contains("sycl", StringComparison.OrdinalIgnoreCase)) return isNative ? "official-windows-sycl" : "official-sycl";
                if (runtime.Backend == RuntimeBackend.Cuda || text.Contains("cuda", StringComparison.OrdinalIgnoreCase)) return isNative ? "official-windows-cuda" : "official-cuda";
                if (runtime.Backend == RuntimeBackend.Vulkan || text.Contains("vulkan", StringComparison.OrdinalIgnoreCase)) return isNative ? "official-windows-vulkan" : "official-vulkan";
                return isNative ? "official-windows-cpu" : "official-cpu";
            }

            return "";
        }
        catch
        {
            return "";
        }
    }

    private static string ForkPresetId(string family, RuntimeBackend backend, bool isNative)
        => (family, backend, isNative) switch
        {
            ("ik-llama", RuntimeBackend.Cuda, true) => "ik-windows-cuda",
            ("ik-llama", RuntimeBackend.Cuda, false) => "ik-llama-cuda",
            ("ik-llama", RuntimeBackend.Cpu, true) => "ik-windows-cpu",
            ("ik-llama", RuntimeBackend.Cpu, false) => "ik-llama-cpu",
            ("thetom-turboquant", RuntimeBackend.Cuda, true) => "thetom-windows-turboquant-cuda",
            ("thetom-turboquant", RuntimeBackend.Cuda, false) => "thetom-turboquant-cuda",
            ("thetom-turboquant", RuntimeBackend.Vulkan, false) => "thetom-turboquant-vulkan",
            ("thetom-turboquant", RuntimeBackend.Cpu, false) => "thetom-turboquant-cpu",
            _ => ""
        };
}
