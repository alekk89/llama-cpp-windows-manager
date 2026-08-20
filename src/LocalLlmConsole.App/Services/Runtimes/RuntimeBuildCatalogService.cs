namespace LocalLlmConsole.Services;

public static partial class RuntimeBuildCatalogService
{
    public static readonly RuntimeBuildPreset[] DefaultPresets =
    [
        new("official-windows-cuda", "Official llama.cpp CUDA Windows", "https://github.com/ggml-org/llama.cpp.git", "master", true, Mode: RuntimeMode.Native),
        new("official-cuda", "Official llama.cpp CUDA WSL", "https://github.com/ggml-org/llama.cpp.git", "master", true),
        new("official-windows-vulkan", "Official llama.cpp Vulkan Windows", "https://github.com/ggml-org/llama.cpp.git", "master", false, Backend: "vulkan", Mode: RuntimeMode.Native),
        new("official-vulkan", "Official llama.cpp Vulkan WSL", "https://github.com/ggml-org/llama.cpp.git", "master", false, Backend: "vulkan"),
        new("official-windows-sycl", "Official llama.cpp SYCL Windows (Intel Arc)", "https://github.com/ggml-org/llama.cpp.git", "master", false, Backend: "sycl", Mode: RuntimeMode.Native),
        new("official-sycl", "Official llama.cpp SYCL WSL (Intel Arc)", "https://github.com/ggml-org/llama.cpp.git", "master", false, Backend: "sycl"),
        new("official-windows-cpu", "Official llama.cpp CPU Windows", "https://github.com/ggml-org/llama.cpp.git", "master", false, Mode: RuntimeMode.Native),
        new("official-cpu", "Official llama.cpp CPU WSL", "https://github.com/ggml-org/llama.cpp.git", "master", false),
        new("atomic-windows-turboquant-cuda", "Atomic TurboQuant CUDA Windows", "https://github.com/AtomicBot-ai/atomic-llama-cpp-turboquant.git", "", true, Mode: RuntimeMode.Native),
        new("atomic-turboquant-cuda", "Atomic TurboQuant CUDA WSL", "https://github.com/AtomicBot-ai/atomic-llama-cpp-turboquant.git", "", true),
        new("ik-llama-cuda", "ik_llama.cpp CUDA", "https://github.com/ikawrakow/ik_llama.cpp.git", "", true),
        new("thetom-turboquant-cuda", "TheTom TurboQuant CUDA", "https://github.com/TheTom/llama-cpp-turboquant.git", "", true)
    ];

    public static IReadOnlyList<RuntimeBuildPreset> PresetRows(string runtimeRoot)
    {
        var rows = new List<RuntimeBuildPreset>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var preset in DefaultPresets.Concat(ReadCustomPresets(runtimeRoot)))
        {
            if (seen.Add(preset.Id))
                rows.Add(preset);
        }
        return rows;
    }
}
