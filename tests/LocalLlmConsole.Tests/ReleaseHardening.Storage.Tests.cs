using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task StateStoreHandlesConcurrentJobTraffic()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();

        var writes = Enumerable.Range(0, 40).Select(async index =>
        {
            var now = DateTimeOffset.UtcNow;
            await store.UpsertJobAsync(new JobRecord(
                $"job-{index}",
                "test",
                JobStatus.Running,
                "{}",
                Path.Combine(root, "logs", $"job-{index}.log"),
                now,
                now));
            _ = await store.ListJobsAsync();
        });

        await Task.WhenAll(writes);

        var jobs = await store.ListJobsAsync();
        Assert.Equal(40, jobs.Count);

        await store.DeleteJobAsync("job-0");
        jobs = await store.ListJobsAsync();

        Assert.Equal(39, jobs.Count);
        Assert.DoesNotContain(jobs, job => job.Id == "job-0");
    }


    [Fact]
    public async Task StateStoreReadsSingleJobById()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var job = new JobRecord(
            "job-single",
            "test",
            JobStatus.Running,
            "{}",
            Path.Combine(root, "logs", "job-single.log"),
            now,
            now);

        await store.UpsertJobAsync(job);

        Assert.Equal(job, await store.GetJobAsync(job.Id));
        Assert.Null(await store.GetJobAsync("missing"));
    }


    [Fact]
    public async Task JobEngineValidatesStatusTransitionsAgainstStoredJobState()
    {
        var root = CreateTempRoot();
        var stateStoreJobsSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "StateStore.Jobs.cs"));
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var job = await jobs.CreateAsync("test", "{}", TestContext.Current.CancellationToken);

        await jobs.UpdateAsync(job, JobStatus.Running, """{"step":1}""", TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Completed, """{"step":2}""", TestContext.Current.CancellationToken);
        var invalid = await Assert.ThrowsAsync<InvalidOperationException>(
            () => jobs.UpdateAsync(job, JobStatus.Running, """{"step":3}""", TestContext.Current.CancellationToken));
        var stored = await store.GetJobAsync(job.Id);

        Assert.Contains("Completed -> Running", invalid.Message, StringComparison.Ordinal);
        Assert.NotNull(stored);
        Assert.Equal(JobStatus.Completed, stored.Status);
        Assert.Equal("""{"step":2}""", stored.PayloadJson);
        Assert.True(JobEngine.IsValidStatusTransition(JobStatus.Failed, JobStatus.Queued));
        Assert.False(JobEngine.IsValidStatusTransition(JobStatus.Completed, JobStatus.Running));
        Assert.Contains("TryUpdateJobAsync", stateStoreJobsSource, StringComparison.Ordinal);
        Assert.Contains("AND status = $expected_status", stateStoreJobsSource, StringComparison.Ordinal);
    }


    [Fact]
    public async Task StateStoreRecordsSchemaMigrationsIdempotently()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        await using (var store = new StateStore(databasePath))
        {
            await store.InitializeAsync();
            await store.InitializeAsync();
        }

        await using var connection = new SqliteConnection($"Data Source={databasePath}");
        await connection.OpenAsync(TestContext.Current.CancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*), MIN(name) FROM migrations WHERE id = 1;";
        await using var reader = await command.ExecuteReaderAsync(TestContext.Current.CancellationToken);

        Assert.True(await reader.ReadAsync(TestContext.Current.CancellationToken));
        Assert.Equal(1, reader.GetInt32(0));
        Assert.Equal("baseline-v1", reader.GetString(1));
    }


    [Fact]
    public async Task StateStoreMigratesLegacyModelAliasesIntoNamedLaunchProfiles()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        var now = DateTimeOffset.UtcNow;
        var baseModel = new ModelRecord("model-qwen", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", now);
        var alias = new ModelRecord(
            "variant-qwen-32k",
            "Qwen 32K",
            baseModel.ModelPath,
            OwnershipKind.RegistryOnly,
            ModelAliasService.CreateMetadata(baseModel, [baseModel]),
            now);
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with
        {
            Port = 8097,
            ContextSize = 32768
        }, "runtime-cuda");

        await using (var store = new StateStore(databasePath))
        {
            await store.InitializeAsync();
            await store.UpsertModelAsync(baseModel);
            await store.UpsertModelAsync(alias);
        }

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
DELETE FROM model_launch_profiles;
INSERT INTO model_launch_settings (model_id, settings_json, updated_at)
VALUES ($model_id, $settings_json, $updated_at);
DELETE FROM migrations WHERE id = 2;
""";
            command.Parameters.AddWithValue("$model_id", alias.Id);
            command.Parameters.AddWithValue("$settings_json", System.Text.Json.JsonSerializer.Serialize(settings));
            command.Parameters.AddWithValue("$updated_at", now.ToString("O"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        await using var migrated = new StateStore(databasePath);
        await migrated.InitializeAsync();
        var models = await migrated.ListModelsAsync();
        var profile = Assert.Single(await migrated.ListNamedModelLaunchProfilesAsync(baseModel.Id));

        Assert.Single(models);
        Assert.Equal(baseModel.Id, models[0].Id);
        Assert.Equal(alias.Id, profile.Id);
        Assert.Equal(alias.Name, profile.Name);
        Assert.Equal(8097, profile.Settings.Port);
        Assert.Equal(32768, profile.Settings.ContextSize);
        Assert.Equal("runtime-cuda", profile.Settings.RuntimeId);
    }


    [Fact]
    public void StateStoreQuarantinesCorruptDatabaseFiles()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        File.WriteAllText(databasePath, "db");
        File.WriteAllText(databasePath + "-wal", "wal");
        File.WriteAllText(databasePath + "-shm", "shm");

        var quarantine = StateStore.QuarantineDatabaseFiles(databasePath);

        Assert.False(File.Exists(databasePath));
        Assert.False(File.Exists(databasePath + "-wal"));
        Assert.False(File.Exists(databasePath + "-shm"));
        Assert.Equal("db", File.ReadAllText(Path.Combine(quarantine, "local-llm-console.db")));
        Assert.Equal("wal", File.ReadAllText(Path.Combine(quarantine, "local-llm-console.db-wal")));
        Assert.Equal("shm", File.ReadAllText(Path.Combine(quarantine, "local-llm-console.db-shm")));
    }


    [Fact]
    public async Task StateStorePersistsLanModelAccessSettings()
    {
        var root = CreateTempRoot();
        var apiKey = new string('k', 32);
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();

        var settings = AppSettings.CreateDefault(root) with
        {
            ModelAccessMode = "lan",
            Host = "0.0.0.0",
            ModelApiKey = apiKey,
            ModelApiKeyBackup = apiKey
        };

        await store.SaveAppSettingsAsync(settings);
        var loaded = await store.GetAppSettingsAsync(root);

        Assert.Equal("both", loaded.ModelAccessMode);
        Assert.Equal("0.0.0.0", loaded.Host);
        Assert.Equal(apiKey, loaded.ModelApiKey);
    }


    [Fact]
    public async Task DeletingModelFreesSavedLaunchProfilePort()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var model = new ModelRecord(
            "model-1",
            "Test Model",
            Path.Combine(root, "models", "test.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);

        await store.UpsertModelAsync(model);
        await store.SaveModelLaunchSettingsAsync(model.Id, ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with { Port = 8083 }));
        await store.DeleteModelAsync(model.Id);

        Assert.Null(await store.GetModelLaunchSettingsAsync(model.Id));
    }


    [Fact]
    public async Task ActiveRuntimeSessionStorePersistsAndClearsSession()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = new string('a', 32) };
        var session = new ActiveRuntimeSession(
            "model-id",
            "runtime-id",
            settings,
            Path.Combine(root, "logs", "runtime.log"),
            DateTimeOffset.UtcNow,
            "marker",
            1234);

        await store.SaveAsync(session, TestContext.Current.CancellationToken);
        var loaded = await store.TryReadAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(loaded);
        Assert.Equal(session.ModelId, loaded.ModelId);
        Assert.Equal(session.RuntimeId, loaded.RuntimeId);
        Assert.Equal(session.ProcessMarker, loaded.ProcessMarker);
        Assert.Equal(session.ProcessId, loaded.ProcessId);

        store.Clear();
        Assert.Null(await store.TryReadAsync(TestContext.Current.CancellationToken));
    }


    [Fact]
    public async Task ActiveRuntimeSessionStoreClearsCorruptSession()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        Directory.CreateDirectory(Path.GetDirectoryName(store.SessionPath)!);
        await File.WriteAllTextAsync(store.SessionPath, "{not-json", TestContext.Current.CancellationToken);

        var loaded = await store.TryReadAsync(TestContext.Current.CancellationToken);

        Assert.Null(loaded);
        Assert.False(File.Exists(store.SessionPath));
    }


    [Fact]
    public async Task StateStorePreservesCurrentAppLaunchValuesThatMatchLegacyDefaults()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();

        await store.SaveAppSettingsAsync(AppSettings.CreateDefault(root) with
        {
            ContextSize = 0,
            GpuLayers = 0,
            BatchSize = 2048,
            CacheTypeK = "f16",
            CacheTypeV = "f16",
            Temperature = 0.8,
            MicroBatchSize = 256,
            VisionImageMinTokens = 256,
            VisionImageMaxTokens = 1024
        });

        var loaded = await store.GetAppSettingsAsync(root);

        Assert.Equal(0, loaded.ContextSize);
        Assert.Equal(0, loaded.GpuLayers);
        Assert.Equal(2048, loaded.BatchSize);
        Assert.Equal("f16", loaded.CacheTypeK);
        Assert.Equal("f16", loaded.CacheTypeV);
        Assert.Equal(0.8, loaded.Temperature);
        Assert.Equal(256, loaded.MicroBatchSize);
        Assert.Equal(256, loaded.VisionImageMinTokens);
        Assert.Equal(1024, loaded.VisionImageMaxTokens);
    }


    [Fact]
    public async Task StateStoreMigratesOnlyLegacyAppLaunchDefaults()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        await using var store = new StateStore(databasePath);
        await store.InitializeAsync();

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            foreach (var (key, value) in new (string Key, object Value)[]
            {
                ("contextSize", 0),
                ("gpuLayers", 0),
                ("batchSize", 2048),
                ("cacheTypeK", "f16"),
                ("cacheTypeV", "f16"),
                ("temperature", 0.8),
                ("microBatchSize", 256)
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = """
INSERT INTO settings (key, value_json, updated_at)
VALUES ($key, $value_json, $updated_at)
ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;
""";
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$value_json", System.Text.Json.JsonSerializer.Serialize(value, value.GetType()));
                command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
                await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
            }
        }

        var migrated = await store.GetAppSettingsAsync(root);

        Assert.Equal(131_072, migrated.ContextSize);
        Assert.Equal(999, migrated.GpuLayers);
        Assert.Equal(4096, migrated.BatchSize);
        Assert.Equal("q8_0", migrated.CacheTypeK);
        Assert.Equal("q8_0", migrated.CacheTypeV);
        Assert.Equal(0.65, migrated.Temperature);
        Assert.Equal(256, migrated.MicroBatchSize);
        Assert.Equal(0, migrated.VisionImageMinTokens);
        Assert.Equal(0, migrated.VisionImageMaxTokens);
    }


    [Fact]
    public async Task StateStorePreservesCurrentModelLaunchValuesThatMatchLegacyDefaults()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertModelAsync(new ModelRecord(
            "model-1",
            "Test Model",
            Path.Combine(root, "models", "test.gguf"),
            OwnershipKind.External,
            "{}",
            now));

        await store.SaveModelLaunchSettingsAsync("model-1", ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with
        {
            ContextSize = 0,
            GpuLayers = 0,
            BatchSize = 2048,
            CacheTypeK = "f16",
            CacheTypeV = "f16",
            Temperature = 0.8,
            MicroBatchSize = 256,
            VisionImageMinTokens = 256,
            VisionImageMaxTokens = 1024,
            CustomParameters = "--device-draft CUDA1",
            GpuMode = "layer",
            GpuDevices = "CUDA0,CUDA1",
            GpuSplit = "3,1"
        }));

        var loaded = await store.GetModelLaunchSettingsAsync("model-1");

        Assert.NotNull(loaded);
        Assert.Equal(0, loaded.ContextSize);
        Assert.Equal(0, loaded.GpuLayers);
        Assert.Equal(2048, loaded.BatchSize);
        Assert.Equal("f16", loaded.CacheTypeK);
        Assert.Equal("f16", loaded.CacheTypeV);
        Assert.Equal(0.8, loaded.Temperature);
        Assert.Equal(256, loaded.MicroBatchSize);
        Assert.Equal(256, loaded.VisionImageMinTokens);
        Assert.Equal(1024, loaded.VisionImageMaxTokens);
        Assert.Equal("--device-draft CUDA1", loaded.CustomParameters);
        Assert.Equal("layer", loaded.GpuMode);
        Assert.Equal("CUDA0,CUDA1", loaded.GpuDevices);
        Assert.Equal("3,1", loaded.GpuSplit);
    }


    [Fact]
    public async Task StateStoreMigratesLegacySavedModelLaunchDefaults()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        await using var store = new StateStore(databasePath);
        await store.InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        await store.UpsertModelAsync(new ModelRecord(
            "model-1",
            "Test Model",
            Path.Combine(root, "models", "test.gguf"),
            OwnershipKind.External,
            "{}",
            now));
        var legacySettings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root) with
        {
            ContextSize = 0,
            GpuLayers = 0,
            BatchSize = 2048,
            CacheTypeK = "f16",
            CacheTypeV = "f16",
            Temperature = 0.8,
            MicroBatchSize = 256,
            VisionImageMinTokens = 256,
            VisionImageMaxTokens = 1024
        });
        var legacyJson = System.Text.Json.JsonSerializer.SerializeToNode(legacySettings)!.AsObject();
        legacyJson.Remove(nameof(ModelLaunchSettings.SpeculativeType));
        legacyJson.Remove(nameof(ModelLaunchSettings.SpecDraftModelPath));
        legacyJson.Remove(nameof(ModelLaunchSettings.MtpHeadPath));
        legacyJson.Remove(nameof(ModelLaunchSettings.VisionImageMinTokens));
        legacyJson.Remove(nameof(ModelLaunchSettings.VisionImageMaxTokens));

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
INSERT INTO model_launch_settings (model_id, settings_json, updated_at)
VALUES ($model_id, $settings_json, $updated_at);
""";
            command.Parameters.AddWithValue("$model_id", "model-1");
            command.Parameters.AddWithValue("$settings_json", legacyJson.ToJsonString());
            command.Parameters.AddWithValue("$updated_at", now.ToString("O"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var migrated = await store.GetModelLaunchSettingsAsync("model-1");

        Assert.NotNull(migrated);
        Assert.Equal(131_072, migrated.ContextSize);
        Assert.Equal(999, migrated.GpuLayers);
        Assert.Equal(4096, migrated.BatchSize);
        Assert.Equal("q8_0", migrated.CacheTypeK);
        Assert.Equal("q8_0", migrated.CacheTypeV);
        Assert.Equal(0.65, migrated.Temperature);
        Assert.Equal(256, migrated.MicroBatchSize);
        Assert.Equal(0, migrated.VisionImageMinTokens);
        Assert.Equal(0, migrated.VisionImageMaxTokens);
        var defaultProfile = Assert.Single(await store.ListNamedModelLaunchProfilesAsync("model-1"));
        Assert.True(defaultProfile.IsDefault);
        Assert.Equal("Default", defaultProfile.Name);
        await using var verify = new SqliteConnection($"Data Source={databasePath}");
        await verify.OpenAsync(TestContext.Current.CancellationToken);
        await using var countLegacy = verify.CreateCommand();
        countLegacy.CommandText = "SELECT COUNT(*) FROM model_launch_settings WHERE model_id = 'model-1';";
        Assert.Equal(0L, (long)(await countLegacy.ExecuteScalarAsync(TestContext.Current.CancellationToken))!);
    }


    [Fact]
    public async Task StateStoreProtectsModelApiKeyAtRest()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var apiKey = new string('a', 64);
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = apiKey };

        await store.SaveAppSettingsAsync(settings);
        var rawSettings = await store.ListSettingsAsync();
        var loaded = await store.GetAppSettingsAsync(root);

        Assert.Equal(apiKey, loaded.ModelApiKey);
        Assert.True(rawSettings.TryGetValue("modelApiKey", out var rawApiKey));
        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("dpapi:v1:", rawApiKey, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, rawApiKey, StringComparison.Ordinal);
        }
    }


    [Fact]
    public void WorkspaceRootDefaultsToDataFolderBesideExecutable()
    {
        var root = CreateTempRoot();
        var executable = Path.Combine(root, "LlamaCppWindowsManager.exe");
        var localAppData = Path.Combine(root, "localappdata");

        var workspace = WorkspaceRootResolver.Resolve(null, executable, localAppData);

        Assert.Equal(Path.Combine(root, "data"), workspace, ignoreCase: true);
        Assert.True(Directory.Exists(workspace));
    }


    [Fact]
    public void WorkspaceRootEnvironmentOverrideWins()
    {
        var root = CreateTempRoot();
        var executable = Path.Combine(root, "LlamaCppWindowsManager.exe");
        var overrideRoot = Path.Combine(root, "custom-workspace");

        var workspace = WorkspaceRootResolver.Resolve(overrideRoot, executable, Path.Combine(root, "localappdata"));

        Assert.Equal(Path.GetFullPath(overrideRoot), workspace, ignoreCase: true);
    }


    [Fact]
    public void WorkspaceRootFallbackUsesNewNameButKeepsLegacyFolder()
    {
        var root = CreateTempRoot();
        var localAppData = Path.Combine(root, "localappdata");
        var freshWorkspace = WorkspaceRootResolver.Resolve(null, null, localAppData);

        Assert.Equal(Path.Combine(localAppData, "llama.cpp Windows Manager"), freshWorkspace, ignoreCase: true);
        Assert.Equal("LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE", WorkspaceRootResolver.EnvironmentVariable);
        Assert.Equal("LLAMA_CPP_CONSOLE_WORKSPACE", WorkspaceRootResolver.LegacyConsoleEnvironmentVariable);
        Assert.Equal("LOCAL_LLM_CONSOLE_WORKSPACE", WorkspaceRootResolver.LegacyEnvironmentVariable);

        var legacyWorkspace = Path.Combine(localAppData, "llama.cpp Console");
        Directory.CreateDirectory(legacyWorkspace);

        var reusedWorkspace = WorkspaceRootResolver.Resolve(null, null, localAppData);

        Assert.Equal(legacyWorkspace, reusedWorkspace, ignoreCase: true);

        Directory.Delete(legacyWorkspace, recursive: true);
        var legacyCodeWorkspace = Path.Combine(localAppData, "LocalLlmConsole");
        Directory.CreateDirectory(legacyCodeWorkspace);

        var reusedCodeWorkspace = WorkspaceRootResolver.Resolve(null, null, localAppData);

        Assert.Equal(legacyCodeWorkspace, reusedCodeWorkspace, ignoreCase: true);
    }


    [Fact]
    public async Task StateStoreHandlesSemicolonInDatabasePath()
    {
        var root = Path.Combine(Path.GetTempPath(), "LocalLlmConsole.Tests", $"semi;colon-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "state", "local;llm;console.db");
        await using var store = new StateStore(databasePath);

        await store.InitializeAsync();
        await store.UpsertJobAsync(new JobRecord(
            "job",
            "test",
            JobStatus.Completed,
            "{}",
            Path.Combine(root, "logs", "job.log"),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

        Assert.True(File.Exists(databasePath));
        Assert.Single(await store.ListJobsAsync());
    }


    [Fact]
    public void CacheMaintenanceServiceClearsOnlyWorkspaceCache()
    {
        var root = CreateTempRoot();
        var cacheRoot = Path.Combine(root, "cache");
        var nested = Path.Combine(cacheRoot, "nested");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(cacheRoot, "a.bin"), "1234");
        File.WriteAllText(Path.Combine(nested, "b.bin"), "123456");
        var external = Path.Combine(Path.GetTempPath(), $"llm-console-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(external);

        try
        {
            Assert.True(CacheMaintenanceService.IsSafeCacheRoot(root, cacheRoot));
            Assert.False(CacheMaintenanceService.IsSafeCacheRoot(root, external));
            Assert.Equal(10, CacheMaintenanceService.Size(cacheRoot));

            CacheMaintenanceService.ClearSafeCacheRoot(root, cacheRoot);

            Assert.True(Directory.Exists(cacheRoot));
            Assert.Empty(Directory.EnumerateFileSystemEntries(cacheRoot));
            Assert.Throws<InvalidOperationException>(() => CacheMaintenanceService.ClearSafeCacheRoot(root, external));
        }
        finally
        {
            if (Directory.Exists(external))
                Directory.Delete(external, recursive: true);
        }
    }

    [Fact]
    public async Task StateStorePersistsStructuredRuntimeLifecycleHistory()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();

        await store.AppendHistoryAsync(
            "runtime-lifecycle",
            "endpoint-health: Model A",
            new { action = "endpoint-health", sessionId = "session-a", current = "Unreachable" },
            TestContext.Current.CancellationToken);

        var item = Assert.Single(await store.ListHistoryAsync(
            "runtime-lifecycle",
            cancellationToken: TestContext.Current.CancellationToken));
        Assert.Equal("endpoint-health: Model A", item.Message);
        Assert.Contains("session-a", item.DataJson, StringComparison.Ordinal);
        Assert.Contains("Unreachable", item.DataJson, StringComparison.Ordinal);
    }

}
