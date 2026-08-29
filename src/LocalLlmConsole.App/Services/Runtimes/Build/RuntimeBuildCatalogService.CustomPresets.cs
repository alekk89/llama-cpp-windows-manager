namespace LocalLlmConsole.Services;

public static partial class RuntimeBuildCatalogService
{
    private static readonly SemaphoreSlim CustomPresetWriteGate = new(1, 1);

    public static string CustomRepositoriesPath(string runtimeRoot)
        => Path.Combine(runtimeRoot, "custom-runtime-repositories.json");

    internal static string CustomRepositoriesBackupPath(string runtimeRoot)
        => CustomRepositoriesPath(runtimeRoot) + ".bak";

    public static IReadOnlyList<RuntimeBuildPreset> ReadCustomPresets(string runtimeRoot)
    {
        foreach (var path in new[] { CustomRepositoriesPath(runtimeRoot), CustomRepositoriesBackupPath(runtimeRoot) })
        {
            if (!File.Exists(path)) continue;
            try
            {
                var text = File.ReadAllText(path);
                var modeProperties = ReadModePropertyMap(text);
                return (JsonSerializer.Deserialize<List<RuntimeBuildPreset>>(text) ?? [])
                    .Select((preset, index) => new { preset, index })
                    .Where(item => !string.IsNullOrWhiteSpace(item.preset.Label) && !string.IsNullOrWhiteSpace(item.preset.RepoUrl))
                    .Where(item => IsSafePreset(item.preset))
                    .Select(item =>
                    {
                        var preset = item.preset;
                        var branch = preset.Branch?.Trim() ?? "";
                        var mode = modeProperties.Contains(item.index) ? NormalizeBuildMode(preset.Mode) : RuntimeMode.Wsl;
                        var rawId = string.IsNullOrWhiteSpace(preset.Id)
                            ? CustomPresetId(preset.Label, preset.RepoUrl, branch, BackendKey(preset), mode)
                            : preset.Id;
                        var id = ModelCatalogService.SafeId(rawId);
                        return preset with { Id = id, Branch = branch, Custom = true, Backend = BackendKey(preset), Mode = mode };
                    })
                    .ToList();
            }
            catch
            {
                // Try the last-known-good generation before treating the catalog as unavailable.
            }
        }

        return [];
    }

    public static async Task SaveCustomPresetsAsync(
        string runtimeRoot,
        IReadOnlyList<RuntimeBuildPreset> presets,
        CancellationToken cancellationToken = default)
    {
        await CustomPresetWriteGate.WaitAsync(cancellationToken);
        var path = CustomRepositoriesPath(runtimeRoot);
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(runtimeRoot);
            var customPresets = presets
                .Where(preset => preset.Custom)
                .OrderBy(preset => preset.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            await File.WriteAllTextAsync(
                temporaryPath,
                JsonSerializer.Serialize(customPresets, new JsonSerializerOptions { WriteIndented = true }),
                cancellationToken);
            await using (var stream = new FileStream(temporaryPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                stream.Flush(flushToDisk: true);

            if (File.Exists(path))
            {
                var backup = CustomRepositoriesBackupPath(runtimeRoot);
                if (File.Exists(backup)) File.Delete(backup);
                File.Replace(temporaryPath, path, backup, ignoreMetadataErrors: true);
            }
            else
            {
                File.Move(temporaryPath, path);
            }
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); }
            catch { }
            CustomPresetWriteGate.Release();
        }
    }

    public static string CustomPresetId(string label, string repoUrl, string branch, bool cuda)
        => CustomPresetId(label, repoUrl, branch, cuda ? "cuda" : "cpu");

    public static string CustomPresetId(string label, string repoUrl, string branch, string backend)
        => CustomPresetId(label, repoUrl, branch, backend, RuntimeMode.Wsl);

    public static string CustomPresetId(string label, string repoUrl, string branch, string backend, RuntimeMode mode)
    {
        var backendKey = NormalizeBuildBackend(backend);
        var modeKey = ModeKey(mode);
        var idBackend = modeKey == "native" ? $"windows-{backendKey}" : backendKey;
        var hashInput = modeKey == "native"
            ? $"{repoUrl}|{branch}|{backendKey}|{modeKey}"
            : $"{repoUrl}|{branch}|{backendKey}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(hashInput.ToLowerInvariant()));
        var hash = Convert.ToHexString(bytes)[..8].ToLowerInvariant();
        return ModelCatalogService.SafeId($"custom-{label}-{idBackend}-{hash}");
    }

    public static bool SameRepository(RuntimeBuildPreset left, RuntimeBuildPreset right)
        => string.Equals(NormalizeRepositoryText(left.RepoUrl), NormalizeRepositoryText(right.RepoUrl), StringComparison.OrdinalIgnoreCase)
            && string.Equals(left.Branch?.Trim() ?? "", right.Branch?.Trim() ?? "", StringComparison.OrdinalIgnoreCase)
            && BuildBackend(left) == BuildBackend(right)
            && NormalizeBuildMode(left.Mode) == NormalizeBuildMode(right.Mode);

    public static string NormalizeRepositoryText(string value)
        => (value ?? "").Trim().TrimEnd('/', '\\');

    public static bool IsSafePreset(RuntimeBuildPreset preset)
        => IsAllowedGitSource(preset.RepoUrl)
            && (string.IsNullOrWhiteSpace(preset.Branch) || IsSafeGitRefName(preset.Branch));

    public static bool IsSafeUiCustomPreset(RuntimeBuildPreset preset)
        => IsHttpsGitSource(preset.RepoUrl)
            && (string.IsNullOrWhiteSpace(preset.Branch) || IsSafeGitRefName(preset.Branch));

    public static bool IsHttpsGitSource(string repoUrl)
    {
        var value = (repoUrl ?? "").Trim();
        return Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(uri.UserInfo);
    }

    public static bool IsAllowedGitSource(string repoUrl)
    {
        var value = (repoUrl ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return false;
        if (Directory.Exists(value)) return true;
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals(Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase)
                || uri.Scheme.Equals("ssh", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    public static bool IsSafeGitRefName(string branch)
    {
        var value = (branch ?? "").Trim();
        if (string.IsNullOrWhiteSpace(value)) return true;
        if (value.StartsWith("-", StringComparison.Ordinal)) return false;
        if (value.Contains("..", StringComparison.Ordinal) || value.EndsWith(".", StringComparison.Ordinal)) return false;
        return value.All(ch => !char.IsControl(ch) && ch is not ' ' and not '~' and not '^' and not ':' and not '?' and not '*' and not '[' and not '\\');
    }
}
