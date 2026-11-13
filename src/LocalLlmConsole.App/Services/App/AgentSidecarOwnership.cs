namespace LocalLlmConsole.Services;

// Records which sidecars beside the executable the Manager installed. Later
// bootstraps replace only absent files or files still matching a recorded hash,
// so unrelated files (for example in a source checkout) are never overwritten.
internal static class AgentSidecarOwnership
{
    public const string RecordFileName = ".llwm-sidecars.json";

    // Every released AGENTS.md since sidecar bootstrap was introduced starts with this text.
    private const string LegacyInstructionsHeading = "# llama.cpp Windows Manager";
    private const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static IReadOnlyDictionary<string, string>? Read(string root)
    {
        var path = Path.Combine(root, RecordFileName);
        if (!File.Exists(path)) return null;
        try
        {
            var record = JsonSerializer.Deserialize<OwnershipRecord>(File.ReadAllText(path), JsonOptions);
            if (record?.Files is null) return null;
            var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (relativePath, sha256) in record.Files)
            {
                if (!string.IsNullOrWhiteSpace(relativePath) && IsSha256(sha256))
                    files[relativePath] = sha256!.ToLowerInvariant();
            }
            return files;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Trace.TraceWarning($"Ignoring unreadable agent sidecar ownership record '{path}': {ex.Message}");
            return null;
        }
    }

    // A pre-ownership release left no record. Its sidecars are adopted only when the folder
    // holds llwmctl.exe, any AGENTS.md there is the Manager's, and it is not a source
    // checkout; otherwise existing files are treated as foreign and preserved.
    public static bool IsLegacyManagerInstallation(string root)
    {
        if (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, ".git"))
            || File.Exists(Path.Combine(root, "global.json"))
            || Directory.EnumerateFiles(root, "*.sln").Any()
            || Directory.EnumerateFiles(root, "*.slnx").Any())
        {
            return false;
        }

        var instructions = Path.Combine(root, "AGENTS.md");
        if (!File.Exists(Path.Combine(root, "llwmctl.exe"))) return false;
        if (!File.Exists(instructions)) return true;
        try
        {
            using var reader = new StreamReader(instructions);
            var heading = reader.ReadLine() ?? "";
            return heading.TrimStart('﻿').StartsWith(LegacyInstructionsHeading, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static void Write(
        string root,
        string version,
        IReadOnlyDictionary<string, string> owned,
        IReadOnlyList<string> preserved,
        IReadOnlyDictionary<string, string>? previous)
    {
        if (previous is not null && preserved.Count == 0 && SameEntries(previous, owned)) return;
        var path = Path.Combine(root, RecordFileName);
        var temporary = path + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
        try
        {
            var record = new OwnershipRecord(
                SchemaVersion,
                version,
                owned.OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(pair => pair.Key, pair => (string?)pair.Value.ToLowerInvariant(), StringComparer.OrdinalIgnoreCase),
                preserved.Count == 0 ? null : preserved);
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, JsonOptions) + Environment.NewLine);
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning($"Could not write agent sidecar ownership record '{path}': {ex.Message}");
        }
        finally
        {
            try
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Trace.TraceWarning($"Could not remove temporary ownership record '{temporary}': {ex.Message}");
            }
        }
    }

    private static bool SameEntries(IReadOnlyDictionary<string, string> left, IReadOnlyDictionary<string, string> right)
        => left.Count == right.Count
            && left.All(pair => right.TryGetValue(pair.Key, out var hash)
                && string.Equals(hash, pair.Value, StringComparison.OrdinalIgnoreCase));

    private static bool IsSha256(string? value)
        => value is { Length: 64 } && value.All(Uri.IsHexDigit);

    private sealed record OwnershipRecord(
        int SchemaVersion,
        string? Version,
        Dictionary<string, string?>? Files,
        IReadOnlyList<string>? Preserved = null);
}
