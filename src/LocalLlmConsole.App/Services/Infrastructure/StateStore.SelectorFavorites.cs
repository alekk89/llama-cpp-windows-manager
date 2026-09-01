using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public Task<IReadOnlySet<string>> ListSelectorFavoriteIdsAsync(SelectorFavoriteKind kind)
        => kind switch
        {
            SelectorFavoriteKind.LaunchProfile => ListFavoriteLaunchProfileIdsAsync(),
            SelectorFavoriteKind.Model => ListFavoriteIdsAsync("favorite_models", "model_id"),
            SelectorFavoriteKind.Runtime => ListFavoriteIdsAsync("favorite_runtimes", "runtime_id"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };

    public async Task<bool> ToggleSelectorFavoriteAsync(SelectorFavoriteKind kind, string itemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        if (kind == SelectorFavoriteKind.LaunchProfile)
        {
            var favorite = !await IsLaunchProfileFavoriteAsync(itemId);
            await SetLaunchProfileFavoriteAsync(itemId, favorite);
            return favorite;
        }

        var (table, column) = FavoriteTable(kind);
        return await WithConnectionAsync(async () =>
        {
            await using var exists = _connection.CreateCommand();
            exists.CommandText = $"SELECT EXISTS(SELECT 1 FROM {table} WHERE {column} = $item_id);";
            exists.Parameters.AddWithValue("$item_id", itemId);
            var favorite = Convert.ToInt32(await exists.ExecuteScalarAsync(), CultureInfo.InvariantCulture) == 0;

            await using var update = _connection.CreateCommand();
            update.CommandText = favorite
                ? $"INSERT INTO {table} ({column}, updated_at) VALUES ($item_id, $updated_at) ON CONFLICT({column}) DO UPDATE SET updated_at = excluded.updated_at;"
                : $"DELETE FROM {table} WHERE {column} = $item_id;";
            update.Parameters.AddWithValue("$item_id", itemId);
            if (favorite)
                update.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            await update.ExecuteNonQueryAsync();
            return favorite;
        });
    }

    private async Task<IReadOnlySet<string>> ListFavoriteIdsAsync(string table, string column)
    {
        return await WithConnectionAsync<IReadOnlySet<string>>(async () =>
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            await using var command = _connection.CreateCommand();
            command.CommandText = $"SELECT {column} FROM {table} ORDER BY updated_at, {column};";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                ids.Add(reader.GetString(0));
            return ids;
        });
    }

    private static (string Table, string Column) FavoriteTable(SelectorFavoriteKind kind)
        => kind switch
        {
            SelectorFavoriteKind.Model => ("favorite_models", "model_id"),
            SelectorFavoriteKind.Runtime => ("favorite_runtimes", "runtime_id"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
}
