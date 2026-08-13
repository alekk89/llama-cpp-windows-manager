namespace LocalLlmConsole.Services;

public sealed partial class ModelCatalogService
{
    public static string? ResolveVisionProjectorPath(string modelPath, string configuredProjectorPath)
    {
        if (VisionProjectorSelection.IsEmbeddedOrMainModel(modelPath, configuredProjectorPath))
            return null;

        if (!string.IsNullOrWhiteSpace(configuredProjectorPath))
        {
            var fullPath = Path.GetFullPath(configuredProjectorPath.Trim());
            return File.Exists(fullPath) ? fullPath : null;
        }

        return FindVisionProjector(modelPath);
    }

    public static string? FindVisionProjector(string modelPath)
        => FindVisionProjectors(modelPath).FirstOrDefault();

    public static IReadOnlyList<string> FindVisionProjectors(string modelPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        return CandidateVisionProjectors(folder)
            .Where(file =>
            {
                var name = Path.GetFileName(file);
                return !string.Equals(Path.GetFullPath(file), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase)
                    && LooksLikeVisionProjectorName(name);
            })
            .OrderBy(file => Path.GetFileName(file).Contains("f16", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? FindDraftModel(string modelPath)
        => FindDraftModels(modelPath).FirstOrDefault();

    public static IReadOnlyList<string> FindDraftModels(string modelPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        var mainPath = Path.GetFullPath(modelPath);
        return CandidateVisionProjectors(folder)
            .Where(file =>
            {
                var name = Path.GetFileName(file);
                return !string.Equals(Path.GetFullPath(file), mainPath, StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    && !LooksLikeVisionProjectorName(name)
                    && LooksLikeDraftOrMtpHeadName(name);
            })
            .OrderBy(file => Path.GetFileName(file).Contains("dspark", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => Path.GetFileName(file).Contains("dflash", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => Path.GetFileName(file).Contains("mtp", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => Path.GetFileName(file).Contains("draft", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> CandidateVisionProjectors(string folder)
    {
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { folder };
        foreach (var child in Directory.EnumerateDirectories(folder).Where(path => !IsReparsePoint(path)).Take(20))
            folders.Add(child);
        var parent = Path.GetDirectoryName(folder);
        if (!string.IsNullOrWhiteSpace(parent))
            folders.Add(parent);

        foreach (var candidateFolder in folders.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(candidateFolder, "*.gguf", SearchOption.TopDirectoryOnly).Take(200))
                yield return file;
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch { return true; }
    }

    private static bool LooksLikeVisionProjectorName(string name)
    {
        var normalized = (name ?? "").Replace('_', '-').Replace('.', '-');
        return normalized.Contains("mmproj", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("projector", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("clip", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vision-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("visual-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("image-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-vision", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-visual", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-image", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("mtp-vision", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vision-mtp", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDraftOrMtpHeadName(string name)
    {
        var normalized = (name ?? "").Replace('_', '-').Replace('.', '-');
        return normalized.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-mtp-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("mtp-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dspark", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("dflash", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-draft-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("-spec-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("spec-", StringComparison.OrdinalIgnoreCase);
    }
}
