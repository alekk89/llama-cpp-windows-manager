using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Microsoft.Data.Sqlite;
using System.Security.Cryptography;

namespace LocalLlmConsole.Tests;

public sealed class DatabaseRecoverySafetyTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(true, 8)]
    [InlineData(false, 1)]
    public async Task StartupPreservesValidDatabaseWhenMigrationCannotRun(bool readOnly, int expectedErrorCode)
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        await using (var store = new StateStore(databasePath))
        {
            await store.InitializeAsync();
            await store.SaveAppSettingsAsync(AppSettings.CreateDefault(root) with { Port = 64123 });
        }
        SqliteConnection.ClearAllPools();
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = readOnly
                ? "DROP TABLE default_runtime; DELETE FROM migrations WHERE id = 15; PRAGMA wal_checkpoint(TRUNCATE);"
                : "DELETE FROM migrations WHERE id = 3; PRAGMA wal_checkpoint(TRUNCATE);";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var before = SHA256.HashData(await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken));
        if (readOnly) File.SetAttributes(databasePath, File.GetAttributes(databasePath) | FileAttributes.ReadOnly);
        var quarantineCalls = 0;
        StateStore? attemptedStore = null;
        try
        {
            var error = await Record.ExceptionAsync(async () =>
            {
                var result = await new StateStoreInitializationService().InitializeAsync(new(
                    root, databasePath,
                    () => attemptedStore = new StateStore(databasePath),
                    path =>
                    {
                        quarantineCalls++;
                        return StateStore.QuarantineDatabaseFiles(path);
                    }));
                await result.StateStore.DisposeAsync();
            });

            Assert.Equal(expectedErrorCode, Assert.IsType<SqliteException>(error).SqliteErrorCode);
            Assert.Equal(0, quarantineCalls);
            Assert.Empty(Directory.EnumerateDirectories(Path.GetDirectoryName(databasePath)!, "corrupt-database-*"));
            SqliteConnection.ClearAllPools();
            Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken)));
            Assert.NotNull(attemptedStore);
            await Assert.ThrowsAsync<ObjectDisposedException>(() => attemptedStore.ListSettingsAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var path in Directory.EnumerateFiles(root, "local-llm-console.db", SearchOption.AllDirectories))
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }
}
