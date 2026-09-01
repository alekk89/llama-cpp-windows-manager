
namespace LocalLlmConsole.Services;

public sealed class RuntimeRegistryService
{
    private static readonly IReadOnlyList<ManagedPresetRule> ManagedPresetRules =
    [
        new(["AtomicBot-ai/atomic-llama-cpp-turboquant", "atomic-llama-cpp-turboquant"],
            "atomic-windows-turboquant-cuda", "atomic-turboquant-cuda"),
        new(["TheTom/llama-cpp-turboquant"], "thetom-windows-turboquant-cuda", "thetom-turboquant-cuda"),
        new(["ikawrakow/ik_llama.cpp", "ik_llama.cpp", "ik-llama"], "ik-windows-cuda", "ik-llama-cuda")
    ];
    private static readonly string[] OfficialRuntimeMarkers =
        ["ggml-org/llama.cpp", "ggerganov/llama.cpp", "llama.cpp"];
    private sealed record RuntimeCandidate(string Folder, string ExecutablePath);

    private readonly StateStore _store;

    public RuntimeRegistryService(StateStore store) => _store = store;

    public async Task<int> ScanAsync(string runtimeRoot)
    {
        Directory.CreateDirectory(runtimeRoot);
        var candidates = await Task.Run(() => CandidateRuntimeFolders(runtimeRoot).Take(1000).ToArray());
        var registered = await _store.ListRuntimesAsync();
        var count = 0;
        foreach (var candidate in candidates)
        {
            var repaired = registered
                .Where(runtime => !RuntimeAvailabilityService.IsAvailable(runtime))
                .Where(runtime => SameFolder(RuntimeMetadataService.Folder(runtime), candidate.Folder))
                .ToArray();
            if (repaired.Length > 0)
            {
                foreach (var runtime in repaired)
                {
                    var updated = runtime with
                    {
                        ExecutablePath = candidate.ExecutablePath,
                        Mode = candidate.ExecutablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                            ? RuntimeMode.Native
                            : RuntimeMode.Wsl,
                        UpdatedAt = DateTimeOffset.UtcNow
                    };
                    await _store.UpsertRuntimeAsync(updated);
                }
            }
            else
            {
                await RegisterFolderAsync(candidate.Folder, candidate.ExecutablePath);
            }
            count++;
        }
        return count;
    }

    private static bool SameFolder(string left, string right)
    {
        try
        {
            return string.Equals(
                Path.GetFullPath(left).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(right).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public async Task<RuntimeRecord> RegisterFolderAsync(string folder)
        => await RegisterFolderAsync(folder, executableHint: "");

    private async Task<RuntimeRecord> RegisterFolderAsync(string folder, string executableHint)
    {
        var record = await Task.Run(() => CreateRuntimeRecord(folder, executableHint));
        await _store.UpsertRuntimeAsync(record);
        return record;
    }

    private static RuntimeRecord CreateRuntimeRecord(string folder, string executableHint)
    {
        var full = NormalizeRuntimeFolder(Path.GetFullPath(folder));
        var executable = IsUsableExecutableHint(full, executableHint)
            ? Path.GetFullPath(executableHint)
            : FindLlamaServer(full) ?? throw new InvalidOperationException("No llama-server or llama-server.exe was found in that folder or its bin folder.");
        var packaged = ReadPackagedMetadata(full);
        var backend = InferBackend(full, executable, packaged);
        var managedPresetId = InferManagedPresetId(full, executable, backend, packaged);
        var metadataRuntime = packaged?["runtime"]?.ToString();
        var mode = string.Equals(metadataRuntime, "native", StringComparison.OrdinalIgnoreCase)
            ? RuntimeMode.Native
            : executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? RuntimeMode.Native : RuntimeMode.Wsl;
        var id = ModelCatalogService.SafeId($"llama-cpp-{Path.GetFileName(full)}-{backend}");
        var metadata = new JsonObject
        {
            ["folder"] = full,
            ["mode"] = mode.ToString(),
            ["registeredAt"] = DateTimeOffset.UtcNow.ToString("O")
        };
        var packagedName = packaged?["name"]?.ToString();
        if (!string.IsNullOrWhiteSpace(managedPresetId)) metadata["managedPresetId"] = managedPresetId;
        if (packaged is not null) metadata["runtimeMetadata"] = packaged.DeepClone();

        var displayName = string.IsNullOrWhiteSpace(packagedName)
            ? $"llama.cpp {Path.GetFileName(full)} {mode} {backend}"
            : $"{packagedName} ({Path.GetFileName(full)})";
        return new RuntimeRecord(id, displayName, mode, backend, executable, metadata.ToJsonString(), DateTimeOffset.UtcNow);
    }

    private static string NormalizeRuntimeFolder(string folder)
    {
        if (!Path.GetFileName(folder).Equals("bin", StringComparison.OrdinalIgnoreCase)) return folder;
        var parent = Path.GetDirectoryName(folder);
        return string.IsNullOrWhiteSpace(parent) ? folder : parent;
    }

    private static JsonObject? ReadPackagedMetadata(string folder)
    {
        var metadataPath = Path.Combine(folder, "local-llm-runtime.json");
        if (!File.Exists(metadataPath)) return null;
        try
        {
            return JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
        }
        catch
        {
            return null;
        }
    }

    private static RuntimeBackend InferBackend(string folder, string executablePath, JsonObject? metadata)
    {
        if (TryParseBackend(metadata?["backend"]?.ToString(), out var explicitBackend)) return explicitBackend;
        if (HasNearbyRocmMarker(folder)) return RuntimeBackend.Rocm;
        if (HasNearbySyclMarker(folder)) return RuntimeBackend.Sycl;
        if (HasNearbyCudaMarker(folder)) return RuntimeBackend.Cuda;
        if (HasNearbyVulkanMarker(folder)) return RuntimeBackend.Vulkan;
        if (TryInferBackendFromManagedMetadata(metadata, out var managedBackend)) return managedBackend;
        return RuntimeBackend.Cpu;
    }

    private static bool TryInferBackendFromManagedMetadata(JsonObject? metadata, out RuntimeBackend backend)
    {
        backend = RuntimeBackend.Cpu;
        if (metadata is null) return false;

        var values = new List<string>
        {
            metadata["build"]?.ToString() ?? "",
            metadata["name"]?.ToString() ?? "",
            metadata["managedPresetId"]?.ToString() ?? "",
            metadata["managedPackageId"]?.ToString() ?? ""
        };
        if (metadata["tags"] is JsonArray tags)
            values.AddRange(tags.Select(tag => tag?.ToString() ?? ""));

        var text = string.Join(" ", values);
        if (ContainsBackendToken(text, "rocm") || ContainsBackendToken(text, "hip")) { backend = RuntimeBackend.Rocm; return true; }
        if (ContainsBackendToken(text, "sycl")) { backend = RuntimeBackend.Sycl; return true; }
        if (ContainsBackendToken(text, "cuda")) { backend = RuntimeBackend.Cuda; return true; }
        if (ContainsBackendToken(text, "vulkan")) { backend = RuntimeBackend.Vulkan; return true; }
        return false;
    }

    private static bool TryParseBackend(string? value, out RuntimeBackend backend)
    {
        if (Enum.TryParse(value, ignoreCase: true, out backend)) return true;
        backend = RuntimeBackend.Cpu;
        return false;
    }

    private static bool ContainsBackendToken(string text, string token)
        => text.Split([' ', '\t', '\r', '\n', '-', '_', '.', '/', '\\', ':', ';', ',', '(', ')', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Any(part => string.Equals(part, token, StringComparison.OrdinalIgnoreCase));

    private static string RuntimeMetadataText(JsonObject? metadata)
    {
        if (metadata is null) return "";
        var values = new List<string>
        {
            metadata["build"]?.ToString() ?? "",
            metadata["backend"]?.ToString() ?? "",
            metadata["name"]?.ToString() ?? "",
            metadata["repoUrl"]?.ToString() ?? "",
            metadata["sourcePath"]?.ToString() ?? "",
            metadata["source"]?.ToString() ?? "",
            metadata["releaseTag"]?.ToString() ?? "",
            metadata["managedPackageId"]?.ToString() ?? ""
        };
        if (metadata["tags"] is JsonArray tags)
        {
            values.AddRange(tags.Select(tag => tag?.ToString() ?? ""));
        }
        return string.Join(" ", values);
    }

    private static string InferManagedPresetId(string folder, string executablePath, RuntimeBackend backend, JsonObject? metadata)
    {
        var explicitId = metadata?["managedPresetId"]?.ToString();
        if (!string.IsNullOrWhiteSpace(explicitId)) return explicitId;

        var text = $"{RuntimeMetadataText(metadata)} {folder}".Replace('\\', '/');
        var isNative = string.Equals(metadata?["runtime"]?.ToString(), "native", StringComparison.OrdinalIgnoreCase)
            || executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        var managed = ManagedPresetRules.FirstOrDefault(rule => rule.Markers.Any(marker =>
            text.Contains(marker, StringComparison.OrdinalIgnoreCase)));
        if (managed is not null)
        {
            if (managed.Markers.Any(marker => marker.Contains("ik_llama", StringComparison.OrdinalIgnoreCase)
                || marker.Contains("ik-llama", StringComparison.OrdinalIgnoreCase)))
                return backend == RuntimeBackend.Cuda
                    ? isNative ? "ik-windows-cuda" : "ik-llama-cuda"
                    : backend == RuntimeBackend.Cpu
                        ? isNative ? "ik-windows-cpu" : "ik-llama-cpu"
                        : "";
            if (managed.Markers.Any(marker => marker.Contains("TheTom", StringComparison.OrdinalIgnoreCase)))
            {
                if (backend == RuntimeBackend.Cuda) return isNative ? "thetom-windows-turboquant-cuda" : "thetom-turboquant-cuda";
                if (backend == RuntimeBackend.Vulkan && !isNative) return "thetom-turboquant-vulkan";
                if (backend == RuntimeBackend.Cpu && !isNative) return "thetom-turboquant-cpu";
                return "";
            }
            return isNative ? managed.NativePresetId : managed.PortablePresetId;
        }

        if (OfficialRuntimeMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
        {
            if (backend == RuntimeBackend.Rocm) return isNative ? "official-windows-rocm" : "official-rocm";
            if (backend == RuntimeBackend.Sycl) return isNative ? "official-windows-sycl" : "official-sycl";
            if (backend == RuntimeBackend.Cuda) return isNative ? "official-windows-cuda" : "official-cuda";
            if (backend == RuntimeBackend.Vulkan) return isNative ? "official-windows-vulkan" : "official-vulkan";
            return isNative ? "official-windows-cpu" : "official-cpu";
        }

        return "";
    }

    private sealed record ManagedPresetRule(
        IReadOnlyList<string> Markers,
        string NativePresetId,
        string PortablePresetId);

    private static IEnumerable<RuntimeCandidate> CandidateRuntimeFolders(string root)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fullRoot = Path.GetFullPath(root);
        var directRootExecutable = FindLlamaServer(fullRoot, recursive: false);
        if (!string.IsNullOrWhiteSpace(directRootExecutable))
        {
            var folder = NormalizeRuntimeFolder(Path.GetDirectoryName(directRootExecutable) ?? fullRoot);
            if (seen.Add(folder)) yield return new RuntimeCandidate(folder, directRootExecutable);
        }

        foreach (var executable in Directory.EnumerateFiles(root, "llama-server*", SafeRecursiveEnumeration())
                     .Where(file => Path.GetFileName(file).Equals("llama-server", StringComparison.OrdinalIgnoreCase)
                         || Path.GetFileName(file).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase))
                     .Take(1000))
        {
            var folder = Path.GetDirectoryName(executable);
            if (folder is null) continue;
            folder = NormalizeRuntimeFolder(folder);
            if (!string.IsNullOrWhiteSpace(folder) && seen.Add(Path.GetFullPath(folder)))
                yield return new RuntimeCandidate(Path.GetFullPath(folder), Path.GetFullPath(executable));
        }
    }

    private static string? FindLlamaServer(string folder, bool recursive = true)
    {
        var direct = Path.Combine(folder, "llama-server.exe");
        if (File.Exists(direct)) return direct;
        var bin = Path.Combine(folder, "bin", "llama-server.exe");
        if (File.Exists(bin)) return bin;
        var wslDirect = Path.Combine(folder, "llama-server");
        if (File.Exists(wslDirect)) return wslDirect;
        var wslBin = Path.Combine(folder, "bin", "llama-server");
        if (File.Exists(wslBin)) return wslBin;
        if (!recursive) return null;
        return Directory.EnumerateFiles(folder, "llama-server*", SafeRecursiveEnumeration())
            .FirstOrDefault(file => Path.GetFileName(file).Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(file).Equals("llama-server", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUsableExecutableHint(string runtimeFolder, string executableHint)
    {
        if (string.IsNullOrWhiteSpace(executableHint) || !File.Exists(executableHint)) return false;
        var name = Path.GetFileName(executableHint);
        if (!name.Equals("llama-server", StringComparison.OrdinalIgnoreCase)
            && !name.Equals("llama-server.exe", StringComparison.OrdinalIgnoreCase))
            return false;

        var executableFolder = NormalizeRuntimeFolder(Path.GetDirectoryName(Path.GetFullPath(executableHint)) ?? "");
        return string.Equals(Path.GetFullPath(runtimeFolder), Path.GetFullPath(executableFolder), StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasNearbyCudaMarker(string folder) => HasNearbyMarker(folder, "cuda");

    private static bool HasNearbyRocmMarker(string folder)
    {
        if (folder.Contains("rocm", StringComparison.OrdinalIgnoreCase)) return true;
        foreach (var candidate in new[] { folder, Path.Combine(folder, "bin"), Path.Combine(folder, "lib") })
        {
            if (!Directory.Exists(candidate)) continue;
            if (Directory.EnumerateFiles(candidate, "*", SearchOption.TopDirectoryOnly).Any(file =>
                Path.GetFileName(file) is var name
                && (name.StartsWith("ggml-hip", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("amdhip", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("hipblas", StringComparison.OrdinalIgnoreCase)
                    || name.StartsWith("rocblas", StringComparison.OrdinalIgnoreCase))))
                return true;
        }
        return false;
    }

    private static bool HasNearbySyclMarker(string folder) => HasNearbyMarker(folder, "sycl");

    private static bool HasNearbyVulkanMarker(string folder) => HasNearbyMarker(folder, "vulkan");

    private static bool HasNearbyMarker(string folder, string marker)
    {
        foreach (var candidate in new[] { folder, Path.Combine(folder, "bin"), Path.Combine(folder, "lib") })
        {
            if (!Directory.Exists(candidate)) continue;
            if (Directory.EnumerateFiles(candidate, $"*{marker}*", SearchOption.TopDirectoryOnly).Any())
                return true;
        }

        return false;
    }

    private static EnumerationOptions SafeRecursiveEnumeration() => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };
}
