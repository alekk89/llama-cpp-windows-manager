namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public async Task<IReadOnlySet<string>> ListFavoriteLaunchProfileIdsAsync()
    {
        return await WithConnectionAsync<IReadOnlySet<string>>(async () =>
        {
            var profileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT launch_profile_id FROM tray_favorite_launch_profiles ORDER BY updated_at, launch_profile_id;";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                profileIds.Add(reader.GetString(0));
            return profileIds;
        });
    }

    public async Task<bool> IsLaunchProfileFavoriteAsync(string profileId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        return await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT EXISTS(SELECT 1 FROM tray_favorite_launch_profiles WHERE launch_profile_id = $profile_id);";
            command.Parameters.AddWithValue("$profile_id", profileId);
            return Convert.ToInt32(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture) != 0;
        });
    }

    public async Task SetLaunchProfileFavoriteAsync(string profileId, bool favorite)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profileId);
        await WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            if (favorite)
            {
                command.CommandText = """
INSERT INTO tray_favorite_launch_profiles (launch_profile_id, updated_at)
VALUES ($profile_id, $updated_at)
ON CONFLICT(launch_profile_id) DO UPDATE SET updated_at = excluded.updated_at;
INSERT INTO favorite_models (model_id, updated_at)
SELECT model_id, $updated_at FROM model_launch_profiles WHERE id = $profile_id
ON CONFLICT(model_id) DO UPDATE SET updated_at = excluded.updated_at;
""";
                command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            }
            else
            {
                command.CommandText = "DELETE FROM tray_favorite_launch_profiles WHERE launch_profile_id = $profile_id;";
            }
            command.Parameters.AddWithValue("$profile_id", profileId);
            await command.ExecuteNonQueryAsync();
        });
    }
}
