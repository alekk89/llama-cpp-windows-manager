namespace LocalLlmConsole.Services;

public sealed record ModelReattachRenamedProfile(
    string Id,
    string PreviousName,
    string Name,
    bool WasDefault);

public sealed record ModelReattachPersistenceResult(
    int RemovedDuplicateRecords,
    int MovedLaunchProfiles,
    IReadOnlyList<ModelReattachRenamedProfile> RenamedLaunchProfiles);

public sealed partial class StateStore
{
    public async Task<ModelReattachPersistenceResult> ReattachModelAsync(
        ModelRecord updatedModel,
        IReadOnlyCollection<string> duplicateModelIds)
    {
        ArgumentNullException.ThrowIfNull(updatedModel);
        ArgumentNullException.ThrowIfNull(duplicateModelIds);

        return await WithConnectionAsync(async () =>
        {
            InvalidateCatalog();
            await using var transaction = (SqliteTransaction)await _connection.BeginTransactionAsync();
            try
            {
                var canonicalProfiles = await ReadProfilesAsync(updatedModel.Id, transaction);
                var profileNames = canonicalProfiles
                    .Select(profile => profile.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                var hasDefault = canonicalProfiles.Any(profile => profile.IsDefault);
                var movedProfiles = 0;
                var removedDuplicates = 0;
                var renamedProfiles = new List<ModelReattachRenamedProfile>();

                foreach (var duplicateId in duplicateModelIds
                             .Where(id => !string.IsNullOrWhiteSpace(id)
                                 && !id.Equals(updatedModel.Id, StringComparison.OrdinalIgnoreCase))
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    foreach (var profile in await ReadProfilesAsync(duplicateId, transaction))
                    {
                        // Conflicting profiles are kept under a distinct name; IDs, and therefore group,
                        // favorite, and startup references, move with them.
                        var keepDefault = profile.IsDefault && !hasDefault;
                        var name = profileNames.Contains(profile.Name) || profile.IsDefault && !keepDefault
                            ? RelocatedProfileName(profile.Name, profileNames)
                            : profile.Name;

                        await using var moveProfile = _connection.CreateCommand();
                        moveProfile.Transaction = transaction;
                        moveProfile.CommandText = """
UPDATE model_launch_profiles
SET model_id = $model_id, name = $name, is_default = $is_default, updated_at = $updated_at
WHERE id = $id;
""";
                        moveProfile.Parameters.AddWithValue("$model_id", updatedModel.Id);
                        moveProfile.Parameters.AddWithValue("$name", name);
                        moveProfile.Parameters.AddWithValue("$is_default", keepDefault ? 1 : 0);
                        moveProfile.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
                        moveProfile.Parameters.AddWithValue("$id", profile.Id);
                        movedProfiles += await moveProfile.ExecuteNonQueryAsync();
                        profileNames.Add(name);
                        hasDefault |= keepDefault;
                        if (!name.Equals(profile.Name, StringComparison.Ordinal) || profile.IsDefault != keepDefault)
                            renamedProfiles.Add(new ModelReattachRenamedProfile(profile.Id, profile.Name, name, profile.IsDefault));
                    }

                    await using (var mergeFavorite = _connection.CreateCommand())
                    {
                        mergeFavorite.Transaction = transaction;
                        mergeFavorite.CommandText = """
INSERT INTO favorite_models (model_id, updated_at)
SELECT $model_id, updated_at
FROM favorite_models
WHERE model_id = $duplicate_id
ON CONFLICT(model_id) DO UPDATE SET updated_at = MAX(favorite_models.updated_at, excluded.updated_at);
""";
                        mergeFavorite.Parameters.AddWithValue("$model_id", updatedModel.Id);
                        mergeFavorite.Parameters.AddWithValue("$duplicate_id", duplicateId);
                        await mergeFavorite.ExecuteNonQueryAsync();
                    }

                    await using var deleteDuplicate = _connection.CreateCommand();
                    deleteDuplicate.Transaction = transaction;
                    deleteDuplicate.CommandText = "DELETE FROM models WHERE id = $id;";
                    deleteDuplicate.Parameters.AddWithValue("$id", duplicateId);
                    removedDuplicates += await deleteDuplicate.ExecuteNonQueryAsync();
                }

                await using (var updateModel = _connection.CreateCommand())
                {
                    updateModel.Transaction = transaction;
                    updateModel.CommandText = """
UPDATE models
SET name = $name,
    model_path = $model_path,
    ownership = $ownership,
    metadata_json = $metadata_json,
    updated_at = $updated_at
WHERE id = $id;
""";
                    updateModel.Parameters.AddWithValue("$id", updatedModel.Id);
                    updateModel.Parameters.AddWithValue("$name", updatedModel.Name);
                    updateModel.Parameters.AddWithValue("$model_path", updatedModel.ModelPath);
                    updateModel.Parameters.AddWithValue("$ownership", updatedModel.Ownership.ToString());
                    updateModel.Parameters.AddWithValue("$metadata_json", updatedModel.MetadataJson);
                    updateModel.Parameters.AddWithValue("$updated_at", updatedModel.UpdatedAt.ToString("O"));
                    if (await updateModel.ExecuteNonQueryAsync() != 1)
                        throw new InvalidOperationException($"Model '{updatedModel.Id}' is no longer registered.");
                }

                await transaction.CommitAsync();
                return new ModelReattachPersistenceResult(removedDuplicates, movedProfiles, renamedProfiles);
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        });
    }

    private async Task<IReadOnlyList<StoredProfileIdentity>> ReadProfilesAsync(
        string modelId,
        SqliteTransaction transaction)
    {
        var profiles = new List<StoredProfileIdentity>();
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, name, is_default FROM model_launch_profiles WHERE model_id = $model_id ORDER BY is_default DESC, name;";
        command.Parameters.AddWithValue("$model_id", modelId);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            profiles.Add(new StoredProfileIdentity(reader.GetString(0), reader.GetString(1), reader.GetInt32(2) != 0));
        return profiles;
    }

    internal static string RelocatedProfileName(string name, IReadOnlySet<string> existingNames)
    {
        for (var attempt = 1; ; attempt++)
        {
            var suffix = attempt == 1
                ? " (relocated)"
                : $" (relocated {attempt.ToString(CultureInfo.InvariantCulture)})";
            var stem = name.Length + suffix.Length > MaxReattachProfileNameLength
                ? name[..(MaxReattachProfileNameLength - suffix.Length)].TrimEnd()
                : name;
            var candidate = stem + suffix;
            if (!existingNames.Contains(candidate)) return candidate;
        }
    }

    private const int MaxReattachProfileNameLength = 200;

    private sealed record StoredProfileIdentity(string Id, string Name, bool IsDefault);
}
