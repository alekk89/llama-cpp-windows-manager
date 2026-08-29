using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ActiveRuntimeSessionStoreTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task PersistsProtectedCredentialsAndClearsSession()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        var apiKey = "recovery-secret-" + new string('a', 32);
        var backupKey = "recovery-backup-" + new string('b', 32);
        var session = CreateRecoverySession(root, "model-id", 8081) with
        {
            LaunchSettings = AppSettings.CreateDefault(root) with
            {
                ModelApiKey = apiKey,
                ModelApiKeyBackup = backupKey
            }
        };

        await store.SaveAsync(session, TestContext.Current.CancellationToken);
        var loaded = await store.TryReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(session.ModelId, loaded.ModelId);
        Assert.Equal(session.ProcessMarker, loaded.ProcessMarker);
        Assert.Equal(apiKey, loaded.LaunchSettings.ModelApiKey);
        Assert.Empty(loaded.LaunchSettings.ModelApiKeyBackup);
        var persistedJson = await File.ReadAllTextAsync(store.SessionsPath, TestContext.Current.CancellationToken);
        Assert.DoesNotContain(apiKey, persistedJson, StringComparison.Ordinal);
        Assert.DoesNotContain(backupKey, persistedJson, StringComparison.Ordinal);
        Assert.Contains("dpapi:v1:", persistedJson, StringComparison.Ordinal);

        store.Clear();
        Assert.Null(await store.TryReadAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task PreservesCorruptLegacySessionForDiagnosis()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.SessionPath)!);
        await File.WriteAllTextAsync(store.SessionPath, "{not-json", TestContext.Current.CancellationToken);

        Assert.Null(await store.TryReadAsync(TestContext.Current.CancellationToken));
        Assert.True(File.Exists(store.SessionPath));
    }

    [Fact]
    public async Task RecoversFromLastKnownGoodBackup()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        var first = CreateRecoverySession(root, "model-first", 8111);

        await store.SaveAsync(first, TestContext.Current.CancellationToken);
        await store.SaveAsync(CreateRecoverySession(root, "model-second", 8112), TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(store.SessionsPath, "{truncated", TestContext.Current.CancellationToken);

        Assert.Equal(first.ModelId, (await store.TryReadAsync(TestContext.Current.CancellationToken))?.ModelId);
    }

    [Fact]
    public async Task SerializesConcurrentWrites()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        await Task.WhenAll(Enumerable.Range(0, 12).Select(index => store.SaveAsync(
            CreateRecoverySession(root, $"model-{index}", 8200 + index),
            TestContext.Current.CancellationToken)));

        var loaded = await store.ReadAllAsync(TestContext.Current.CancellationToken);
        Assert.Single(loaded);
        Assert.StartsWith("model-", loaded[0].ModelId, StringComparison.Ordinal);
        System.Text.Json.JsonDocument.Parse(
            await File.ReadAllTextAsync(store.SessionsPath, TestContext.Current.CancellationToken));
    }

    private static ActiveRuntimeSession CreateRecoverySession(string root, string modelId, int port)
        => new(
            modelId,
            "runtime-id",
            AppSettings.CreateDefault(root) with
            {
                Port = port,
                ModelApiKey = "recovery-secret-" + new string('x', 32)
            },
            Path.Combine(root, "logs", modelId + ".log"),
            DateTimeOffset.UtcNow,
            "marker-" + modelId,
            1234,
            "session-" + modelId);
}
