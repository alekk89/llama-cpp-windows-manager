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

    [Fact]
    public async Task StartupRefusesDatabaseMigratedByNewerVersionWithoutChangingIt()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        await using (var store = new StateStore(databasePath))
        {
            await store.InitializeAsync();
            await store.SaveAppSettingsAsync(AppSettings.CreateDefault(root) with { Port = 64124 });
        }
        SqliteConnection.ClearAllPools();
        var futureId = StateStore.LatestKnownSchemaMigrationId + 7;
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            // A table the current build would recreate proves that no setup statement ran.
            command.CommandText = $"""
DROP TABLE token_usage;
INSERT INTO migrations (id, name, applied_at) VALUES ({futureId}, 'future-schema', '2030-01-01T00:00:00Z');
PRAGMA wal_checkpoint(TRUNCATE);
""";
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }
        var before = SHA256.HashData(await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken));
        var quarantineCalls = 0;
        StateStore? attemptedStore = null;

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

        var tooNew = Assert.IsType<StateStoreSchemaTooNewException>(error);
        Assert.Equal(futureId, tooNew.MigrationId);
        Assert.Equal("future-schema", tooNew.MigrationName);
        Assert.Equal(StateStore.LatestKnownSchemaMigrationId, tooNew.LatestKnownMigrationId);
        Assert.Contains("newer version", tooNew.Message, StringComparison.Ordinal);
        Assert.Contains(databasePath, tooNew.Message, StringComparison.Ordinal);
        Assert.Equal(0, quarantineCalls);
        Assert.NotNull(attemptedStore);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => attemptedStore.ListSettingsAsync());
        SqliteConnection.ClearAllPools();
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(databasePath, TestContext.Current.CancellationToken)));
        await using (var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False;Mode=ReadOnly"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'token_usage';";
            Assert.Equal(0L, (long)(await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
        }
        SqliteConnection.ClearAllPools();
    }

    [Fact]
    public async Task DisposalCompletesQueuedOperationsWithStoreDisposedError()
    {
        var root = CreateTempRoot();
        var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        // Hold the database gate so disposal and the callers queue deterministically behind it.
        var gate = (SemaphoreSlim)typeof(StateStore)
            .GetField("_dbLock", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(store)!;
        await gate.WaitAsync(TestContext.Current.CancellationToken);

        var disposals = new[] { store.DisposeAsync().AsTask(), store.DisposeAsync().AsTask() };
        var operations = Enumerable.Range(0, 8).Select(_ => store.ListSettingsAsync()).ToArray();
        Assert.All(operations, operation => Assert.False(operation.IsCompleted));
        gate.Release();
        var all = Task.WhenAll(operations.Cast<Task>().Concat(disposals));
        var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(15), TestContext.Current.CancellationToken));

        Assert.Same(all, completed);
        await Task.WhenAll(disposals);
        foreach (var operation in operations)
        {
            var error = await Record.ExceptionAsync(() => operation);
            Assert.Equal(nameof(StateStore), Assert.IsType<ObjectDisposedException>(error).ObjectName);
        }
        Assert.Equal(nameof(StateStore), (await Assert.ThrowsAsync<ObjectDisposedException>(() => store.ListSettingsAsync())).ObjectName);
        await store.DisposeAsync();
        SqliteConnection.ClearAllPools();
    }
}
