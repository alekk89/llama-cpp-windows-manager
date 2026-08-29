using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeSessionCommandsTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task ModelRuntimeLoadApplicationServiceOwnsSelectedAndOverviewLoadComposition()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord("model", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, CreateRuntimeExecutable(root), "{}", DateTimeOffset.UtcNow);
        var settings = AppSettings.CreateDefault(root) with { Port = 8084 };
        var service = new ModelRuntimeLoadApplicationService(
            new ModelRuntimeCommandDecisionService(),
            new LaunchRuntimeSelectionService());
        var calls = new List<string>();

        var missingSelection = await service.LoadSelectedAsync(
            new SelectedModelRuntimeLoadApplicationRequest(
                null,
                Restart: false,
                ModelLoaded: false,
                ModelActive: false,
                LaunchSettingsLoaded: false,
                SelectedRuntimeId: "",
                FallbackRuntime: null),
            Actions());
        var renderedAndStarted = await service.LoadSelectedAsync(
            new SelectedModelRuntimeLoadApplicationRequest(
                model,
                Restart: false,
                ModelLoaded: false,
                ModelActive: false,
                LaunchSettingsLoaded: false,
                SelectedRuntimeId: runtime.Id,
                FallbackRuntime: null),
            Actions());
        var restarted = await service.LoadSelectedAsync(
            new SelectedModelRuntimeLoadApplicationRequest(
                model,
                Restart: true,
                ModelLoaded: true,
                ModelActive: false,
                LaunchSettingsLoaded: true,
                SelectedRuntimeId: "",
                FallbackRuntime: runtime),
            Actions());
        var overviewSwitched = await service.LoadOverviewAsync(
            new OverviewModelRuntimeLoadApplicationRequest(
                model,
                ModelLoaded: true,
                ModelActive: false,
                AppReady: true,
                SelectedProfileLoaded: true),
            Actions());
        var overviewProfileReplacement = await service.LoadOverviewAsync(
            new OverviewModelRuntimeLoadApplicationRequest(
                model,
                ModelLoaded: true,
                ModelActive: true,
                AppReady: true,
                SelectedProfileLoaded: false),
            Actions());
        var missingOverviewRuntime = await service.LoadOverviewAsync(
            new OverviewModelRuntimeLoadApplicationRequest(
                model,
                ModelLoaded: false,
                ModelActive: false,
                AppReady: true,
                SelectedProfileLoaded: false),
            Actions(listedRuntimes: [runtime], draft: ModelLaunchSettings.FromAppSettings(settings, "missing-runtime")));

        Assert.Equal(ModelRuntimeLoadApplicationOutcome.Status, missingSelection);
        Assert.Equal(ModelRuntimeLoadApplicationOutcome.RenderedLaunchSettings, renderedAndStarted);
        Assert.Equal(ModelRuntimeLoadApplicationOutcome.Started, restarted);
        Assert.Equal(ModelRuntimeLoadApplicationOutcome.SwitchedLoaded, overviewSwitched);
        Assert.Equal(ModelRuntimeLoadApplicationOutcome.Started, overviewProfileReplacement);
        Assert.Equal(ModelRuntimeLoadApplicationOutcome.MissingRuntime, missingOverviewRuntime);
        Assert.Equal([
            "busy:Preparing model load...",
            "status:Select a model first.",
            "busy:Preparing model load...",
            "render",
            "read",
            "list",
            "start:runtime:model:8084",
            "busy:Preparing restart...",
            "read",
            "list",
            "stop:model",
            "start:runtime:model:8084",
            "busy:Preparing model load...",
            "switch:model",
            "busy:Preparing model load...",
            "draft:model",
            "list",
            "stop:model",
            "read",
            "start:runtime:model:8084",
            "busy:Preparing model load...",
            "draft:model",
            "list",
            "status:Saved runtime 'missing-runtime' is missing. Choose another runtime and save the model profile."
        ], calls);

        ModelRuntimeLoadApplicationActions Actions(
            IReadOnlyList<RuntimeRecord>? listedRuntimes = null,
            ModelLaunchSettings? draft = null)
            => new(
                async (message, action) =>
                {
                    calls.Add($"busy:{message}");
                    await action();
                },
                loadedModel =>
                {
                    calls.Add($"switch:{loadedModel.Id}");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("render");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("read");
                    return settings;
                },
                () =>
                {
                    calls.Add("list");
                    return Task.FromResult<IReadOnlyList<RuntimeRecord>>(listedRuntimes ?? [runtime]);
                },
                draftModel =>
                {
                    calls.Add($"draft:{draftModel.Id}");
                    return Task.FromResult(draft ?? ModelLaunchSettings.FromAppSettings(settings, runtime.Id));
                },
                stoppedModel =>
                {
                    calls.Add($"stop:{stoppedModel.Id}");
                    return Task.CompletedTask;
                },
                (selectedRuntime, startedModel, launchSettings) =>
                {
                    calls.Add($"start:{selectedRuntime.Id}:{startedModel.Id}:{launchSettings.Port}");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"));
    }


    [Fact]
    public async Task ModelRuntimeUnloadApplicationServiceOwnsOverviewUnloadComposition()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord("model", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var service = new ModelRuntimeUnloadApplicationService(new ModelRuntimeCommandDecisionService());
        var calls = new List<string>();

        var missingOverview = await service.UnloadOverviewAsync(
            new ModelRuntimeUnloadApplicationRequest(null, ModelLoaded: false),
            Actions());
        var overviewStopped = await service.UnloadOverviewAsync(
            new ModelRuntimeUnloadApplicationRequest(model, ModelLoaded: true),
            Actions());

        Assert.Equal(ModelRuntimeUnloadApplicationOutcome.Status, missingOverview);
        Assert.Equal(ModelRuntimeUnloadApplicationOutcome.Stopped, overviewStopped);
        Assert.Equal([
            "status:Choose the loading or loaded model to unload it.",
            "stop:model"
        ], calls);

        ModelRuntimeUnloadApplicationActions Actions()
            => new(
                stoppedModel =>
                {
                    calls.Add($"stop:{stoppedModel.Id}");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"));
    }


    [Fact]
    public async Task RuntimeSessionCommandServiceOwnsStopAndSwitchCommandBoundary()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var coordinator = new RuntimeSessionCoordinator(manager, Path.Combine(root, "logs"));
        var service = new RuntimeSessionCommandService(coordinator, new RuntimeSessionActionDecisionService());
        manager.AttachExisting(runtime, modelA, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", "session-a", DateTimeOffset.UtcNow);
        manager.AttachExisting(runtime, modelB, settings with { Port = 8083 }, "b.log", LlamaRuntimeState.Loaded, "", "session-b", DateTimeOffset.UtcNow);

        var stopPlan = service.PlanStopModel(modelA, modelIsSelected: false, modelIsLoading: true);
        var switched = service.SwitchToModel(modelB);
        var stopped = await service.StopModelAsync(modelB.Id);

        Assert.Equal(modelA.Id, stopPlan.ReadinessMonitorModelId);
        Assert.True(stopPlan.StopLoadingStatus);
        Assert.False(stopPlan.ResetMetricCounters);
        Assert.True(switched.Decision.Selected);
        Assert.Equal(8083, switched.ActiveSettings?.Port);
        Assert.Equal(modelB.Id, stopped.StoppedSession?.ModelId);
        Assert.True(manager.IsModelLoaded(modelA.Id));
        Assert.False(manager.IsModelLoaded(modelB.Id));
    }


    [Fact]
    public async Task RuntimeSessionApplicationServiceAppliesStopAndSwitchInOrder()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8099 };
        var calls = new List<string>();

        RuntimeStopApplicationActions StopActions()
            => new(
                id => calls.Add($"monitor:{id}"),
                () => calls.Add("stop-loading"),
                () => calls.Add("reset-metrics"),
                _ => calls.Add("reset-lifetime"),
                _ => calls.Add("reset-idle"),
                active => calls.Add($"active:{active?.Port}"),
                () => { calls.Add("save"); return Task.CompletedTask; },
                () => { calls.Add("overview"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}"));

        await RuntimeSessionApplicationService.ApplyStopAsync(
            new RuntimeStopApplicationRequest(
                new RuntimeStopDecision("model-a", StopLoadingStatus: true, ResetMetricCounters: true, StatusMessage: "stopped"),
                StoppedSession: null,
                ResetMetricCountersBeforeStop: false,
                StopAsync: () =>
                {
                    calls.Add("stop-command");
                    return Task.FromResult(new RuntimeSessionStopResult(null, settings));
                }),
            StopActions());

        Assert.Equal(
            ["monitor:model-a", "stop-loading", "reset-lifetime", "reset-idle", "stop-command", "active:8099", "save", "reset-metrics", "overview", "metrics", "actions", "status:stopped"],
            calls);

        calls.Clear();
        await RuntimeSessionApplicationService.ApplyStopAsync(
            new RuntimeStopApplicationRequest(
                new RuntimeStopDecision("model-b", StopLoadingStatus: false, ResetMetricCounters: true, StatusMessage: "unloaded"),
                StoppedSession: null,
                ResetMetricCountersBeforeStop: true,
                StopAsync: () =>
                {
                    calls.Add("stop-command");
                    return Task.FromResult(new RuntimeSessionStopResult(null, settings));
                }),
            StopActions());

        Assert.Equal(
            ["monitor:model-b", "reset-metrics", "reset-lifetime", "reset-idle", "stop-command", "active:8099", "save", "overview", "metrics", "actions", "status:unloaded"],
            calls);

        calls.Clear();
        await RuntimeSessionApplicationService.ApplySwitchAsync(
            new RuntimeSwitchCommandResult(new RuntimeSwitchDecision(Selected: false, ResetMetricCounters: false, StartDashboardRefresh: false, StatusMessage: "missing"), null),
            new RuntimeSwitchApplicationActions(
                active => calls.Add($"active:{active?.Port}"),
                () => calls.Add("reset-metrics"),
                () => { calls.Add("save"); return Task.CompletedTask; },
                () => calls.Add("dashboard"),
                () => { calls.Add("selector"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}")));

        Assert.Equal(["status:missing"], calls);

        calls.Clear();
        await RuntimeSessionApplicationService.ApplySwitchAsync(
            new RuntimeSwitchCommandResult(new RuntimeSwitchDecision(Selected: true, ResetMetricCounters: true, StartDashboardRefresh: true, StatusMessage: "selected"), settings),
            new RuntimeSwitchApplicationActions(
                active => calls.Add($"active:{active?.Port}"),
                () => calls.Add("reset-metrics"),
                () => { calls.Add("save"); return Task.CompletedTask; },
                () => calls.Add("dashboard"),
                () => { calls.Add("selector"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}")));

        Assert.Equal(["active:8099", "reset-metrics", "save", "dashboard", "selector", "metrics", "actions", "status:selected"], calls);
    }


    [Fact]
    public async Task RuntimeSessionApplicationServiceOwnsStopAndSwitchComposition()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var modelA = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var modelB = new ModelRecord("model-b", "Model B", Path.Combine(root, "b.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        using var manager = CreateLoadedModelSessionManager();
        var coordinator = new RuntimeSessionCoordinator(manager, Path.Combine(root, "logs"));
        var service = new RuntimeSessionApplicationService(
            new RuntimeSessionCommandService(coordinator, new RuntimeSessionActionDecisionService()));
        manager.AttachExisting(runtime, modelA, settings with { Port = 8081 }, "a.log", LlamaRuntimeState.Loaded, "", "session-a", DateTimeOffset.UtcNow);
        manager.AttachExisting(runtime, modelB, settings with { Port = 8083 }, "b.log", LlamaRuntimeState.Loaded, "", "session-b", DateTimeOffset.UtcNow);
        var calls = new List<string>();
        RuntimeStopApplicationActions StopActions()
            => new(
                id => calls.Add($"monitor:{id}"),
                () => calls.Add("stop-loading"),
                () => calls.Add("reset-metrics"),
                _ => calls.Add("reset-lifetime"),
                _ => calls.Add("reset-idle"),
                active => calls.Add($"active:{active?.Port}"),
                () => { calls.Add("save"); return Task.CompletedTask; },
                () => { calls.Add("overview"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}"));

        await service.SwitchToModelAsync(
            modelB,
            new RuntimeSwitchApplicationActions(
                active => calls.Add($"active:{active?.Port}"),
                () => calls.Add("reset-metrics"),
                () => { calls.Add("save"); return Task.CompletedTask; },
                () => calls.Add("dashboard"),
                () => { calls.Add("selector"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}")));
        await service.StopModelAsync(
            new RuntimeSessionStopModelApplicationRequest(
                modelB,
                manager.SessionForModel(modelB.Id),
                ModelIsActive: true,
                ModelIsLoading: false),
            StopActions());

        Assert.Contains("status:Selected loaded model Model B.", calls);
        Assert.Contains("status:Unloaded Model B.", calls);
        Assert.False(manager.IsModelLoaded(modelB.Id));
        Assert.True(manager.IsModelLoaded(modelA.Id));
    }


}
