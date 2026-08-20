namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public async Task UpsertModelAsync(ModelRecord model)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
INSERT INTO models (id, name, model_path, ownership, metadata_json, updated_at)
VALUES ($id, $name, $model_path, $ownership, $metadata_json, $updated_at)
ON CONFLICT(id) DO UPDATE SET
  name = excluded.name,
  model_path = excluded.model_path,
  ownership = excluded.ownership,
  metadata_json = excluded.metadata_json,
  updated_at = excluded.updated_at;
""";
            command.Parameters.AddWithValue("$id", model.Id);
            command.Parameters.AddWithValue("$name", model.Name);
            command.Parameters.AddWithValue("$model_path", model.ModelPath);
            command.Parameters.AddWithValue("$ownership", model.Ownership.ToString());
            command.Parameters.AddWithValue("$metadata_json", model.MetadataJson);
            command.Parameters.AddWithValue("$updated_at", model.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task<IReadOnlyList<ModelRecord>> ListModelsAsync()
    {
        return await WithConnectionAsync<IReadOnlyList<ModelRecord>>(async () =>
        {
            var models = new List<ModelRecord>();
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id, name, model_path, ownership, metadata_json, updated_at FROM models ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                models.Add(new ModelRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    EnumValue(reader.GetString(3), OwnershipKind.External),
                    reader.GetString(4),
                    DateValue(reader.GetString(5))));
            }
            return models;
        });
    }

    public async Task DeleteModelAsync(string id)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM models WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task<ModelLaunchSettings?> GetModelLaunchSettingsAsync(string modelId)
    {
        return await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
SELECT settings_json, is_legacy FROM (
SELECT settings_json, 0 AS is_legacy
FROM model_launch_profiles
WHERE model_id = $model_id AND is_default = 1
UNION ALL
SELECT settings_json, 1 AS is_legacy
FROM model_launch_settings
WHERE model_id = $model_id
)
LIMIT 1;
""";
            command.Parameters.AddWithValue("$model_id", modelId);
            string? json;
            var legacyRow = false;
            await using (var reader = await command.ExecuteReaderAsync())
            {
                if (!await reader.ReadAsync()) return null;
                json = reader.GetString(0);
                legacyRow = reader.GetInt32(1) != 0;
            }
            if (string.IsNullOrWhiteSpace(json)) return null;
            try
            {
                var settings = JsonSerializer.Deserialize<ModelLaunchSettings>(json);
                if (settings is null) return null;
                var migrated = MigrateLegacyModelLaunchDefaults(settings, LooksLikeLegacyModelLaunchDefaultsJson(json), out var changed);
                if (changed)
                {
                    await using var update = _connection.CreateCommand();
                    update.CommandText = """
UPDATE model_launch_profiles
SET settings_json = $settings_json, updated_at = $updated_at
WHERE model_id = $model_id AND is_default = 1;
UPDATE model_launch_settings
SET settings_json = $settings_json, updated_at = $updated_at
WHERE model_id = $model_id;
""";
                    update.Parameters.AddWithValue("$model_id", modelId);
                    update.Parameters.AddWithValue("$settings_json", JsonSerializer.Serialize(migrated));
                    update.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
                    await update.ExecuteNonQueryAsync();
                }
                if (legacyRow)
                    await SaveDefaultProfileUnlockedAsync(modelId, migrated);
                return migrated;
            }
            catch { return null; }
        });
    }

    public async Task SaveModelLaunchSettingsAsync(string modelId, ModelLaunchSettings settings)
    {
        await WithConnectionAsync(async () =>
        {
            await SaveDefaultProfileUnlockedAsync(modelId, settings);
        });
    }

    public async Task DeleteModelLaunchSettingsAsync(string modelId)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
DELETE FROM model_launch_profiles WHERE model_id = $model_id AND is_default = 1;
DELETE FROM model_launch_settings WHERE model_id = $model_id;
""";
            command.Parameters.AddWithValue("$model_id", modelId);
            await command.ExecuteNonQueryAsync();
        });
    }

    private async Task SaveDefaultProfileUnlockedAsync(string modelId, ModelLaunchSettings settings)
    {
        var now = DateTimeOffset.UtcNow.ToString("O");
        var json = JsonSerializer.Serialize(settings);
        await using var update = _connection.CreateCommand();
        update.CommandText = """
UPDATE model_launch_profiles
SET name = 'Default', settings_json = $settings_json, updated_at = $updated_at, is_default = 1
WHERE model_id = $model_id AND (is_default = 1 OR name = 'Default' COLLATE NOCASE);
""";
        update.Parameters.AddWithValue("$model_id", modelId);
        update.Parameters.AddWithValue("$settings_json", json);
        update.Parameters.AddWithValue("$updated_at", now);
        var changed = await update.ExecuteNonQueryAsync();
        if (changed == 0)
        {
            await using var insert = _connection.CreateCommand();
            insert.CommandText = """
INSERT INTO model_launch_profiles (id, model_id, name, settings_json, updated_at, is_default)
VALUES ($id, $model_id, 'Default', $settings_json, $updated_at, 1);
""";
            insert.Parameters.AddWithValue("$id", $"default:{modelId}");
            insert.Parameters.AddWithValue("$model_id", modelId);
            insert.Parameters.AddWithValue("$settings_json", json);
            insert.Parameters.AddWithValue("$updated_at", now);
            await insert.ExecuteNonQueryAsync();
        }

        await using var cleanup = _connection.CreateCommand();
        cleanup.CommandText = "DELETE FROM model_launch_settings WHERE model_id = $model_id;";
        cleanup.Parameters.AddWithValue("$model_id", modelId);
        await cleanup.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<NamedModelLaunchProfile>> ListNamedModelLaunchProfilesAsync(string? modelId = null)
    {
        return await WithConnectionAsync<IReadOnlyList<NamedModelLaunchProfile>>(async () =>
        {
            var profiles = new List<NamedModelLaunchProfile>();
            await using var command = _connection.CreateCommand();
            command.CommandText = string.IsNullOrWhiteSpace(modelId)
                ? "SELECT id, model_id, name, settings_json, updated_at, is_default FROM model_launch_profiles ORDER BY is_default DESC, name;"
                : "SELECT id, model_id, name, settings_json, updated_at, is_default FROM model_launch_profiles WHERE model_id = $model_id ORDER BY is_default DESC, name;";
            if (!string.IsNullOrWhiteSpace(modelId))
                command.Parameters.AddWithValue("$model_id", modelId);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                try
                {
                    var settings = JsonSerializer.Deserialize<ModelLaunchSettings>(reader.GetString(3));
                    if (settings is null) continue;
                    profiles.Add(new NamedModelLaunchProfile(
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        settings,
                        DateValue(reader.GetString(4)),
                        reader.GetInt32(5) != 0));
                }
                catch
                {
                }
            }
            return profiles;
        });
    }

    public async Task<NamedModelLaunchProfile?> GetNamedModelLaunchProfileAsync(string profileId)
        => (await ListNamedModelLaunchProfilesAsync()).FirstOrDefault(profile =>
            string.Equals(profile.Id, profileId, StringComparison.OrdinalIgnoreCase));

    public async Task SaveNamedModelLaunchProfileAsync(NamedModelLaunchProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
INSERT INTO model_launch_profiles (id, model_id, name, settings_json, updated_at, is_default)
VALUES ($id, $model_id, $name, $settings_json, $updated_at, $is_default)
ON CONFLICT(id) DO UPDATE SET
  model_id = excluded.model_id,
  name = excluded.name,
  settings_json = excluded.settings_json,
  updated_at = excluded.updated_at,
  is_default = excluded.is_default;
""";
            command.Parameters.AddWithValue("$id", profile.Id);
            command.Parameters.AddWithValue("$model_id", profile.ModelId);
            command.Parameters.AddWithValue("$name", profile.Name.Trim());
            command.Parameters.AddWithValue("$settings_json", JsonSerializer.Serialize(profile.Settings));
            command.Parameters.AddWithValue("$updated_at", profile.UpdatedAt.ToString("O"));
            command.Parameters.AddWithValue("$is_default", profile.IsDefault ? 1 : 0);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task DeleteNamedModelLaunchProfileAsync(string profileId)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM model_launch_profiles WHERE id = $id;";
            command.Parameters.AddWithValue("$id", profileId);
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task UpsertRuntimeAsync(RuntimeRecord runtime)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
INSERT INTO runtimes (id, name, mode, backend, executable_path, metadata_json, updated_at)
VALUES ($id, $name, $mode, $backend, $executable_path, $metadata_json, $updated_at)
ON CONFLICT(id) DO UPDATE SET
  name = excluded.name,
  mode = excluded.mode,
  backend = excluded.backend,
  executable_path = excluded.executable_path,
  metadata_json = excluded.metadata_json,
  updated_at = excluded.updated_at;
""";
            command.Parameters.AddWithValue("$id", runtime.Id);
            command.Parameters.AddWithValue("$name", runtime.Name);
            command.Parameters.AddWithValue("$mode", runtime.Mode.ToString());
            command.Parameters.AddWithValue("$backend", runtime.Backend.ToString());
            command.Parameters.AddWithValue("$executable_path", runtime.ExecutablePath);
            command.Parameters.AddWithValue("$metadata_json", runtime.MetadataJson);
            command.Parameters.AddWithValue("$updated_at", runtime.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync();
        });
    }

    public async Task<IReadOnlyList<RuntimeRecord>> ListRuntimesAsync()
    {
        return await WithConnectionAsync<IReadOnlyList<RuntimeRecord>>(async () =>
        {
            var runtimes = new List<RuntimeRecord>();
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT id, name, mode, backend, executable_path, metadata_json, updated_at FROM runtimes ORDER BY name;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                runtimes.Add(new RuntimeRecord(
                    reader.GetString(0),
                    reader.GetString(1),
                    EnumValue(reader.GetString(2), RuntimeMode.Native),
                    EnumValue(reader.GetString(3), RuntimeBackend.Cpu),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateValue(reader.GetString(6))));
            }
            return runtimes;
        });
    }

    public async Task DeleteRuntimeAsync(string id)
    {
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "DELETE FROM runtimes WHERE id = $id;";
            command.Parameters.AddWithValue("$id", id);
            await command.ExecuteNonQueryAsync();
        });
    }
}
