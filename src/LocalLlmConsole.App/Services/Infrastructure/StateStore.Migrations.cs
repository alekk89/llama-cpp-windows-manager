namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    private sealed record SchemaMigration(int Id, string Name, string Sql);

    private static readonly SchemaMigration[] SchemaMigrations =
    [
        new(1, "baseline-v1", ""),
        new(2, "named-model-launch-profiles", """
CREATE TABLE IF NOT EXISTS model_launch_profiles (
  id TEXT PRIMARY KEY,
  model_id TEXT NOT NULL,
  name TEXT NOT NULL COLLATE NOCASE,
  settings_json TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE,
  UNIQUE(model_id, name)
);
WITH alias_profiles AS (
  SELECT
    alias.id AS profile_id,
    COALESCE(
      NULLIF(CASE WHEN json_valid(alias.metadata_json) THEN json_extract(alias.metadata_json, '$.sourceModelId') END, ''),
      (SELECT base.id
       FROM models base
       WHERE base.id <> alias.id
         AND base.model_path = alias.model_path COLLATE NOCASE
         AND NOT (base.ownership = 'RegistryOnly'
                  AND json_valid(base.metadata_json)
                  AND json_extract(base.metadata_json, '$.recordKind') = 'launchAlias')
       ORDER BY CASE base.ownership WHEN 'AppOwned' THEN 0 WHEN 'External' THEN 1 ELSE 2 END
       LIMIT 1)
    ) AS source_model_id,
    alias.name AS profile_name,
    settings.settings_json AS settings_json,
    settings.updated_at AS updated_at
  FROM models alias
  JOIN model_launch_settings settings ON settings.model_id = alias.id
  WHERE alias.ownership = 'RegistryOnly'
    AND json_valid(alias.metadata_json)
    AND json_extract(alias.metadata_json, '$.recordKind') = 'launchAlias'
)
INSERT OR IGNORE INTO model_launch_profiles (id, model_id, name, settings_json, updated_at)
SELECT profile_id, source_model_id, profile_name, settings_json, updated_at
FROM alias_profiles
WHERE source_model_id IS NOT NULL
  AND EXISTS (SELECT 1 FROM models source WHERE source.id = alias_profiles.source_model_id);
DELETE FROM models
WHERE ownership = 'RegistryOnly'
  AND json_valid(metadata_json)
  AND json_extract(metadata_json, '$.recordKind') = 'launchAlias';
"""),
        new(3, "real-default-model-launch-profiles", """
ALTER TABLE model_launch_profiles ADD COLUMN is_default INTEGER NOT NULL DEFAULT 0;
UPDATE model_launch_profiles
SET is_default = 1
WHERE name = 'Default' COLLATE NOCASE;
UPDATE model_launch_profiles
SET settings_json = (
      SELECT saved.settings_json
      FROM model_launch_settings saved
      WHERE saved.model_id = model_launch_profiles.model_id),
    updated_at = (
      SELECT saved.updated_at
      FROM model_launch_settings saved
      WHERE saved.model_id = model_launch_profiles.model_id),
    is_default = 1
WHERE name = 'Default' COLLATE NOCASE
  AND EXISTS (
      SELECT 1 FROM model_launch_settings saved
      WHERE saved.model_id = model_launch_profiles.model_id);
INSERT OR IGNORE INTO model_launch_profiles
  (id, model_id, name, settings_json, updated_at, is_default)
SELECT 'default:' || models.id,
       models.id,
       'Default',
       saved.settings_json,
       saved.updated_at,
       1
FROM models
JOIN model_launch_settings saved ON saved.model_id = models.id
WHERE NOT EXISTS (
  SELECT 1 FROM model_launch_profiles profile
  WHERE profile.model_id = models.id AND profile.is_default = 1);
DELETE FROM model_launch_settings;
CREATE UNIQUE INDEX IF NOT EXISTS ux_model_launch_profiles_default
ON model_launch_profiles(model_id)
WHERE is_default = 1;
"""),
        new(4, "model-groups-and-retention-priority", """
CREATE TABLE IF NOT EXISTS model_groups (
  id TEXT PRIMARY KEY,
  name TEXT NOT NULL COLLATE NOCASE UNIQUE,
  retention_mode TEXT NOT NULL CHECK (retention_mode IN ('Inherit','Pinned','IdleTimeout')),
  idle_minutes INTEGER NOT NULL CHECK (idle_minutes BETWEEN 1 AND 10080),
  eviction_priority TEXT NOT NULL CHECK (eviction_priority IN ('Low','Normal','High')),
  updated_at TEXT NOT NULL
);
CREATE TABLE IF NOT EXISTS model_group_assignments (
  model_id TEXT PRIMARY KEY,
  group_id TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE,
  FOREIGN KEY(group_id) REFERENCES model_groups(id) ON DELETE CASCADE
);
CREATE INDEX IF NOT EXISTS ix_model_group_assignments_group_id
ON model_group_assignments(group_id);
"""),
        new(5, "launch-profile-group-assignments", """
CREATE TABLE IF NOT EXISTS launch_profile_group_assignments (
  launch_profile_id TEXT PRIMARY KEY,
  group_id TEXT NOT NULL,
  updated_at TEXT NOT NULL,
  FOREIGN KEY(launch_profile_id) REFERENCES model_launch_profiles(id) ON DELETE CASCADE,
  FOREIGN KEY(group_id) REFERENCES model_groups(id) ON DELETE CASCADE
);
INSERT OR REPLACE INTO launch_profile_group_assignments (launch_profile_id, group_id, updated_at)
SELECT profile.id, assignment.group_id, assignment.updated_at
FROM model_group_assignments assignment
JOIN model_launch_profiles profile
  ON profile.model_id = assignment.model_id
 AND profile.is_default = 1;
DROP TABLE IF EXISTS model_group_assignments;
CREATE INDEX IF NOT EXISTS ix_launch_profile_group_assignments_group_id
ON launch_profile_group_assignments(group_id);
"""),
        new(6, "hourly-token-usage-history", """
ALTER TABLE token_usage ADD COLUMN cached_prompt_tokens INTEGER NOT NULL DEFAULT 0;
ALTER TABLE token_usage ADD COLUMN cache_counter_observed INTEGER NOT NULL DEFAULT 0;
CREATE TABLE IF NOT EXISTS token_usage_hourly (
  bucket_start_utc TEXT NOT NULL,
  model_id TEXT NOT NULL,
  model_name TEXT NOT NULL,
  launch_profile_id TEXT NOT NULL DEFAULT '',
  launch_profile_name TEXT NOT NULL DEFAULT '',
  runtime_id TEXT NOT NULL DEFAULT '',
  runtime_name TEXT NOT NULL DEFAULT '',
  runtime_mode TEXT NOT NULL,
  runtime_backend TEXT NOT NULL,
  prompt_tokens INTEGER NOT NULL DEFAULT 0 CHECK (prompt_tokens >= 0),
  cached_prompt_tokens INTEGER NOT NULL DEFAULT 0 CHECK (cached_prompt_tokens >= 0),
  generated_tokens INTEGER NOT NULL DEFAULT 0 CHECK (generated_tokens >= 0),
  cache_counter_observed INTEGER NOT NULL DEFAULT 0 CHECK (cache_counter_observed IN (0, 1)),
  updated_at TEXT NOT NULL,
  PRIMARY KEY (bucket_start_utc, model_id, launch_profile_id, runtime_id)
);
CREATE INDEX IF NOT EXISTS ix_token_usage_hourly_time
ON token_usage_hourly(bucket_start_utc);
CREATE INDEX IF NOT EXISTS ix_token_usage_hourly_model_time
ON token_usage_hourly(model_id, bucket_start_utc);
"""),
        new(7, "usage-performance-aggregates", """
ALTER TABLE token_usage_hourly ADD COLUMN prompt_seconds REAL NOT NULL DEFAULT 0 CHECK (prompt_seconds >= 0);
ALTER TABLE token_usage_hourly ADD COLUMN generated_seconds REAL NOT NULL DEFAULT 0 CHECK (generated_seconds >= 0);
ALTER TABLE token_usage_hourly ADD COLUMN timing_counter_observed INTEGER NOT NULL DEFAULT 0 CHECK (timing_counter_observed IN (0, 1));
ALTER TABLE token_usage_hourly ADD COLUMN request_count INTEGER NOT NULL DEFAULT 0 CHECK (request_count >= 0);
ALTER TABLE token_usage_hourly ADD COLUMN failed_request_count INTEGER NOT NULL DEFAULT 0 CHECK (failed_request_count >= 0);
ALTER TABLE token_usage_hourly ADD COLUMN request_counter_observed INTEGER NOT NULL DEFAULT 0 CHECK (request_counter_observed IN (0, 1));
"""),
        new(8, "hourly-host-gpu-energy", """
CREATE TABLE IF NOT EXISTS gpu_energy_hourly (
  bucket_start_utc TEXT PRIMARY KEY,
  watt_hours REAL NOT NULL DEFAULT 0 CHECK (watt_hours >= 0),
  sampled_seconds REAL NOT NULL DEFAULT 0 CHECK (sampled_seconds >= 0),
  complete_coverage INTEGER NOT NULL DEFAULT 1 CHECK (complete_coverage IN (0, 1)),
  observed_gpu_count INTEGER NOT NULL DEFAULT 0 CHECK (observed_gpu_count >= 0),
  detected_gpu_count INTEGER NOT NULL DEFAULT 0 CHECK (detected_gpu_count >= 0),
  updated_at TEXT NOT NULL
);
CREATE INDEX IF NOT EXISTS ix_gpu_energy_hourly_time
ON gpu_energy_hourly(bucket_start_utc);
"""),
        new(9, "hourly-per-gpu-energy", """
CREATE TABLE IF NOT EXISTS gpu_energy_device_hourly (
  bucket_start_utc TEXT NOT NULL,
  sensor_key TEXT NOT NULL,
  gpu_index INTEGER NOT NULL CHECK (gpu_index >= 0),
  gpu_name TEXT NOT NULL,
  watt_hours REAL NOT NULL DEFAULT 0 CHECK (watt_hours >= 0),
  sampled_seconds REAL NOT NULL DEFAULT 0 CHECK (sampled_seconds >= 0),
  updated_at TEXT NOT NULL,
  PRIMARY KEY (bucket_start_utc, sensor_key)
);
CREATE INDEX IF NOT EXISTS ix_gpu_energy_device_hourly_time
ON gpu_energy_device_hourly(bucket_start_utc);
""")
    ];

    private async Task ApplyMigrationsUnlockedAsync()
    {
        var applied = new HashSet<int>();
        await using (var list = _connection.CreateCommand())
        {
            list.CommandText = "SELECT id FROM migrations;";
            await using var reader = await list.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                applied.Add(reader.GetInt32(0));
        }

        foreach (var migration in SchemaMigrations.OrderBy(migration => migration.Id))
        {
            if (applied.Contains(migration.Id)) continue;
            await using var transaction = await _connection.BeginTransactionAsync();
            try
            {
                if (!string.IsNullOrWhiteSpace(migration.Sql))
                {
                    await using var migrate = _connection.CreateCommand();
                    migrate.Transaction = (SqliteTransaction)transaction;
                    migrate.CommandText = migration.Sql;
                    await migrate.ExecuteNonQueryAsync();
                }

                await using var mark = _connection.CreateCommand();
                mark.Transaction = (SqliteTransaction)transaction;
                mark.CommandText = """
INSERT INTO migrations (id, name, applied_at)
VALUES ($id, $name, $applied_at);
""";
                mark.Parameters.AddWithValue("$id", migration.Id);
                mark.Parameters.AddWithValue("$name", migration.Name);
                mark.Parameters.AddWithValue("$applied_at", DateTimeOffset.UtcNow.ToString("O"));
                await mark.ExecuteNonQueryAsync();
                await transaction.CommitAsync();
            }
            catch
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }
}
