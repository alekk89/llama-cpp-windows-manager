namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    private const int MaximumUiLayoutJsonLength = 256 * 1024;

    public Task<string?> GetUiLayoutStateAsync(string scopeKey)
    {
        ValidateUiLayout(scopeKey, null);
        return WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT layout_json FROM ui_layout_state WHERE scope_key = $scope_key;";
            command.Parameters.AddWithValue("$scope_key", scopeKey);
            return await command.ExecuteScalarAsync() as string;
        });
    }

    public Task SaveUiLayoutStateAsync(string scopeKey, string layoutJson)
    {
        ValidateUiLayout(scopeKey, layoutJson);
        return WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = """
INSERT INTO ui_layout_state (scope_key, layout_json, updated_at)
VALUES ($scope_key, $layout_json, $updated_at)
ON CONFLICT(scope_key) DO UPDATE SET
  layout_json = excluded.layout_json,
  updated_at = excluded.updated_at;
""";
            command.Parameters.AddWithValue("$scope_key", scopeKey);
            command.Parameters.AddWithValue("$layout_json", layoutJson);
            command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync();
        });
    }

    private static void ValidateUiLayout(string scopeKey, string? layoutJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
        if (scopeKey.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(scopeKey), "UI layout scope keys cannot exceed 128 characters.");
        if (layoutJson is null) return;
        ArgumentException.ThrowIfNullOrWhiteSpace(layoutJson);
        if (layoutJson.Length > MaximumUiLayoutJsonLength)
            throw new ArgumentOutOfRangeException(nameof(layoutJson), "UI layout state is unexpectedly large.");
    }
}
