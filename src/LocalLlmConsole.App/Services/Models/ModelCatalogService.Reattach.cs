namespace LocalLlmConsole.Services;

public sealed record ModelReattachInspection(
    string Path,
    GgufFileClassification Classification,
    IReadOnlyList<string> IdentityWarnings,
    IReadOnlyList<ModelRecord> DuplicateModels)
{
    public bool RequiresIdentityConfirmation => IdentityWarnings.Count > 0;
}

public sealed record ModelReattachResult(
    ModelRecord Model,
    GgufFileClassification Classification,
    int RemovedDuplicateRecords,
    int MovedLaunchProfiles,
    IReadOnlyList<string> IdentityWarnings);

public sealed partial class ModelCatalogService
{
    public async Task<ModelReattachInspection> InspectReattachFileAsync(
        ModelRecord model,
        string path,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(model);
        var fullPath = Path.GetFullPath(path);
        var classification = await Task.Run(() => ClassifyGguf(fullPath), cancellationToken);
        if (classification.Role == GgufFileRole.Invalid)
            throw new InvalidOperationException(classification.Reason);

        var registered = await _store.ListModelsAsync();
        var current = registered.FirstOrDefault(candidate => candidate.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Model '{model.Id}' is no longer registered.");
        if (File.Exists(current.ModelPath))
            throw new InvalidOperationException($"{current.Name} is not missing. Reattach is available only when its registered GGUF cannot be found.");

        var duplicates = registered
            .Where(candidate => candidate.Ownership != OwnershipKind.RegistryOnly
                && !candidate.Id.Equals(current.Id, StringComparison.OrdinalIgnoreCase)
                && string.Equals(NormalizePath(candidate.ModelPath), fullPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var warnings = await IdentityWarningsAsync(current, fullPath, cancellationToken);
        return new ModelReattachInspection(fullPath, classification, warnings, duplicates);
    }

    public async Task<ModelReattachResult> ReattachFileAsync(
        ModelRecord model,
        string path,
        bool confirmRole = false,
        bool confirmIdentityMismatch = false,
        CancellationToken cancellationToken = default)
    {
        var inspection = await InspectReattachFileAsync(model, path, cancellationToken);
        return await ReattachFileAsync(model, inspection, confirmRole, confirmIdentityMismatch);
    }

    public async Task<ModelReattachResult> ReattachFileAsync(
        ModelRecord model,
        ModelReattachInspection inspection,
        bool confirmRole = false,
        bool confirmIdentityMismatch = false)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(inspection);
        if (inspection.Classification.Role != GgufFileRole.MainModel && !confirmRole)
            throw new InvalidOperationException(
                $"The selected GGUF was classified as {inspection.Classification.Role}: {inspection.Classification.Reason} "
                + "Retry with explicit role confirmation only when this file should be treated as a main model.");
        if (inspection.RequiresIdentityConfirmation && !confirmIdentityMismatch)
            throw new InvalidOperationException(
                "The selected GGUF does not match the stored model identity: "
                + string.Join(" ", inspection.IdentityWarnings)
                + " Retry only after explicitly confirming the mismatch.");

        var registered = await _store.ListModelsAsync();
        var current = registered.First(candidate => candidate.Id.Equals(model.Id, StringComparison.OrdinalIgnoreCase));
        var metadata = ExistingMetadataOrEmpty(current);
        foreach (var duplicate in inspection.DuplicateModels.OrderByDescending(candidate => candidate.Ownership == OwnershipKind.AppOwned))
        {
            var duplicateMetadata = ExistingMetadataOrEmpty(duplicate);
            foreach (var property in duplicateMetadata)
                if (!metadata.ContainsKey(property.Key)) metadata[property.Key] = property.Value?.DeepClone();
        }

        metadata = JsonNode.Parse(MergeGgufManifest(inspection.Path, metadata.ToJsonString()))?.AsObject() ?? metadata;
        metadata["sourceFolder"] = Path.GetDirectoryName(inspection.Path) ?? "";
        metadata["modelFile"] = inspection.Path;
        metadata["registrationSource"] = "reattached-file";
        metadata["reattachedFromPath"] = current.ModelPath;
        metadata["reattachedAt"] = DateTimeOffset.UtcNow;
        metadata["detectedRole"] = inspection.Classification.Role.ToString();
        metadata["detectedRoleConfidence"] = inspection.Classification.Confidence.ToString();
        metadata["detectedRoleReason"] = inspection.Classification.Reason;
        if (inspection.Classification.Role != GgufFileRole.MainModel && confirmRole)
        {
            metadata["userConfirmedMainModel"] = true;
            metadata["confirmedMainModelIdentity"] = ClassificationIdentity(inspection.Classification);
        }
        else
        {
            metadata.Remove("userConfirmedMainModel");
            metadata.Remove("confirmedMainModelIdentity");
        }

        var ownership = inspection.DuplicateModels.Any(candidate => candidate.Ownership == OwnershipKind.AppOwned)
            ? OwnershipKind.AppOwned
            : OwnershipKind.External;
        var updated = current with
        {
            ModelPath = inspection.Path,
            Ownership = ownership,
            MetadataJson = metadata.ToJsonString(),
            UpdatedAt = DateTimeOffset.UtcNow
        };
        var persisted = await _store.ReattachModelAsync(updated, inspection.DuplicateModels.Select(candidate => candidate.Id).ToArray());
        return new ModelReattachResult(
            updated,
            inspection.Classification,
            persisted.RemovedDuplicateRecords,
            persisted.MovedLaunchProfiles,
            inspection.IdentityWarnings);
    }

    private static async Task<IReadOnlyList<string>> IdentityWarningsAsync(
        ModelRecord model,
        string candidatePath,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var oldMetadata = ExistingMetadataOrEmpty(model);
        var candidateMetadata = JsonNode.Parse(MergeGgufManifest(candidatePath, "{}"))?.AsObject() ?? new JsonObject();

        CompareLong("file size", "ggufSizeBytes");
        CompareString("architecture", "ggufArchitecture");
        CompareLong("parameter count", "ggufParameterCount");
        CompareString("quantization", "ggufQuantization");

        var expectedSha = Sha256Digest.NormalizeHex(
            oldMetadata["Sha256"]?.ToString()
            ?? oldMetadata["sha256"]?.ToString());
        if (!string.IsNullOrWhiteSpace(expectedSha))
        {
            var actualSha = await FileSystemSafetyService.Sha256Async(candidatePath, cancellationToken);
            if (!string.Equals(expectedSha, actualSha, StringComparison.OrdinalIgnoreCase))
                warnings.Add("Its SHA-256 checksum differs from the downloaded model.");
        }

        return warnings;

        void CompareLong(string label, string key)
        {
            if (!TryLong(oldMetadata[key], out var expected) || expected <= 0
                || !TryLong(candidateMetadata[key], out var actual) || actual <= 0
                || expected == actual)
                return;
            warnings.Add($"Its {label} changed from {expected.ToString(CultureInfo.InvariantCulture)} to {actual.ToString(CultureInfo.InvariantCulture)}.");
        }

        void CompareString(string label, string key)
        {
            var expected = oldMetadata[key]?.ToString().Trim() ?? "";
            var actual = candidateMetadata[key]?.ToString().Trim() ?? "";
            if (string.IsNullOrWhiteSpace(expected) || string.IsNullOrWhiteSpace(actual)
                || expected.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || actual.Equals("unknown", StringComparison.OrdinalIgnoreCase)
                || expected.Equals(actual, StringComparison.OrdinalIgnoreCase))
                return;
            warnings.Add($"Its {label} changed from '{expected}' to '{actual}'.");
        }
    }

    private static bool TryLong(JsonNode? node, out long value)
    {
        value = 0;
        if (node is null) return false;
        return long.TryParse(node.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
