
namespace LocalLlmConsole.Services;

public sealed partial class ModelCatalogService
{
    private readonly StateStore _store;

    public ModelCatalogService(StateStore store) => _store = store;

    public async Task<int> ScanAsync(string modelsRoot)
        => (await ScanDetailedAsync(modelsRoot)).RegisteredCount;

    public async Task<ModelScanResult> ScanDetailedAsync(string modelsRoot)
    {
        Directory.CreateDirectory(modelsRoot);
        var ggufPaths = await FindGgufFilesAsync(modelsRoot);
        var existing = await _store.ListModelsAsync();
        var confirmedIdentities = existing
            .OrderByDescending(model => model.UpdatedAt)
            .Select(model => (Path: NormalizePath(model.ModelPath), Identity: ConfirmedMainModelIdentity(model)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Identity))
            .GroupBy(item => item.Path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Identity!, StringComparer.OrdinalIgnoreCase);
        var classifications = await Task.Run(() => ggufPaths
            .Select(ClassifyGguf)
            .Select(classification => ConfirmedClassification(classification, confirmedIdentities))
            .ToArray());
        var modelPaths = classifications
            .Where(classification => classification.Role == GgufFileRole.MainModel)
            .Select(classification => classification.Path)
            .ToArray();
        var registered = await RegisterExternalModelsAsync(modelsRoot, modelPaths);
        return new ModelScanResult(registered, classifications);
    }

    public async Task<ModelRecord> ImportFolderAsync(string folder)
    {
        var full = Path.GetFullPath(folder);
        var result = await ScanDetailedAsync(full);
        if (result.RegisteredModels.Count == 0)
        {
            var first = result.Files.FirstOrDefault();
            var detail = first is null ? "No GGUF files were found." : $"{Path.GetFileName(first.Path)}: {first.Reason}";
            throw new InvalidOperationException($"No main GGUF models were found in that folder. {detail} Use explicit file import to confirm an ambiguous model.");
        }
        return result.RegisteredModels.First();
    }

    public async Task<ModelRecord> ImportFileAsync(string path, bool confirmRole = false)
    {
        var fullPath = Path.GetFullPath(path);
        var classification = await Task.Run(() => ClassifyGguf(fullPath));
        if (classification.Role == GgufFileRole.Invalid)
            throw new InvalidOperationException(classification.Reason);
        if (classification.Role != GgufFileRole.MainModel && !confirmRole)
            throw new InvalidOperationException(
                $"The selected GGUF was classified as {classification.Role}: {classification.Reason} "
                + "Retry with explicit role confirmation only when this file should be treated as a main model.");

        var existing = (await _store.ListModelsAsync())
            .Where(model => model.Ownership != OwnershipKind.RegistryOnly)
            .FirstOrDefault(model => string.Equals(NormalizePath(model.ModelPath), fullPath, StringComparison.OrdinalIgnoreCase));
        var folder = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var discovered = CreateExternalRecord(folder, folder, fullPath);
        var metadata = ExistingMetadataOrEmpty(existing);
        var discoveredMetadata = JsonNode.Parse(discovered.MetadataJson)?.AsObject() ?? new JsonObject();
        foreach (var property in discoveredMetadata)
            if (!metadata.ContainsKey(property.Key)) metadata[property.Key] = property.Value?.DeepClone();
        metadata["registrationSource"] = "manual-file";
        if (classification.Role != GgufFileRole.MainModel && confirmRole)
        {
            metadata["userConfirmedMainModel"] = true;
            metadata["confirmedMainModelIdentity"] = ClassificationIdentity(classification);
        }
        else
        {
            metadata.Remove("userConfirmedMainModel");
            metadata.Remove("confirmedMainModelIdentity");
        }
        metadata["detectedRole"] = classification.Role.ToString();
        metadata["detectedRoleConfidence"] = classification.Confidence.ToString();
        metadata["detectedRoleReason"] = classification.Reason;
        metadata["manuallyRegisteredAt"] = DateTimeOffset.UtcNow;

        var record = discovered with
        {
            Id = existing?.Id ?? discovered.Id,
            Name = existing?.Name ?? discovered.Name,
            Ownership = existing?.Ownership ?? OwnershipKind.External,
            MetadataJson = metadata.ToJsonString(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _store.UpsertModelAsync(record);
        await RemoveDuplicateModelRecordsForPathAsync(record);
        await SeedLegacyLaunchSettingsAsync(record);
        return record;
    }

    private static JsonObject ExistingMetadataOrEmpty(ModelRecord? model)
    {
        if (model is null) return new JsonObject();
        try { return JsonNode.Parse(model.MetadataJson)?.AsObject() ?? new JsonObject(); }
        catch { return new JsonObject { ["rawMetadata"] = model.MetadataJson }; }
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
        var contained = PathContainmentGuard.ResolveDescendant(
            root,
            path,
            "Refusing to register an app-owned download outside the configured models folder.");
        try
        {
            PathContainmentGuard.RejectReparsePointAncestors(
                contained,
                includeExistingTarget: true,
                "Refusing to register an app-owned download through a symlink or junction.");
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
            var canonicalExisting = existingForPath?
                .Where(model => model.Ownership != OwnershipKind.RegistryOnly)
                .OrderBy(model => model.Ownership == OwnershipKind.AppOwned ? 0 : 1)
                .ThenByDescending(IsUserConfirmedMainModel)
                .ThenByDescending(model => model.UpdatedAt)
                .FirstOrDefault();
            if (canonicalExisting is not null)
            {
                await RemoveDuplicateModelRecordsForPathAsync(canonicalExisting);
                await SeedLegacyLaunchSettingsAsync(canonicalExisting);
                records.Add(canonicalExisting);
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

        try { metadata["ggufSizeBytes"] = new FileInfo(modelPath).Length; }
        catch { metadata["ggufSizeBytes"] = null; }

        var gguf = GgufMetadataReader.TryRead(modelPath);
        if (gguf.Count == 0) return metadata.ToJsonString();

        var architecture = gguf.TryGetValue("general.architecture", out var architectureValue) ? architectureValue?.ToString() ?? "" : "";
        var quantization = InferQuant(modelPath);
        var contextLength = ModelCapabilityService.ContextLength(gguf, architecture);
        metadata["ggufMetadataAvailable"] = true;
        metadata["ggufArchitecture"] = string.IsNullOrWhiteSpace(architecture) ? "unknown" : architecture;
        metadata["ggufQuantization"] = string.IsNullOrWhiteSpace(quantization) ? "unknown" : quantization;
        if (contextLength > 0) metadata["ggufContextLength"] = contextLength;
        metadata["ggufParameterCount"] = TryPositiveInteger(gguf, "general.parameter_count")
            ?? GgufMetadataReader.TryReadParameterCount(modelPath);
        metadata["ggufHasChatTemplate"] = gguf.ContainsKey("tokenizer.chat_template");
        return metadata.ToJsonString();
    }

    private static long? TryPositiveInteger(IReadOnlyDictionary<string, object?> metadata, string key)
    {
        if (!metadata.TryGetValue(key, out var value) || value is null) return null;
        try
        {
            var parsed = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return parsed > 0 ? parsed : null;
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> FindGgufFiles(string root)
    {
        if (!Directory.Exists(root)) yield break;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.System | FileAttributes.ReparsePoint
        };
        foreach (var file in Directory.EnumerateFiles(root, "*", options)
            .Where(file => file.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            yield return file;
        }
    }

    private static async Task<string[]> FindGgufFilesAsync(string root)
        => await Task.Run(() => FindGgufFiles(root).ToArray());

    private static bool IsModelGguf(string file)
        => ClassifyGguf(file).Role == GgufFileRole.MainModel;

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
