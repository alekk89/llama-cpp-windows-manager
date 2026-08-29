namespace LocalLlmConsole.Services;

public static partial class RuntimeMetadataService
{
    public static string Commit(RuntimeRecord runtime)
    {
        try
        {
            var metadata = JsonNode.Parse(runtime.MetadataJson);
            var commit = metadata?["commit"]?.ToString()
                ?? metadata?["runtimeMetadata"]?["commit"]?.ToString()
                ?? "";
            if (!string.IsNullOrWhiteSpace(commit)) return commit;
        }
        catch
        {
            // Try packaged metadata below.
        }

        var folder = Folder(runtime);
        try
        {
            var metadataPath = Path.Combine(folder, "local-llm-runtime.json");
            if (File.Exists(metadataPath))
            {
                var commit = JsonNode.Parse(File.ReadAllText(metadataPath))?["commit"]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(commit)) return commit;
            }
        }
        catch
        {
            // Infer from paths below.
        }

        return InferCommitFromText($"{runtime.Name} {runtime.ExecutablePath} {folder}");
    }

    public static bool CommitsMatch(string localCommit, string remoteCommit)
    {
        if (string.IsNullOrWhiteSpace(localCommit) || string.IsNullOrWhiteSpace(remoteCommit)) return false;
        return remoteCommit.StartsWith(localCommit, StringComparison.OrdinalIgnoreCase)
            || localCommit.StartsWith(remoteCommit, StringComparison.OrdinalIgnoreCase);
    }

    public static string ShortCommit(string commit)
        => string.IsNullOrWhiteSpace(commit) ? "n/a" : commit[..Math.Min(12, commit.Length)];

    public static string DisplayCommit(string commit)
        => string.IsNullOrWhiteSpace(commit) ? "commit unavailable" : ShortCommit(commit);

    public static string TryReadGitHeadCommit(string sourceDir)
    {
        try
        {
            var headPath = Path.Combine(sourceDir, ".git", "HEAD");
            if (!File.Exists(headPath)) return "";
            var head = File.ReadAllText(headPath).Trim();
            if (head.StartsWith("ref:", StringComparison.OrdinalIgnoreCase))
            {
                var refPath = Path.Combine(sourceDir, ".git", head[4..].Trim().Replace('/', Path.DirectorySeparatorChar));
                return File.Exists(refPath) ? File.ReadAllText(refPath).Trim() : "";
            }
            return head;
        }
        catch
        {
            return "";
        }
    }

    public static string InferCommitFromText(string text)
    {
        var matches = Regex.Matches(text, "[0-9a-fA-F]{7,40}");
        return matches.Count == 0
            ? ""
            : matches.Cast<Match>().OrderByDescending(match => match.Value.Length).First().Value;
    }
}
