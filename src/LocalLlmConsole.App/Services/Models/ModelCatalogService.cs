
namespace LocalLlmConsole.Services;

public sealed partial class ModelCatalogService
{
    private readonly StateStore _store;

    public ModelCatalogService(StateStore store) => _store = store;

    public async Task<int> ScanAsync(string modelsRoot)
    {
        Directory.CreateDirectory(modelsRoot);
        var modelPaths = await FindModelFilesAsync(modelsRoot);
        var registered = await RegisterExternalModelsAsync(modelsRoot, modelPaths);
        return registered.Count;
    }

    public async Task<ModelRecord> ImportFolderAsync(string folder)
    {
        var full = Path.GetFullPath(folder);
        var modelPaths = await FindModelFilesAsync(full);
        if (modelPaths.Length == 0) throw new InvalidOperationException("No GGUF model files were found in that folder.");
        var registered = await RegisterExternalModelsAsync(full, modelPaths);
        return registered.First();
    }

    public async Task<ModelRecord> RegisterDownloadedAsync(string modelsRoot, string modelName, string modelPath, string metadataJson)
    {
        EnsurePathInsideRoot(modelPath, modelsRoot);
        var id = ModelIdForPath(modelsRoot, modelPath);
        var enrichedMetadata = await Task.Run(() => MergeGgufManifest(modelPath, metadataJson));
        var record = new ModelRecord(
            id,
            FriendlyDisplayName(modelName, modelPath),
            Path.GetFullPath(modelPath),
            OwnershipKind.AppOwned,
            enrichedMetadata,
            DateTimeOffset.UtcNow);
        await _store.UpsertModelAsync(record);
        await RemoveDuplicateModelRecordsForPathAsync(record);
        await SeedLegacyLaunchSettingsAsync(record);
        return record;
    }

    private static void EnsurePathInsideRoot(string path, string root)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(fullRoot, fullPath);
        if (string.IsNullOrWhiteSpace(relative)
            || string.Equals(relative, ".", StringComparison.Ordinal)
            || string.Equals(relative, "..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
            || Path.IsPathRooted(relative))
            throw new InvalidOperationException("Refusing to register an app-owned download outside the configured models folder.");

        RejectReparsePointPath(fullRoot, fullPath);
    }

    private static void RejectReparsePointPath(string root, string path)
    {
        try
        {
            if ((File.Exists(path) || Directory.Exists(path))
                && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Refusing to register an app-owned download through a symlink or junction.");

            var current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(current)
                && Path.GetRelativePath(root, current) is var relative
                && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                && !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal)
                && !Path.IsPathRooted(relative))
            {
                if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Refusing to register an app-owned download through a symlink or junction.");
                if (string.Equals(Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
                    return;
                current = Path.GetDirectoryName(current);
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch
        {
            throw new InvalidOperationException("Could not validate the app-owned download path.");
        }
    }

    public async Task DeleteAsync(ModelRecord model, string modelsRoot)
    {
        if (model.Ownership == OwnershipKind.AppOwned)
        {
            var dir = Path.GetDirectoryName(model.ModelPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                FileOwnershipService.EnsureDeletionAllowed(model, dir, modelsRoot);
                if (Directory.Exists(dir)) await Task.Run(() => Directory.Delete(dir, recursive: true));
            }
        }
        await _store.DeleteModelAsync(model.Id);
    }

    private async Task<IReadOnlyList<ModelRecord>> RegisterExternalModelsAsync(string scopeRoot, IReadOnlyList<string> modelPaths)
    {
        var records = new List<ModelRecord>();
        var existingByPath = (await _store.ListModelsAsync())
            .GroupBy(model => NormalizePath(model.ModelPath), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var modelPath in modelPaths.Select(NormalizePath).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            existingByPath.TryGetValue(modelPath, out var existingForPath);
            var appOwned = existingForPath?.FirstOrDefault(model => model.Ownership == OwnershipKind.AppOwned);
            if (appOwned is not null)
            {
                await RemoveDuplicateModelRecordsForPathAsync(appOwned);
                await SeedLegacyLaunchSettingsAsync(appOwned);
                records.Add(appOwned);
                continue;
            }

            var folder = Path.GetDirectoryName(modelPath) ?? Path.GetFullPath(scopeRoot);
            var record = await CreateExternalRecordAsync(scopeRoot, folder, modelPath);
            foreach (var stale in existingForPath?.Where(model => model.Id != record.Id && model.Ownership != OwnershipKind.RegistryOnly) ?? [])
                await _store.DeleteModelAsync(stale.Id);

            await _store.UpsertModelAsync(record);
            await SeedLegacyLaunchSettingsAsync(record);
            records.Add(record);
        }

        return records;
    }

    private async Task SeedLegacyLaunchSettingsAsync(ModelRecord record)
    {
        if (await _store.GetModelLaunchSettingsAsync(record.Id) is not null) return;
        var legacy = await Task.Run(() => TryReadLegacyLaunchSettings(record.ModelPath));
        if (legacy is not null) await _store.SaveModelLaunchSettingsAsync(record.Id, legacy);
    }

    private static async Task<ModelRecord> CreateExternalRecordAsync(string scopeRoot, string folder, string modelPath)
        => await Task.Run(() => CreateExternalRecord(scopeRoot, folder, modelPath));

    private static ModelRecord CreateExternalRecord(string scopeRoot, string folder, string modelPath)
    {
        var id = ModelIdForPath(scopeRoot, modelPath);
        var name = FriendlyName(Path.GetFileNameWithoutExtension(modelPath));
        var legacySource = TryReadLegacySourceReference(modelPath);
        var metadata = MergeGgufManifest(modelPath, JsonSerializer.Serialize(new
        {
            sourceFolder = folder,
            modelFile = modelPath,
            quant = InferQuant(modelPath),
            sourceRepo = legacySource?.Repo,
            sourceFile = legacySource?.Path,
            registeredAt = DateTimeOffset.UtcNow
        }));
        return new ModelRecord(id, name, Path.GetFullPath(modelPath), OwnershipKind.External, metadata, DateTimeOffset.UtcNow);
    }

    private static string MergeGgufManifest(string modelPath, string metadataJson)
    {
        JsonObject metadata;
        try
        {
            metadata = JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
        }
        catch
        {
            metadata = new JsonObject { ["rawMetadata"] = metadataJson };
        }

        var gguf = GgufMetadataReader.TryRead(modelPath);
        if (gguf.Count == 0) return metadata.ToJsonString();

        var architecture = gguf.TryGetValue("general.architecture", out var architectureValue) ? architectureValue?.ToString() ?? "" : "";
        var quantization = InferQuant(modelPath);
        var contextLength = ModelCapabilityService.ContextLength(gguf, architecture);
        metadata["ggufMetadataAvailable"] = true;
        metadata["ggufArchitecture"] = string.IsNullOrWhiteSpace(architecture) ? "unknown" : architecture;
        metadata["ggufQuantization"] = string.IsNullOrWhiteSpace(quantization) ? "unknown" : quantization;
        if (contextLength > 0) metadata["ggufContextLength"] = contextLength;
        metadata["ggufHasChatTemplate"] = gguf.ContainsKey("tokenizer.chat_template");
        return metadata.ToJsonString();
    }

    private static IEnumerable<string> FindModelFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(root, "*", options)
            .Where(IsModelGguf)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    private static async Task<string[]> FindModelFilesAsync(string root)
        => await Task.Run(() => FindModelFiles(root).ToArray());

    private static bool IsModelGguf(string file)
    {
        var name = Path.GetFileName(file);
        return name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
            && !LooksLikeVisionProjectorName(name)
            && !LooksLikeDraftOrMtpHeadName(name)
            && !HasStandaloneSpeculativeArchitecture(file);
    }

    private static string? FindLegacyModelJson(string modelPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder)) return null;

        var candidates = new[]
        {
            Path.Combine(folder, "model.json"),
            Path.Combine(Directory.GetParent(folder)?.FullName ?? folder, "model.json")
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static string ModelIdForPath(string scopeRoot, string modelPath)
    {
        var fullPath = Path.GetFullPath(modelPath);
        var seed = RelativePathOrFullPath(scopeRoot, fullPath);
        seed = Path.ChangeExtension(seed, null) ?? seed;
        var safe = SafeId(seed);
        var hash = ShortHash(fullPath);
        var safePrefix = safe[..Math.Min(86, safe.Length)];
        return $"{safePrefix}-{hash}";
    }

    private static string RelativePathOrFullPath(string scopeRoot, string modelPath)
    {
        var root = Path.GetFullPath(scopeRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSeparator = root + Path.DirectorySeparatorChar;
        return modelPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            ? Path.GetRelativePath(root, modelPath)
            : modelPath;
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path);

    private static string ShortHash(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value.ToLowerInvariant()));
        return Convert.ToHexString(bytes)[..8].ToLowerInvariant();
    }

    internal static string SafeId(string value)
    {
        var safe = new string((value ?? "model").ToLowerInvariant().Select(ch =>
            char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.' ? ch : '-').ToArray()).Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "model" : safe[..Math.Min(96, safe.Length)];
    }

    internal static string FriendlyName(string value)
        => string.Join(" ", (value ?? "Local model").Replace('_', '-').Split('-', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part[1..]));

    internal static string FriendlyDisplayName(string name, string modelPath)
    {
        var source = string.IsNullOrWhiteSpace(name)
            ? Path.GetFileNameWithoutExtension(modelPath)
            : name.Trim();
        if (source.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            source = Path.GetFileNameWithoutExtension(source);
        return FriendlyName(source);
    }

    internal static string InferQuant(string file)
    {
        var name = Path.GetFileName(file).ToLowerInvariant();
        var match = System.Text.RegularExpressions.Regex.Match(name, @"(?:^|[-_.])(iq\d_[a-z0-9]+|q\d(?:_[a-z0-9]+)+|f16|bf16|f32)(?:[-_.]|$)");
        return match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
    }
}
