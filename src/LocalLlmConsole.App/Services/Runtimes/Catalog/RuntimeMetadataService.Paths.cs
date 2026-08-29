namespace LocalLlmConsole.Services;

public static partial class RuntimeMetadataService
{
    public static string Folder(RuntimeRecord runtime)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            var folder = metadata?["folder"]?.ToString();
            if (!string.IsNullOrWhiteSpace(folder)) return NormalizeFolder(folder);
        }
        catch
        {
            // Fall back to executable location below.
        }

        var parent = Path.GetDirectoryName(runtime.ExecutablePath) ?? "";
        return NormalizeFolder(parent);
    }

    public static string PackagedMetadataText(string folder)
    {
        try
        {
            var metadataPath = Path.Combine(folder, "local-llm-runtime.json");
            if (!File.Exists(metadataPath)) return "";
            var metadata = JsonNode.Parse(File.ReadAllText(metadataPath));
            return string.Join(" ", new[]
            {
                metadata?["managedPresetId"]?.ToString() ?? "",
                metadata?["repoUrl"]?.ToString() ?? "",
                metadata?["sourcePath"]?.ToString() ?? "",
                metadata?["build"]?.ToString() ?? "",
                metadata?["name"]?.ToString() ?? "",
                metadata?["source"]?.ToString() ?? "",
                metadata?["releaseTag"]?.ToString() ?? "",
                metadata?["managedPackageId"]?.ToString() ?? "",
                metadata?["runtimeFingerprint"]?.ToString() ?? ""
            });
        }
        catch
        {
            return "";
        }
    }

    public static string NormalizeFolder(string folder)
    {
        if (!Path.GetFileName(folder).Equals("bin", StringComparison.OrdinalIgnoreCase)) return folder;
        return Path.GetDirectoryName(folder) ?? folder;
    }
}
