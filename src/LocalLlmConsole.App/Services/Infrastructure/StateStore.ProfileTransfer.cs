namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public Task ApplyProfileImportAsync(string modelId, long expectedRevision,
        IReadOnlyList<NamedModelLaunchProfile> profiles)
        => WithConnectionAsync(async () =>
        {
            if (CatalogRevision != expectedRevision)
                throw new InvalidOperationException("The catalog changed after preview. Review the import again.");
            if (profiles.Count == 0 || profiles.Any(profile => profile.ModelId != modelId))
                throw new InvalidOperationException("An import must contain profiles for exactly one target model.");
            await using var transaction = _connection.BeginTransaction();
            InvalidateCatalog();
            foreach (var profile in profiles) await WriteLaunchProfileUnlockedAsync(profile, transaction);
            await transaction.CommitAsync();
        });

    private async Task WriteLaunchProfileUnlockedAsync(NamedModelLaunchProfile profile, SqliteTransaction? transaction)
    {
        await using var command = _connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
INSERT INTO model_launch_profiles (id, model_id, name, settings_json, updated_at, is_default, annotations_json)
VALUES ($id, $model_id, $name, $settings_json, $updated_at, $is_default, $annotations_json)
ON CONFLICT(id) DO UPDATE SET
  model_id = excluded.model_id,
  name = excluded.name,
  settings_json = excluded.settings_json,
  updated_at = excluded.updated_at,
  is_default = excluded.is_default,
  annotations_json = COALESCE(excluded.annotations_json, model_launch_profiles.annotations_json);
""";
        command.Parameters.AddWithValue("$id", profile.Id);
        command.Parameters.AddWithValue("$model_id", profile.ModelId);
        command.Parameters.AddWithValue("$name", profile.Name.Trim());
        command.Parameters.AddWithValue("$settings_json", JsonSerializer.Serialize(profile.Settings));
        command.Parameters.AddWithValue("$updated_at", profile.UpdatedAt.ToString("O"));
        command.Parameters.AddWithValue("$is_default", profile.IsDefault ? 1 : 0);
        command.Parameters.AddWithValue("$annotations_json", profile.Annotations is null ? DBNull.Value : JsonSerializer.Serialize(profile.Annotations));
        await command.ExecuteNonQueryAsync();
    }
}
