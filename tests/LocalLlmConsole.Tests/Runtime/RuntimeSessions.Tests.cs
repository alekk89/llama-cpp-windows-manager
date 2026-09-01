using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeSessionsTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ActiveRuntimeSessionStorePersistsMultipleSelectedSessions()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        var settings = AppSettings.CreateDefault(root);
        var first = new ActiveRuntimeSession("model-a", "runtime-a", settings with { Port = 8081 }, "a.log", DateTimeOffset.UtcNow, ProcessId: 11, SessionId: "session-a", IsSelected: false);
        var second = new ActiveRuntimeSession(
            "model-b",
            "runtime-b",
            settings with { Port = 8082 },
            "b.log",
            DateTimeOffset.UtcNow,
            ProcessId: 22,
            SessionId: "session-b",
            IsSelected: true,
            LaunchProfileId: "profile-b",
            LaunchProfileName: "Balanced");

        await store.SaveAllAsync([first, second], TestContext.Current.CancellationToken);
        var all = await store.ReadAllAsync(TestContext.Current.CancellationToken);
        var selected = await store.TryReadAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, all.Count);
        Assert.Equal("session-b", selected?.SessionId);
        Assert.Equal(8082, selected?.LaunchSettings.Port);
        Assert.Equal("profile-b", selected?.LaunchProfileId);
        Assert.Equal("Balanced", selected?.LaunchProfileName);
    }


    [Fact]
    public async Task ActiveRuntimeSessionStoreCancellationPreservesRecoveryFile()
    {
        var root = CreateTempRoot();
        var store = new ActiveRuntimeSessionStore(root);
        var session = new ActiveRuntimeSession(
            "model-a",
            "runtime-a",
            AppSettings.CreateDefault(root),
            "runtime.log",
            DateTimeOffset.UtcNow,
            ProcessId: 11,
            SessionId: "session-a");
        await store.SaveAllAsync([session], TestContext.Current.CancellationToken);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadAllAsync(cancelled.Token));
        Assert.True(File.Exists(store.SessionsPath));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.TryReadAsync(cancelled.Token));
        Assert.True(File.Exists(store.SessionsPath));
    }


    [Fact]
    public void LoadedModelSessionManagerTracksMultipleExposedEndpoints()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();

        manager.AttachExisting(runtime, modelA, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", LoadedModelSessionManager.SessionIdFor(modelA.Id), DateTimeOffset.UtcNow);
        manager.AttachExisting(runtime, modelB, settings with { Port = 8082 }, "b.log", LlamaRuntimeState.Loaded, "", "session-b", DateTimeOffset.UtcNow);
        manager.SelectModel(modelB.Id);
        var sessions = manager.Snapshots();

        Assert.Equal(2, sessions.Count);
        Assert.Contains(sessions, session => session.ModelId == modelA.Id && session.Endpoint.EndsWith(":8081/v1", StringComparison.Ordinal));
        Assert.Contains(sessions, session => session.ModelId == modelB.Id && session.Endpoint.EndsWith(":8082/v1", StringComparison.Ordinal) && session.IsSelected);
        Assert.True(manager.IsModelLoaded(modelA.Id));
        Assert.True(manager.IsModelActive(modelB.Id));
    }

    [Fact]
    public async Task LoadedModelSessionManagerTracksAndStopsSameModelProfilesIndependently()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var firstId = LoadedModelSessionManager.SessionIdFor(model.Id, "profile-a");
        var secondId = LoadedModelSessionManager.SessionIdFor(model.Id, "profile-b");

        manager.AttachExisting(runtime, model, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", firstId, DateTimeOffset.UtcNow,
            launchProfileId: "profile-a", launchProfileName: "GPU 1");
        manager.AttachExisting(runtime, model, settings with { Port = 8082 }, "b.log", LlamaRuntimeState.Loaded, "", secondId, DateTimeOffset.UtcNow,
            launchProfileId: "profile-b", launchProfileName: "GPU 2");

        Assert.NotEqual(firstId, secondId);
        Assert.Equal(2, manager.SessionsForModel(model.Id).Count);
        Assert.Equal(8081, manager.SessionForProfile(model.Id, "profile-a")?.LaunchSettings.Port);
        Assert.Equal(8082, manager.SessionForProfile(model.Id, "profile-b")?.LaunchSettings.Port);

        await manager.StopAsync(firstId, cancellationToken: TestContext.Current.CancellationToken);
        Assert.Null(manager.SessionForProfile(model.Id, "profile-a"));
        Assert.True(manager.SessionForProfile(model.Id, "profile-b")?.IsRunning);

        await manager.StopModelAsync(model.Id);
        Assert.Empty(manager.SessionsForModel(model.Id));
    }

    [Fact]
    public void LoadedModelSessionManagerCachesModelSizeForTheSessionLifetime()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(modelPath, new byte[128]);
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model", "Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();

        var attached = manager.AttachExisting(
            runtime,
            model,
            settings,
            "model.log",
            LlamaRuntimeState.Loaded,
            "",
            "session-model",
            DateTimeOffset.UtcNow);
        File.WriteAllBytes(modelPath, new byte[256]);
        var refreshed = manager.Snapshots().Single();

        Assert.Equal(128, attached.ModelSizeBytes);
        Assert.Equal(attached.ModelSizeBytes, refreshed.ModelSizeBytes);
    }

    [Fact]
    public void LoadedModelSessionManagerUsesSupervisorFactoryForSessionLifecycles()
    {
        var managerSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Sessions", "LoadedModelSessionManager.cs"));
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var created = 0;
        using var manager = new LoadedModelSessionManager(() =>
        {
            created++;
            return new LlamaProcessSupervisor(
                new WslRuntimeStopService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))),
                new NativeRuntimeStopService());
        });

        Assert.Equal(1, created);

        var attached = manager.AttachExisting(
            runtime,
            model,
            settings with { Port = 8081 },
            "a.log",
            LlamaRuntimeState.Loaded,
            "",
            LoadedModelSessionManager.SessionIdFor(model.Id),
            DateTimeOffset.UtcNow);

        Assert.Equal(2, created);
        Assert.Equal(model.Id, attached.ModelId);
        Assert.Equal(attached, manager.SelectedSnapshot());
        Assert.Throws<InvalidOperationException>(() => new LoadedModelSessionManager(() => null!));
        Assert.DoesNotContain("DefaultSupervisorFactory", managerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new WslRuntimeStopService", managerSource, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeSessionPersistenceServiceSavesRunningSessionsAndClearsWhenEmpty()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var wslRuntime = new RuntimeRecord("runtime-wsl", "llama.cpp WSL CUDA", RuntimeMode.Wsl, RuntimeBackend.Cuda, "/opt/llama/llama-server", "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var store = new ActiveRuntimeSessionStore(root);
        var persistence = new RuntimeSessionPersistenceService(store, manager);
        manager.AttachExisting(runtime, modelA, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", "session-a", DateTimeOffset.UtcNow);
        manager.AttachExisting(
            wslRuntime,
            modelB,
            settings with { Port = 8082 },
            "b.log",
            LlamaRuntimeState.Loading,
            "marker-b",
            "session-b",
            DateTimeOffset.UtcNow,
            launchProfileId: "profile-b",
            launchProfileName: "Balanced");
        manager.SelectModel(modelB.Id);

        var saved = await persistence.SaveRunningAsync(TestContext.Current.CancellationToken);
        var stored = await persistence.ReadAllAsync(TestContext.Current.CancellationToken);
        var selected = await store.TryReadAsync(TestContext.Current.CancellationToken);
        await manager.StopAllAsync();
        var cleared = await persistence.SaveRunningAsync(TestContext.Current.CancellationToken);

        Assert.Equal(2, saved.SavedSessionCount);
        Assert.False(saved.Cleared);
        Assert.Equal(2, stored.Count);
        Assert.Equal("session-b", selected?.SessionId);
        Assert.Equal("runtime-wsl", selected?.RuntimeId);
        Assert.Equal("marker-b", selected?.ProcessMarker);
        Assert.Equal("profile-b", selected?.LaunchProfileId);
        Assert.Equal("Balanced", selected?.LaunchProfileName);
        Assert.Equal(0, cleared.SavedSessionCount);
        Assert.True(cleared.Cleared);
        Assert.Empty(await persistence.ReadAllAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task RuntimeSessionRecoveryServiceValidatesAndAttachesRestorableSessions()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var wslRuntime = new RuntimeRecord("runtime-wsl", "llama.cpp WSL CUDA", RuntimeMode.Wsl, RuntimeBackend.Cuda, "/opt/llama/llama-server", "{}", DateTimeOffset.UtcNow);
        var nativeRuntime = new RuntimeRecord("runtime-native", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelC = new ModelRecord("model-c", "Model C", Path.Combine(root, "c.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelD = new ModelRecord("model-d", "Model D", Path.Combine(root, "d.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var persisted = new[]
        {
            new ActiveRuntimeSession(modelA.Id, wslRuntime.Id, settings with { Port = 8081 }, "a.log", DateTimeOffset.UtcNow, "marker-a", SessionId: "session-a", IsSelected: false),
            new ActiveRuntimeSession(modelB.Id, wslRuntime.Id, settings with { Port = 8082 }, "b.log", DateTimeOffset.UtcNow, "marker-b", SessionId: "session-b", IsSelected: true),
            new ActiveRuntimeSession(modelC.Id, wslRuntime.Id, settings with { Port = 8083 }, "c.log", DateTimeOffset.UtcNow, "marker-c", SessionId: "session-c"),
            new ActiveRuntimeSession(modelD.Id, wslRuntime.Id, settings with { Port = 8084 }, "d.log", DateTimeOffset.UtcNow, "marker-d", SessionId: "session-d"),
            new ActiveRuntimeSession(modelA.Id, wslRuntime.Id, settings with { Port = 8085 }, "missing-marker.log", DateTimeOffset.UtcNow, ProcessMarker: "", SessionId: "session-no-marker"),
            new ActiveRuntimeSession(modelA.Id, nativeRuntime.Id, settings with { Port = 8086 }, "native.log", DateTimeOffset.UtcNow, ProcessId: 123, SessionId: "session-native")
        };
        using var manager = CreateLoadedModelSessionManager();
        var recovery = new RuntimeSessionRecoveryService(manager);
        var nativeChecks = 0;

        var result = await recovery.RecoverAsync(new RuntimeSessionRecoveryRequest(
            persisted,
            [modelA, modelB, modelC, modelD],
            [wslRuntime, nativeRuntime],
            (launchSettings, _) => Task.FromResult<IReadOnlyList<string>>(launchSettings.Port switch
            {
                8081 => [modelA.Name],
                8083 => ["not-the-same-model"],
                _ => []
            }),
            (launchSettings, _) => Task.FromResult(launchSettings.Port == 8081),
            (launchSettings, _) => Task.FromResult(launchSettings.Port == 8082),
            (_, _) =>
            {
                nativeChecks++;
                return false;
            }),
            TestContext.Current.CancellationToken);

        var loading = result.AttachedSessions.Single(attachment => attachment.State == LlamaRuntimeState.Loading);
        var loaded = result.AttachedSessions.Single(attachment => attachment.State == LlamaRuntimeState.Loaded);
        Assert.Equal(2, result.AttachedSessions.Count);
        Assert.Equal(4, result.SkippedSessionCount);
        Assert.Equal(modelA.Id, loaded.Model.Id);
        Assert.False(loaded.NeedsReadinessMonitor);
        Assert.Equal(modelB.Id, loading.Model.Id);
        Assert.True(loading.NeedsReadinessMonitor);
        Assert.True(loading.WasSelected);
        Assert.Equal("session-b", manager.SelectedSessionId);
        Assert.Equal(2, manager.Snapshots().Count);
        Assert.Equal(1, nativeChecks);
    }


    [Fact]
    public async Task RuntimeSessionRecoveryApplicationServiceOwnsStartupRecoveryFollowup()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime-wsl", "llama.cpp WSL CUDA", RuntimeMode.Wsl, RuntimeBackend.Cuda, "/opt/llama/llama-server", "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var persisted = new[]
        {
            new ActiveRuntimeSession(modelA.Id, runtime.Id, settings with { Port = 8081 }, "a.log", DateTimeOffset.UtcNow, "marker-a", SessionId: "session-a"),
            new ActiveRuntimeSession(modelB.Id, runtime.Id, settings with { Port = 8082 }, "b.log", DateTimeOffset.UtcNow, "marker-b", SessionId: "session-b", IsSelected: true)
        };
        using var manager = CreateLoadedModelSessionManager();
        var store = new ActiveRuntimeSessionStore(root);
        await store.SaveAllAsync(persisted, TestContext.Current.CancellationToken);
        var persistence = new RuntimeSessionPersistenceService(store, manager);
        var application = new RuntimeSessionRecoveryApplicationService(
            manager,
            persistence,
            new RuntimeSessionRecoveryService(manager));
        var loadingStatuses = new List<string>();
        var readinessMonitors = new List<string>();
        var statuses = new List<string>();
        AppSettings? activeSettings = null;
        var dashboardRefreshes = 0;
        var selectorRefreshes = 0;
        var metricRefreshes = 0;

        var result = await application.RecoverAsync(new RuntimeSessionRecoveryApplicationActions(
            () => Task.FromResult<IReadOnlyList<ModelRecord>>([modelA, modelB]),
            () => Task.FromResult<IReadOnlyList<RuntimeRecord>>([runtime]),
            (_, _) => Task.FromResult<IReadOnlyList<string>>([]),
            (launchSettings, _) => Task.FromResult(launchSettings.Port == 8081),
            (launchSettings, _) => Task.FromResult(launchSettings.Port == 8082),
            (model, launchSettings) => loadingStatuses.Add($"{model.Id}:{launchSettings.Port}"),
            session => readinessMonitors.Add($"{session.ModelId}:{session.LaunchSettings.Port}"),
            launchSettings => activeSettings = launchSettings,
            statuses.Add,
            () => dashboardRefreshes++,
            () =>
            {
                selectorRefreshes++;
                return Task.CompletedTask;
            },
            () =>
            {
                metricRefreshes++;
                return Task.CompletedTask;
            }),
            TestContext.Current.CancellationToken);

        var storedSelected = await store.TryReadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.Attempted);
        Assert.True(result.Recovered);
        Assert.Equal(2, result.AttachedSessionCount);
        Assert.Equal(0, result.SkippedSessionCount);
        Assert.Equal(modelB.Id, result.SelectedSession?.ModelId);
        Assert.Equal(2, manager.Snapshots().Count);
        Assert.Equal(8082, activeSettings?.Port);
        Assert.Equal(["model-b:8082"], loadingStatuses);
        Assert.Equal(["model-b:8082"], readinessMonitors);
        Assert.Single(statuses);
        Assert.Contains("Recovered 2 loaded model session(s). Selected Model B", statuses[0], StringComparison.Ordinal);
        Assert.Equal(1, dashboardRefreshes);
        Assert.Equal(1, selectorRefreshes);
        Assert.Equal(1, metricRefreshes);
        Assert.Equal("session-b", storedSelected?.SessionId);
    }


    [Fact]
    public async Task RuntimeSessionReconcilerMarksReadyLoadingSessionsLoaded()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var reconciler = new RuntimeSessionReconciler();
        manager.AttachExisting(runtime, model, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loading, "", "session-a", DateTimeOffset.UtcNow);

        var result = await reconciler.ReconcileAsync(
            manager,
            _ => Task.FromResult(true),
            _ => Task.FromResult(true));

        var transition = Assert.Single(result.LoadedTransitions);
        Assert.True(result.HasChanges);
        Assert.Equal(0, result.RemovedSessionCount);
        Assert.Equal(model.Id, transition.ModelId);
        Assert.Equal(model.Name, transition.ModelName);
        Assert.Equal(LoadedModelSessionStatus.Running, manager.SessionForModel(model.Id)?.Status);
    }


    [Fact]
    public async Task RuntimeSessionReconciliationApplicationServiceOwnsPersistenceAndUiFollowup()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var store = new ActiveRuntimeSessionStore(root);
        var persistence = new RuntimeSessionPersistenceService(store, manager);
        var application = new RuntimeSessionReconciliationApplicationService(
            manager,
            persistence,
            new RuntimeSessionReconciler());
        manager.AttachExisting(runtime, model, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loading, "", "session-a", DateTimeOffset.UtcNow);
        var calls = new List<string>();
        AppSettings? activeSettings = null;

        var result = await application.ReconcileAsync(new RuntimeSessionReconciliationApplicationActions(
            _ => Task.FromResult(true),
            _ => Task.FromResult(true),
            settings =>
            {
                activeSettings = settings;
                calls.Add($"active:{settings?.Port}");
            },
            transition => calls.Add($"loaded:{transition.ModelId}"),
            () => calls.Add("rows"),
            () => calls.Add("actions")),
            TestContext.Current.CancellationToken);

        var storedSelected = await store.TryReadAsync(TestContext.Current.CancellationToken);
        Assert.True(result.HasChanges);
        Assert.Equal(0, result.RemovedSessionCount);
        Assert.Equal(model.Id, Assert.Single(result.LoadedTransitions).ModelId);
        Assert.Equal(LoadedModelSessionStatus.Running, manager.SessionForModel(model.Id)?.Status);
        Assert.Equal(8081, activeSettings?.Port);
        Assert.Equal(["active:8081", "loaded:model-a", "rows", "actions"], calls);
        Assert.Equal("session-a", storedSelected?.SessionId);
    }


    [Fact]
    public async Task RuntimeSessionReconcilerRemovesUnavailableRecoveredSessions()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var reconciler = new RuntimeSessionReconciler();
        manager.AttachExisting(runtime, model, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", "session-a", DateTimeOffset.UtcNow);

        var result = await reconciler.ReconcileAsync(
            manager,
            _ => Task.FromResult(false),
            _ => throw new InvalidOperationException("No loading sessions should be probed."));

        Assert.True(result.HasChanges);
        Assert.Equal(1, result.RemovedSessionCount);
        Assert.Empty(result.LoadedTransitions);
        Assert.Empty(manager.Snapshots());
    }


    [Fact]
    public void RuntimeSessionCoordinatorValidatesLaunchPorts()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        using var manager = CreateLoadedModelSessionManager(() => now);
        var coordinator = new RuntimeSessionCoordinator(manager, Path.Combine(root, "logs"));
        manager.AttachExisting(runtime, modelA, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", LoadedModelSessionManager.SessionIdFor(modelA.Id), DateTimeOffset.UtcNow);

        coordinator.EnsureLaunchPortAvailable(modelA.Id, settings with { Port = 8081 }, autoLoadGatewayEnabled: false, autoLoadGatewayPort: 8082);
        var portConflict = Assert.Throws<InvalidOperationException>(() =>
            coordinator.EnsureLaunchPortAvailable(modelB.Id, settings with { Port = 8081 }, autoLoadGatewayEnabled: false, autoLoadGatewayPort: 8082));
        var gatewayConflict = Assert.Throws<InvalidOperationException>(() =>
            coordinator.EnsureLaunchPortAvailable(modelB.Id, settings with { Port = 8082 }, autoLoadGatewayEnabled: true, autoLoadGatewayPort: 8082));

        Assert.Contains("already assigned", portConflict.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("reserved for the auto-load gateway", gatewayConflict.Message, StringComparison.OrdinalIgnoreCase);
    }


    [Fact]
    public async Task RuntimeSessionCoordinatorSelectsAndStopsSessions()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var now = new DateTimeOffset(2026, 8, 20, 12, 0, 0, TimeSpan.Zero);
        using var manager = CreateLoadedModelSessionManager(() => now);
        var coordinator = new RuntimeSessionCoordinator(manager, Path.Combine(root, "logs"));
        manager.AttachExisting(runtime, modelA, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", "session-a", DateTimeOffset.UtcNow);
        manager.AttachExisting(runtime, modelB, settings with { Port = 8083 }, "b.log", LlamaRuntimeState.Loaded, "", "session-b", DateTimeOffset.UtcNow);

        var selected = coordinator.SelectModel(modelB.Id);
        var stopped = await coordinator.StopModelAsync(modelB.Id);

        Assert.True(selected.Selected);
        Assert.Equal(8083, selected.ActiveSettings?.Port);
        Assert.Equal(modelB.Id, stopped.StoppedSession?.ModelId);
        Assert.Equal(8081, stopped.ActiveSettings?.Port);
        Assert.False(manager.IsModelLoaded(modelB.Id));
        Assert.True(manager.IsModelLoaded(modelA.Id));
        Assert.Contains(manager.OverviewSnapshots(), session => session.ModelId == modelB.Id && !session.IsRunning);

        now = now.AddSeconds(2.1);

        Assert.DoesNotContain(manager.OverviewSnapshots(), session => session.ModelId == modelB.Id);
    }


    [Fact]
    public async Task LoadedModelSessionManagerRemovesUnavailableRecoveredLoadedSessions()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();

        manager.AttachExisting(runtime, model, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", "session-a", DateTimeOffset.UtcNow);

        var removed = await manager.StopUnavailableRecoveredSessionsAsync(_ => Task.FromResult(false));

        Assert.Equal(1, removed);
        Assert.False(manager.HasRunningSessions);
        Assert.Empty(manager.Snapshots());
    }


    [Fact]
    public async Task LoadedModelSessionManagerRemovesUnavailableRecoveredLoadingSessions()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp WSL", RuntimeMode.Wsl, RuntimeBackend.Cuda, Path.Combine(root, "llama-server"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();

        manager.AttachExisting(runtime, model, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loading, "marker", "session-a", DateTimeOffset.UtcNow);

        var removed = await manager.StopUnavailableRecoveredSessionsAsync(_ => Task.FromResult(false));

        Assert.Equal(1, removed);
        Assert.False(manager.HasRunningSessions);
        Assert.Empty(manager.Snapshots());
    }

    [Fact]
    public void LoadedModelSessionManagerRequiresRepeatedEndpointFailuresAndReportsRecovery()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "llama.cpp CPU", RuntimeMode.Native, RuntimeBackend.Cpu, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var snapshot = manager.AttachExisting(
            runtime,
            model,
            settings,
            "a.log",
            LlamaRuntimeState.Loaded,
            "",
            "session-a",
            DateTimeOffset.UtcNow,
            launchProfileId: "profile-a",
            launchProfileName: "Scientific");

        RuntimeMetricPollResult FailedPoll()
            => new(snapshot, RuntimeMetricPollerService.RuntimeKey(snapshot), [], null, "connection refused", EndpointResponded: false);

        Assert.Empty(manager.ApplyEndpointHealth([FailedPoll()]));
        Assert.Empty(manager.ApplyEndpointHealth([FailedPoll()]));
        var unavailable = Assert.Single(manager.ApplyEndpointHealth([FailedPoll()]));

        var unhealthySnapshot = Assert.Single(manager.Snapshots());
        Assert.Equal(RuntimeEndpointHealth.Unreachable, unavailable.Current);
        Assert.Equal(LoadedModelSessionStatus.Unreachable, unhealthySnapshot.Status);
        Assert.Equal("Scientific", unhealthySnapshot.LaunchProfileName);
        Assert.True(unhealthySnapshot.IsRunning);

        var recovered = Assert.Single(manager.ApplyEndpointHealth([
            new RuntimeMetricPollResult(snapshot, RuntimeMetricPollerService.RuntimeKey(snapshot), [], null, "", EndpointResponded: true)
        ]));
        Assert.Equal(RuntimeEndpointHealth.Healthy, recovered.Current);
        Assert.Equal(LoadedModelSessionStatus.Running, Assert.Single(manager.Snapshots()).Status);
    }


}
