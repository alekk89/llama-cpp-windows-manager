namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public async Task<IReadOnlyList<string>> ListStartupLaunchProfileIdsAsync()
    {
        return await WithConnectionAsync<IReadOnlyList<string>>(async () =>
        {
            var profileIds = new List<string>();
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT launch_profile_id FROM startup_launch_profiles ORDER BY position, updated_at, launch_profile_id;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                profileIds.Add(reader.GetString(0));
            return profileIds;
        });
    }

    public async Task SetStartupLaunchProfileAsync(string profileId, bool loadOnStartup)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await WithConnectionAsync(async () =>
        {
            await using var transaction = await _connection.BeginTransactionAsync();
            await using var command = _connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            if (loadOnStartup)
            {
                command.CommandText = """
INSERT INTO startup_launch_profiles (launch_profile_id, position, updated_at)
SELECT $profile_id,
       COALESCE((SELECT MAX(position) + 1 FROM startup_launch_profiles), 0),
       $updated_at
WHERE EXISTS (SELECT 1 FROM model_launch_profiles WHERE id = $profile_id)
ON CONFLICT(launch_profile_id) DO NOTHING;
""";
                command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            }
            else
            {
                command.CommandText = "DELETE FROM startup_launch_profiles WHERE launch_profile_id = $profile_id;";
            }
            command.Parameters.AddWithValue("$profile_id", profileId);
            await command.ExecuteNonQueryAsync();

            await using var compact = _connection.CreateCommand();
            compact.Transaction = (SqliteTransaction)transaction;
            compact.CommandText = """
WITH ordered AS (
  SELECT launch_profile_id,
         ROW_NUMBER() OVER (ORDER BY position, updated_at, launch_profile_id) - 1 AS new_position
  FROM startup_launch_profiles
)
UPDATE startup_launch_profiles
SET position = (SELECT new_position FROM ordered WHERE ordered.launch_profile_id = startup_launch_profiles.launch_profile_id);
""";
            await compact.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
        });
    }
}
