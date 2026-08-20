using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task GatewayModelLoadWorkflowStopsConflictingSessionsFixesGatewayPortAndWaitsForReady()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var runtime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var target = new ModelRecord("target", "Target Model", Path.Combine(root, "target.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var loaded = new ModelRecord("loaded", "Loaded Model", Path.Combine(root, "loaded.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profiled = new ModelRecord("profiled", "Profiled Model", Path.Combine(root, "profiled.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(target);
        await store.UpsertModelAsync(loaded);
        await store.UpsertModelAsync(profiled);
        await store.SaveModelLaunchSettingsAsync(target.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8082 }, runtime.Id));
        await store.SaveModelLaunchSettingsAsync(profiled.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8081 }, runtime.Id));
        await store.SaveModelLaunchSettingsAsync(loaded.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8083 }, runtime.Id));
        using var sessions = CreateLoadedModelSessionManager();
        sessions.AttachExisting(runtime, loaded, settings with { Port = 8083 }, Path.Combine(root, "loaded.log"), LlamaRuntimeState.Loaded, "", "loaded-session", DateTimeOffset.UtcNow);
        var runtimeSessions = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var profiles = new ModelLaunchProfileService(store, sessions);
        var workflow = new GatewayModelLoadWorkflowService(store, profiles, runtimeSessions);
        var targetProfile = await profiles.EnsureDefaultAsync(target, settings);
        var phases = new List<string>();
        var stopped = new List<string>();
        AppSettings? startedSettings = null;

        var result = await workflow.EnsureLoadedAsync(new GatewayModelLoadWorkflowRequest(
            target,
            targetProfile,
            ModelGatewaySwapPolicy.SingleActive,
            settings,
            async (model, _) =>
            {
                stopped.Add(model.Id);
                await runtimeSessions.StopModelAsync(model.Id);
            },
            (startedRuntime, model, _, launchSettings, _) =>
            {
                startedSettings = launchSettings;
                sessions.AttachExisting(startedRuntime, model, launchSettings, Path.Combine(root, "target.log"), LlamaRuntimeState.Loading, "", "target-session", DateTimeOffset.UtcNow);
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(true),
            (model, _, _) =>
            {
                sessions.MarkModelLoadedIfRunning(model.Id);
                return Task.FromResult(sessions.SessionForModel(model.Id));
            },
            phases.Add,
            ReadyTimeout: TimeSpan.FromSeconds(1),
            PollInterval: TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        var savedTargetProfile = await store.GetModelLaunchSettingsAsync(target.Id);
        Assert.Equal([loaded.Id], stopped);
        Assert.Equal(8084, savedTargetProfile?.Port);
        Assert.Equal(8084, startedSettings?.Port);
        Assert.Equal(target.Id, result.Session.ModelId);
        Assert.Equal(LoadedModelSessionStatus.Running, result.Session.Status);
        Assert.Contains(phases, phase => phase.Contains("freeing VRAM", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("preparing", phases);
        Assert.Contains("starting", phases);
        Assert.Contains("waiting for API from", phases);
    }

    [Fact]
    public async Task GatewayModelLoadWorkflowRestartsSameModelForRequestedProfile()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082,
            Port = 8084
        };
        var runtime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("qwen", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var defaultProfile = new NamedModelLaunchProfile(
            "default:qwen", model.Id, "Default", ModelLaunchSettings.FromAppSettings(settings with { Port = 8084 }, runtime.Id), DateTimeOffset.UtcNow, true);
        var tunedProfile = new NamedModelLaunchProfile(
            "profile-qwen-128k", model.Id, "128K", ModelLaunchSettings.FromAppSettings(settings with { Port = 8085, ContextSize = 131072 }, runtime.Id), DateTimeOffset.UtcNow, false);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(model);
        await store.SaveNamedModelLaunchProfileAsync(defaultProfile);
        await store.SaveNamedModelLaunchProfileAsync(tunedProfile);
        using var sessions = CreateLoadedModelSessionManager();
        sessions.AttachExisting(
            runtime, model, defaultProfile.Settings.ApplyTo(settings), Path.Combine(root, "default.log"),
            LlamaRuntimeState.Loaded, "", "default-session", DateTimeOffset.UtcNow,
            launchProfileId: defaultProfile.Id, launchProfileName: defaultProfile.Name);
        var runtimeSessions = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var workflow = new GatewayModelLoadWorkflowService(store, new ModelLaunchProfileService(store, sessions), runtimeSessions);
        var stopped = 0;
        NamedModelLaunchProfile? startedProfile = null;
        var phases = new List<string>();

        var result = await workflow.EnsureLoadedAsync(new GatewayModelLoadWorkflowRequest(
            model,
            tunedProfile,
            ModelGatewaySwapPolicy.KeepLoaded,
            settings,
            async (_, _) =>
            {
                stopped++;
                await runtimeSessions.StopModelAsync(model.Id);
            },
            (startedRuntime, startedModel, profile, launchSettings, _) =>
            {
                startedProfile = profile;
                sessions.AttachExisting(
                    startedRuntime, startedModel, launchSettings, Path.Combine(root, "tuned.log"),
                    LlamaRuntimeState.Loading, "", "tuned-session", DateTimeOffset.UtcNow,
                    launchProfileId: profile.Id, launchProfileName: profile.Name);
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(true),
            (readyModel, _, _) =>
            {
                sessions.MarkModelLoadedIfRunning(readyModel.Id);
                return Task.FromResult(sessions.SessionForModel(readyModel.Id));
            },
            phases.Add,
            ReadyTimeout: TimeSpan.FromSeconds(1),
            PollInterval: TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, stopped);
        Assert.Equal(tunedProfile.Id, startedProfile?.Id);
        Assert.Equal(tunedProfile.Id, result.Session.LaunchProfileId);
        Assert.Equal(8085, result.LaunchSettings.Port);
        Assert.Contains(phases, phase => phase.Contains("switching from Default to 128K", StringComparison.Ordinal));
    }


    [Fact]
    public async Task GatewayRuntimeApplicationServiceOwnsActivityRefreshAndErrorBoundary()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8084,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var runtime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("target", "Target Model", Path.Combine(root, "target.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(model);
        await store.SaveModelLaunchSettingsAsync(model.Id, ModelLaunchSettings.FromAppSettings(settings, runtime.Id));
        using var sessions = CreateLoadedModelSessionManager();
        var runtimeSessions = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var application = new GatewayRuntimeApplicationService(new GatewayModelLoadWorkflowService(
            store,
            new ModelLaunchProfileService(store, sessions),
            runtimeSessions));
        var profile = await new ModelLaunchProfileService(store, sessions).EnsureDefaultAsync(model, settings);
        var calls = new List<string>();

        var result = await application.EnsureModelLoadedAsync(
            new GatewayRuntimeLoadApplicationRequest(
                model,
                profile,
                ModelGatewaySwapPolicy.KeepLoaded,
                settings,
                ExistingSession: null),
            new GatewayRuntimeLoadApplicationActions(
                (_, _) => throw new InvalidOperationException("Keep-loaded policy should not stop models."),
                (startedRuntime, runtimeModel, _, launchSettings, _) =>
                {
                    calls.Add($"start:{runtimeModel.Id}:{launchSettings.Port}");
                    sessions.AttachExisting(startedRuntime, runtimeModel, launchSettings, Path.Combine(root, "target.log"), LlamaRuntimeState.Loading, "", "target-session", DateTimeOffset.UtcNow);
                    return Task.CompletedTask;
                },
                (_, _) => Task.FromResult(true),
                (runtimeModel, _, _) =>
                {
                    calls.Add($"ready:{runtimeModel.Id}");
                    sessions.MarkModelLoadedIfRunning(runtimeModel.Id);
                    return Task.FromResult(sessions.SessionForModel(runtimeModel.Id));
                },
                (runtimeModel, phase) => calls.Add($"activity:{phase}:{runtimeModel.Id}"),
                phase => calls.Add($"phase:{phase}"),
                () => calls.Add("complete"),
                message => calls.Add($"fail:{message}"),
                () => { calls.Add("overview"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}")),
            TestContext.Current.CancellationToken);

        Assert.Equal(model.Id, result.ModelId);
        Assert.Contains($"activity:switching to:{model.Id}", calls);
        Assert.Contains("status:Gateway auto-loading Target Model with profile Default...", calls);
        Assert.Contains($"start:{model.Id}:8084", calls);
        Assert.Contains($"ready:{model.Id}", calls);
        Assert.Contains("status:Gateway loaded Target Model at http://127.0.0.1:8084/v1.", calls);
        Assert.Contains("complete", calls);
        Assert.Contains("overview", calls);
        Assert.Contains("metrics", calls);
        Assert.Contains("actions", calls);
        Assert.DoesNotContain(calls, call => call.StartsWith("fail:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelGatewayLifecycleApplicationServiceOwnsRestartAndFailureCleanup()
    {
        var root = CreateTempRoot();
        var service = new ModelGatewayLifecycleApplicationService();
        var apiKey = new string('a', 40);
        var settings = AppSettings.CreateDefault(root) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8099,
            ModelApiKey = apiKey
        };
        var existing = new FakeModelGatewayHost();
        var created = new List<FakeModelGatewayHost>();
        var calls = new List<string>();
        IModelGatewayHost? currentGateway = existing;

        var result = await service.RestartAsync(
            new ModelGatewayLifecycleRestartRequest(currentGateway, settings),
            Actions(
                gateway => currentGateway = gateway,
                _ => Task.FromResult(settings),
                (_, _) =>
                {
                    var gateway = new FakeModelGatewayHost();
                    created.Add(gateway);
                    return gateway;
                },
                calls),
            TestContext.Current.CancellationToken);

        Assert.True(existing.Disposed);
        var started = Assert.Single(created);
        Assert.True(started.Started);
        Assert.Same(started, currentGateway);
        Assert.True(result.GatewayStarted);
        Assert.Contains("Auto-load gateway listening at http://127.0.0.1:8099/v1.", calls);
        Assert.Contains("status", calls);

        var disabled = settings with { AutoLoadGatewayEnabled = false };
        calls.Clear();
        result = await service.RestartAsync(
            new ModelGatewayLifecycleRestartRequest(currentGateway, disabled),
            Actions(
                gateway => currentGateway = gateway,
                _ => throw new InvalidOperationException("Disabled gateway should not require an API key."),
                (_, _) => throw new InvalidOperationException("Disabled gateway should not create a host."),
                calls),
            TestContext.Current.CancellationToken);

        Assert.True(started.Disposed);
        Assert.Null(currentGateway);
        Assert.False(result.GatewayStarted);
        Assert.DoesNotContain("key", calls);
        Assert.DoesNotContain(calls, call => call.StartsWith("create:", StringComparison.Ordinal));
        Assert.Contains("status", calls);

        var stopOnlyGateway = new FakeModelGatewayHost();
        currentGateway = stopOnlyGateway;
        calls.Clear();
        var stopped = await service.StopAsync(
            new ModelGatewayLifecycleStopRequest(currentGateway),
            new ModelGatewayLifecycleStopActions(
                gateway =>
                {
                    calls.Add(gateway is null ? "gateway:null" : "gateway:set");
                    currentGateway = gateway;
                },
                () => calls.Add("status")));

        Assert.True(stopped);
        Assert.True(stopOnlyGateway.Disposed);
        Assert.Null(currentGateway);
        Assert.Equal(["gateway:null", "status"], calls);

        var failed = new FakeModelGatewayHost(new InvalidOperationException("port busy"));
        calls.Clear();
        var failureResult = await service.RestartAsync(
            new ModelGatewayLifecycleRestartRequest(null, settings),
            Actions(
                gateway => currentGateway = gateway,
                _ => Task.FromResult(settings),
                (_, _) => failed,
                calls),
            TestContext.Current.CancellationToken);

        Assert.False(failureResult.GatewayStarted);
        Assert.True(failed.Disposed);
        Assert.Null(currentGateway);
        Assert.Contains("status", calls);
        Assert.Contains(calls, call => call.Contains("port busy"));

        ModelGatewayLifecycleActions Actions(
            Action<IModelGatewayHost?> setGateway,
            Func<AppSettings, Task<AppSettings>> ensureApiKey,
            Func<ModelGatewayOptions, IModelGatewayRuntimeController, IModelGatewayHost> createGateway,
            List<string> callLog)
            => new(
                gateway =>
                {
                    callLog.Add(gateway is null ? "gateway:null" : "gateway:set");
                    setGateway(gateway);
                },
                settings =>
                {
                    callLog.Add("key");
                    return ensureApiKey(settings);
                },
                () =>
                {
                    callLog.Add("controller");
                    return new FakeModelGatewayRuntimeController();
                },
                (options, controller) =>
                {
                    callLog.Add($"create:{options.Port}:{options.SwapPolicy}");
                    return createGateway(options, controller);
                },
                () => callLog.Add("status"),
                callLog.Add);
    }


}
