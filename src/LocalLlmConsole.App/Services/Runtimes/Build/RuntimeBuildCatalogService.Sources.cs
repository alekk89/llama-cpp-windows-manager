namespace LocalLlmConsole.Services;

public static partial class RuntimeBuildCatalogService
{
    public static IEnumerable<RuntimeSourceEntry> Sources(string runtimeRoot)
    {
        var root = SourceRoot(runtimeRoot);
        if (!Directory.Exists(root)) yield break;

        IEnumerable<string> sourceDirs;
        try
        {
            sourceDirs = Directory.EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly).ToArray();
        }
        catch
        {
            yield break;
        }

        foreach (var sourceDir in sourceDirs)
        {
            var metadataPath = SourceMetadataPath(sourceDir);
            if (!File.Exists(metadataPath)) continue;
            var source = ReadSource(metadataPath);
            if (source is not null) yield return source;
        }
    }

    public static RuntimeSourceEntry? ReadSource(string metadataPath)
    {
        try
        {
            var text = File.ReadAllText(metadataPath);
            var source = JsonSerializer.Deserialize<RuntimeSourceEntry>(text);
            if (source is null) return null;
            var node = JsonNode.Parse(text);
            var mode = HasModeProperty(node) ? NormalizeBuildMode(source.Mode) : RuntimeMode.Wsl;
            return source with { Mode = mode };
        }
        catch
        {
            return null;
        }
    }

    public static string SourceRoot(string runtimeRoot) => Path.Combine(runtimeRoot, "runtime-sources");

    public static string SourceDir(string runtimeRoot, RuntimeBuildPreset preset) => Path.Combine(SourceRoot(runtimeRoot), preset.Id);

    public static string SourceMetadataPath(string sourceDir) => Path.Combine(sourceDir, "local-llm-runtime-source.json");

    public static string SourceCommit(RuntimeSourceEntry source)
    {
        if (!string.IsNullOrWhiteSpace(source.Commit) && !string.Equals(source.Commit, "unknown", StringComparison.OrdinalIgnoreCase))
            return source.Commit;

        var gitCommit = RuntimeMetadataService.TryReadGitHeadCommit(source.SourceDir);
        if (!string.IsNullOrWhiteSpace(gitCommit)) return gitCommit;
        return RuntimeMetadataService.InferCommitFromText(source.SourceDir);
    }

    public static IReadOnlyList<string> RemoteRefs(RuntimeBuildPreset preset)
        => string.IsNullOrWhiteSpace(preset.Branch)
            ? ["HEAD"]
            : [$"refs/heads/{preset.Branch}", preset.Branch];

    public static string FirstLsRemoteCommit(string output)
        => (output ?? "")
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "")
            .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text)) ?? "";

    private static bool HasModeProperty(JsonNode? node)
        => node?["Mode"] is not null || node?["mode"] is not null;

    private static HashSet<int> ReadModePropertyMap(string json)
    {
        try
        {
            var array = JsonNode.Parse(json) as JsonArray;
            if (array is null) return [];
            return array
                .Select((node, index) => new { node, index })
                .Where(item => HasModeProperty(item.node))
                .Select(item => item.index)
                .ToHashSet();
        }
        catch
        {
            return [];
        }
    }
}
