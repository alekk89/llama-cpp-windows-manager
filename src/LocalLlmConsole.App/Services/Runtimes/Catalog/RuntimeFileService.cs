
namespace LocalLlmConsole.Services;

public static class RuntimeFileService
{
    public static bool CanDeleteRuntimeFiles(RuntimeRecord runtime, string runtimeRoot, out string folder, out string reason)
    {
        folder = RuntimeMetadataService.Folder(runtime);
        reason = "";
        if (string.IsNullOrWhiteSpace(folder))
        {
            reason = "The runtime folder is not known.";
            return false;
        }

        if (!Directory.Exists(folder))
        {
            reason = "The runtime folder no longer exists.";
            return false;
        }

        if (IsSafeRuntimeFolder(runtimeRoot, folder)) return true;
        if (IsPackagedRuntimeFolderSafeToDelete(folder)) return true;

        reason = "The runtime is outside the app runtimes folder and does not have an app-managed runtime marker, so its files were left untouched.";
        return false;
    }

    public static bool IsSafeRuntimeFolder(string runtimeRoot, string folder)
        => FileSystemSafetyService.IsSafeChildDirectory(runtimeRoot, folder);

    public static bool IsPackagedRuntimeFolderSafeToDelete(string folder)
    {
        try
        {
            var full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? "";
            if (string.IsNullOrWhiteSpace(root) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return false;
            if (!HasManagedRuntimeMarker(full)) return false;
            if (!HasDirectLlamaServer(full)) return false;
            if (FileSystemSafetyService.ContainsDirectoryReparsePoint(full)) return false;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void DeleteSafeRuntimeFolder(string runtimeRoot, string folder)
    {
        if (!Directory.Exists(folder)) return;
        if (!IsSafeRuntimeFolder(runtimeRoot, folder))
            throw new InvalidOperationException("Refusing to delete a runtime folder outside app storage or containing symlinked directories/junctions.");
        FileSystemSafetyService.DeleteDirectoryTree(folder);
    }

    public static Task DeleteSafeRuntimeFolderAsync(
        string runtimeRoot,
        string folder,
        CancellationToken cancellationToken = default)
        => Task.Run(() => DeleteSafeRuntimeFolder(runtimeRoot, folder), cancellationToken);

    public static void DeleteRuntimeFiles(string runtimeRoot, string folder)
    {
        if (!Directory.Exists(folder)) return;
        if (!IsSafeRuntimeFolder(runtimeRoot, folder) && !IsPackagedRuntimeFolderSafeToDelete(folder))
            throw new InvalidOperationException("Refusing to delete a runtime folder that is not app-managed or contains symlinked directories/junctions.");
        FileSystemSafetyService.DeleteDirectoryTree(folder);
    }

    public static Task DeleteRuntimeFilesAsync(
        string runtimeRoot,
        string folder,
        CancellationToken cancellationToken = default)
        => Task.Run(() => DeleteRuntimeFiles(runtimeRoot, folder), cancellationToken);

    private static bool HasManagedRuntimeMarker(string folder)
    {
        var metadataPath = Path.Combine(folder, "local-llm-runtime.json");
        if (!File.Exists(metadataPath)) return false;
        try
        {
            var metadata = JsonNode.Parse(File.ReadAllText(metadataPath));
            return !string.IsNullOrWhiteSpace(metadata?["managedPresetId"]?.ToString())
                || !string.IsNullOrWhiteSpace(metadata?["repoUrl"]?.ToString())
                || !string.IsNullOrWhiteSpace(metadata?["sourcePath"]?.ToString());
        }
        catch
        {
            return false;
        }
    }

    private static bool HasDirectLlamaServer(string folder)
        => File.Exists(Path.Combine(folder, "llama-server"))
            || File.Exists(Path.Combine(folder, "llama-server.exe"))
            || File.Exists(Path.Combine(folder, "bin", "llama-server"))
            || File.Exists(Path.Combine(folder, "bin", "llama-server.exe"));
}
