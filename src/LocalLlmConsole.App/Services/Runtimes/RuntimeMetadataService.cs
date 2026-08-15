namespace LocalLlmConsole.Services;

public static partial class RuntimeMetadataService
{
    public static bool IsManagedSourceBuild(RuntimeRecord runtime)
    {
        if (!string.IsNullOrWhiteSpace(ManagedPackageId(runtime))) return false;

        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            if (HasManagedSourceIdentity(metadata) || HasManagedSourceIdentity(metadata?["runtimeMetadata"]))
                return true;
        }
        catch
        {
            // Try the runtime's stamped metadata file below.
        }

        try
        {
            var metadataPath = Path.Combine(Folder(runtime), "local-llm-runtime.json");
            return File.Exists(metadataPath) && HasManagedSourceIdentity(JsonNode.Parse(File.ReadAllText(metadataPath)));
        }
        catch
        {
            return false;
        }
    }

    public static string ManagedPackageId(RuntimeRecord runtime)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            return metadata?["managedPackageId"]?.ToString()
                ?? metadata?["runtimeMetadata"]?["managedPackageId"]?.ToString()
                ?? "";
        }
        catch
        {
            return "";
        }
    }

    public static string PackageTag(RuntimeRecord runtime)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            var tag = metadata?["releaseTag"]?.ToString()
                ?? metadata?["runtimeMetadata"]?["releaseTag"]?.ToString()
                ?? "";
            if (!string.IsNullOrWhiteSpace(tag)) return tag;
        }
        catch
        {
            // Try packaged metadata below.
        }

        try
        {
            var metadataPath = Path.Combine(Folder(runtime), "local-llm-runtime.json");
            if (File.Exists(metadataPath))
            {
                var tag = JsonNode.Parse(File.ReadAllText(metadataPath))?["releaseTag"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(tag)) return tag;
            }
        }
        catch
        {
            // No package metadata is available.
        }

        return "";
    }

    public static string PackageAssetSummary(RuntimeRecord runtime)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            var assets = ReadPackageAssetNames(metadata?["assets"]);
            if (assets.Count > 0) return string.Join(", ", assets);

            assets = ReadPackageAssetNames(metadata?["runtimeMetadata"]?["assets"]);
            if (assets.Count > 0) return string.Join(", ", assets);
        }
        catch
        {
            // Try packaged metadata below.
        }

        try
        {
            var metadataPath = Path.Combine(Folder(runtime), "local-llm-runtime.json");
            if (File.Exists(metadataPath))
            {
                var assets = ReadPackageAssetNames(JsonNode.Parse(File.ReadAllText(metadataPath))?["assets"]);
                if (assets.Count > 0) return string.Join(", ", assets);
            }
        }
        catch
        {
            // No package metadata is available.
        }

        return "";
    }

    public static string RuntimeFingerprint(RuntimeRecord runtime)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            var fingerprint = metadata?["runtimeFingerprint"]?.ToString()
                ?? metadata?["runtimeMetadata"]?["runtimeFingerprint"]?.ToString()
                ?? "";
            if (!string.IsNullOrWhiteSpace(fingerprint)) return fingerprint;
        }
        catch
        {
            // Try packaged metadata below.
        }

        try
        {
            var metadataPath = Path.Combine(Folder(runtime), "local-llm-runtime.json");
            if (File.Exists(metadataPath))
            {
                var fingerprint = JsonNode.Parse(File.ReadAllText(metadataPath))?["runtimeFingerprint"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(fingerprint)) return fingerprint;
            }
        }
        catch
        {
            // No fingerprint metadata is available.
        }

        return "";
    }

    public static IReadOnlyList<string> EquivalentPackageIds(RuntimeRecord runtime)
        => ReadStringArray(runtime, "equivalentPackageIds");

    public static IReadOnlyList<string> EquivalentSourcePresetIds(RuntimeRecord runtime)
        => ReadStringArray(runtime, "equivalentSourcePresetIds");

    private static IReadOnlyList<string> ReadStringArray(RuntimeRecord runtime, string propertyName)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            var direct = ReadArray(metadata?[propertyName]);
            if (direct.Count > 0) return direct;
            var packaged = ReadArray(metadata?["runtimeMetadata"]?[propertyName]);
            if (packaged.Count > 0) return packaged;
        }
        catch
        {
            // Try packaged metadata below.
        }

        try
        {
            var metadataPath = Path.Combine(Folder(runtime), "local-llm-runtime.json");
            if (File.Exists(metadataPath))
                return ReadArray(JsonNode.Parse(File.ReadAllText(metadataPath))?[propertyName]);
        }
        catch
        {
            // No aliases are available.
        }

        return [];
    }

    private static IReadOnlyList<string> ReadArray(JsonNode? node)
    {
        if (node is JsonArray array)
        {
            return array
                .Select(item => item?.ToString() ?? "")
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        var value = node?.ToString() ?? "";
        return string.IsNullOrWhiteSpace(value) ? [] : [value];
    }

    private static IReadOnlyList<string> ReadPackageAssetNames(JsonNode? node)
    {
        if (node is not JsonArray array) return [];
        return array
            .Select(item => item is JsonObject obj ? obj["name"]?.ToString() ?? "" : item?.ToString() ?? "")
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasManagedSourceIdentity(JsonNode? metadata)
        => !string.IsNullOrWhiteSpace(metadata?["managedPresetId"]?.ToString());
}
