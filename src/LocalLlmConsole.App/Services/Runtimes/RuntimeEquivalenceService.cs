namespace LocalLlmConsole.Services;

public static class RuntimeEquivalenceService
{
    public static async Task<bool> ReconcileOfficialRuntimeEquivalenceAsync(StateStore store, IReadOnlyList<RuntimeRecord> runtimes, CancellationToken cancellationToken = default)
    {
        var changed = false;
        var current = runtimes.ToDictionary(runtime => runtime.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var preset in RuntimePackageSourceCatalog.PresetRows())
        {
            var packageRuntimes = current.Values
                .Where(runtime => string.Equals(RuntimeMetadataService.ManagedPackageId(runtime), preset.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (packageRuntimes.Count == 0) continue;

            var sourceRuntimes = current.Values
                .Where(runtime => string.Equals(RuntimeMetadataService.ManagedPresetId(runtime), preset.SourcePresetId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (sourceRuntimes.Count == 0) continue;

            foreach (var packageRuntime in packageRuntimes.ToArray())
            {
                var packageWithFingerprint = await EnsureFingerprintAsync(store, packageRuntime, cancellationToken);
                current[packageWithFingerprint.Id] = packageWithFingerprint;
                changed |= !string.Equals(packageWithFingerprint.MetadataJson, packageRuntime.MetadataJson, StringComparison.Ordinal);
                var packageFingerprint = RuntimeMetadataService.RuntimeFingerprint(packageWithFingerprint);
                if (string.IsNullOrWhiteSpace(packageFingerprint)) continue;

                foreach (var sourceRuntime in sourceRuntimes.ToArray())
                {
                    var sourceWithFingerprint = await EnsureFingerprintAsync(store, sourceRuntime, cancellationToken);
                    current[sourceWithFingerprint.Id] = sourceWithFingerprint;
                    changed |= !string.Equals(sourceWithFingerprint.MetadataJson, sourceRuntime.MetadataJson, StringComparison.Ordinal);
                    if (!string.Equals(packageFingerprint, RuntimeMetadataService.RuntimeFingerprint(sourceWithFingerprint), StringComparison.OrdinalIgnoreCase))
                        continue;

                    var updatedSource = WithArrayValue(sourceWithFingerprint, "equivalentPackageIds", preset.Id);
                    var updatedPackage = WithArrayValue(packageWithFingerprint, "equivalentSourcePresetIds", preset.SourcePresetId);
                    if (!ReferenceEquals(updatedSource, sourceWithFingerprint))
                    {
                        await store.UpsertRuntimeAsync(updatedSource);
                        current[updatedSource.Id] = updatedSource;
                        changed = true;
                    }
                    if (!ReferenceEquals(updatedPackage, packageWithFingerprint))
                    {
                        await store.UpsertRuntimeAsync(updatedPackage);
                        current[updatedPackage.Id] = updatedPackage;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    public static async Task<RuntimeRecord> EnsureFingerprintAsync(StateStore store, RuntimeRecord runtime, CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(RuntimeMetadataService.RuntimeFingerprint(runtime))) return runtime;
        var folder = RuntimeMetadataService.Folder(runtime);
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return runtime;
        var fingerprint = await ComputeFolderFingerprintAsync(folder, cancellationToken);
        if (string.IsNullOrWhiteSpace(fingerprint)) return runtime;
        var updated = WithMetadata(runtime, metadata =>
        {
            metadata["runtimeFingerprint"] = fingerprint;
            metadata["runtimeFingerprintVersion"] = "binlib-v1";
            metadata["runtimeFingerprintAt"] = DateTimeOffset.UtcNow.ToString("O");
        });
        await WritePackagedMetadataAsync(RuntimeMetadataService.Folder(updated), metadata =>
        {
            metadata["runtimeFingerprint"] = fingerprint;
            metadata["runtimeFingerprintVersion"] = "binlib-v1";
            metadata["runtimeFingerprintAt"] = DateTimeOffset.UtcNow.ToString("O");
        }, cancellationToken);
        await store.UpsertRuntimeAsync(updated);
        return updated;
    }

    public static string ComputeFolderFingerprint(string folder)
    {
        if (!Directory.Exists(folder)) return "";
        var files = FingerprintFiles(folder).ToArray();
        if (files.Length == 0) return "";

        using var overall = SHA256.Create();
        foreach (var file in files)
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/').ToLowerInvariant();
            using var stream = File.OpenRead(file);
            var fileHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
            var line = $"{relative}\n{new FileInfo(file).Length}\n{fileHash}\n";
            var bytes = Encoding.UTF8.GetBytes(line);
            overall.TransformBlock(bytes, 0, bytes.Length, null, 0);
        }
        overall.TransformFinalBlock([], 0, 0);
        return Convert.ToHexString(overall.Hash ?? []).ToLowerInvariant();
    }

    public static Task<string> ComputeFolderFingerprintAsync(
        string folder,
        CancellationToken cancellationToken = default)
        => Task.Run(() => ComputeFolderFingerprint(folder), cancellationToken);

    private static IEnumerable<string> FingerprintFiles(string folder)
    {
        var roots = new[] { Path.Combine(folder, "bin"), Path.Combine(folder, "lib") }
            .Where(Directory.Exists)
            .ToArray();
        if (roots.Length == 0) roots = [folder];

        return roots
            .SelectMany(root => Directory.EnumerateFiles(root, "*", SafeRecursiveEnumeration()))
            .Where(IsRuntimeBinaryFile)
            .OrderBy(file => Path.GetRelativePath(folder, file), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsRuntimeBinaryFile(string file)
    {
        var name = Path.GetFileName(file);
        if (name.Equals("local-llm-runtime.json", StringComparison.OrdinalIgnoreCase)) return false;
        var extension = Path.GetExtension(file).ToLowerInvariant();
        if (extension.Equals(".pdb", StringComparison.OrdinalIgnoreCase)) return false;
        if (extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) return false;
        if (extension.Equals(".txt", StringComparison.OrdinalIgnoreCase)) return false;
        if (extension.Equals(".pc", StringComparison.OrdinalIgnoreCase)) return false;
        if (extension.Equals(".cmake", StringComparison.OrdinalIgnoreCase)) return false;
        if (extension.Equals(".h", StringComparison.OrdinalIgnoreCase) || extension.Equals(".hpp", StringComparison.OrdinalIgnoreCase)) return false;
        return extension is "" or ".exe" or ".dll" or ".so" or ".dylib" or ".lib" or ".a"
            || name.StartsWith("llama-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("ggml", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("libggml", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("libllama", StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimeRecord WithArrayValue(RuntimeRecord runtime, string propertyName, string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return runtime;
        var updated = false;
        var next = WithMetadata(runtime, metadata =>
        {
            var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (metadata[propertyName] is JsonArray existing)
            {
                foreach (var item in existing)
                {
                    var text = item?.ToString() ?? "";
                    if (!string.IsNullOrWhiteSpace(text)) values.Add(text);
                }
            }
            else
            {
                var text = metadata[propertyName]?.ToString() ?? "";
                if (!string.IsNullOrWhiteSpace(text)) values.Add(text);
            }

            if (!values.Add(value)) return;
            var array = new JsonArray();
            foreach (var item in values.OrderBy(item => item, StringComparer.OrdinalIgnoreCase))
                array.Add(item);
            metadata[propertyName] = array;
            updated = true;
        });
        return updated ? next : runtime;
    }

    private static RuntimeRecord WithMetadata(RuntimeRecord runtime, Action<JsonObject> update)
    {
        JsonObject metadata;
        try
        {
            metadata = JsonNode.Parse(runtime.MetadataJson)?.AsObject() ?? new JsonObject();
        }
        catch
        {
            metadata = new JsonObject();
        }

        update(metadata);
        return runtime with { MetadataJson = metadata.ToJsonString() };
    }

    private static async Task WritePackagedMetadataAsync(string folder, Action<JsonObject> update, CancellationToken cancellationToken)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;
            var path = Path.Combine(folder, "local-llm-runtime.json");
            var metadata = File.Exists(path)
                ? JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken))?.AsObject() ?? new JsonObject()
                : new JsonObject();
            update(metadata);
            await File.WriteAllTextAsync(path, metadata.ToJsonString(), cancellationToken);
        }
        catch
        {
            // The database metadata is enough for app behavior; packaged metadata is best effort.
        }
    }

    private static EnumerationOptions SafeRecursiveEnumeration() => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
    };
}
