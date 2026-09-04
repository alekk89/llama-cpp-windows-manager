namespace LocalLlmConsole.Services;

public sealed partial class StateStore
{
    public Task<string> GetDefaultRuntimeIdAsync()
        => WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = "SELECT runtime_id FROM default_runtime WHERE singleton = 1;";
            return await command.ExecuteScalarAsync() as string ?? "";
        });

    public Task SetDefaultRuntimeAsync(string runtimeId)
        => WithConnectionAsync(async () =>
        {
            await using var command = _connection.CreateCommand();
            command.CommandText = string.IsNullOrWhiteSpace(runtimeId)
                ? "DELETE FROM default_runtime;"
                : "INSERT INTO default_runtime (singleton, runtime_id) VALUES (1, $id) ON CONFLICT(singleton) DO UPDATE SET runtime_id = excluded.runtime_id;";
            if (!string.IsNullOrWhiteSpace(runtimeId))
                command.Parameters.AddWithValue("$id", runtimeId);
            await command.ExecuteNonQueryAsync();
        });
}
