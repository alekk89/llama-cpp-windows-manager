using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
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
    public async Task ModelRuntimeUnloadApplicationServiceOwnsSelectedAndOverviewUnloadComposition()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord("model", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var service = new ModelRuntimeUnloadApplicationService(new ModelRuntimeCommandDecisionService());
        var calls = new List<string>();

        var missingSelected = await service.UnloadSelectedAsync(
            new ModelRuntimeUnloadApplicationRequest(null, ModelLoaded: false),
            Actions());
        var missingOverview = await service.UnloadOverviewAsync(
            new ModelRuntimeUnloadApplicationRequest(null, ModelLoaded: false),
            Actions());
        var selectedStopped = await service.UnloadSelectedAsync(
            new ModelRuntimeUnloadApplicationRequest(model, ModelLoaded: true),
            Actions());
        var overviewStopped = await service.UnloadOverviewAsync(
            new ModelRuntimeUnloadApplicationRequest(model, ModelLoaded: true),
            Actions());

        Assert.Equal(ModelRuntimeUnloadApplicationOutcome.Status, missingSelected);
        Assert.Equal(ModelRuntimeUnloadApplicationOutcome.Status, missingOverview);
        Assert.Equal(ModelRuntimeUnloadApplicationOutcome.Stopped, selectedStopped);
        Assert.Equal(ModelRuntimeUnloadApplicationOutcome.Stopped, overviewStopped);
        Assert.Equal([
            "status:Select the loading or loaded model to unload it.",
            "status:Choose the loading or loaded model to unload it.",
            "stop:model",
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
    public async Task RuntimeSessionFollowupApplicationServiceAppliesStopAndSwitchInOrder()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8099 };
        var service = new RuntimeSessionFollowupApplicationService();
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

        await service.ApplyStopAsync(
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
        await service.ApplyStopAsync(
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
        await service.ApplySwitchAsync(
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
        await service.ApplySwitchAsync(
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
            new RuntimeSessionCommandService(coordinator, new RuntimeSessionActionDecisionService()),
            new RuntimeSessionFollowupApplicationService());
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


    [Fact]
    public void ModelRuntimeStatusTrackerOwnsTransientLoadingAndLoadedText()
    {
        var source = ReadMainWindowSources();
        var lifecycle = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.ModelRuntimeLifecycle.cs"));
        var tracker = new ModelRuntimeStatusTracker();
        var renderService = new ModelRuntimeStatusRenderService();
        var startedAt = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);

        var fallback = tracker.StatusFor(null, "No model", startedAt);
        tracker.StartLoading("model-1", "Qwen", "http://127.0.0.1:8083", startedAt);
        var loading = tracker.StatusFor("MODEL-1", "No model", startedAt.AddSeconds(5));
        var otherModel = tracker.StatusFor("other", "Other model", startedAt.AddSeconds(5));
        var loadingModelMatches = tracker.IsLoadingModel("MODEL-1");
        var loaded = tracker.StopLoading(showLoadedDuration: true, loadedModelName: "", startedAt.AddSeconds(5));
        var loadedVisible = tracker.StatusFor(null, "No model", startedAt.AddSeconds(6));
        var redundantStop = tracker.StopLoading(showLoadedDuration: false, loadedModelName: "", startedAt.AddSeconds(7));
        var loadedAfterRedundantStop = tracker.StatusFor(null, "No model", startedAt.AddSeconds(7));
        tracker.ClearLoadedStatus();
        var cleared = tracker.StatusFor(null, "No model", startedAt.AddSeconds(6));
        var loadingPlan = renderService.LoadingTick(loading);
        var fallbackPlan = renderService.DashboardRefresh(fallback, hasLoadedStatusTimer: false);
        var loadedPlan = renderService.LoadedStatus(loaded);
        var loadedTimerPlan = renderService.DashboardRefresh(loadedVisible, hasLoadedStatusTimer: true);
        var nonePlan = renderService.LoadingTick(null);

        Assert.Equal(ModelRuntimeStatusKind.Fallback, fallback.Kind);
        Assert.Equal("No model", fallback.MetricText);
        Assert.True(loadingModelMatches);
        Assert.False(tracker.IsLoadingModel("model-1"));
        Assert.Equal(ModelRuntimeStatusKind.Loading, loading.Kind);
        Assert.Equal("Loading Model: Qwen\nLoading Time: 5s", loading.MetricText);
        Assert.Equal("Loading Qwen at http://127.0.0.1:8083.", loading.StatusText);
        Assert.Equal(ModelRuntimeStatusKind.Fallback, otherModel.Kind);
        Assert.NotNull(loaded);
        Assert.Equal(ModelRuntimeStatusKind.Loaded, loaded.Kind);
        Assert.Equal("Loaded Model: Qwen\nLoading Time: 5s", loaded.MetricText);
        Assert.Equal(ModelRuntimeStatusKind.Loaded, loadedVisible.Kind);
        Assert.Null(redundantStop);
        Assert.Equal("Loaded Model: Qwen\nLoading Time: 5s", loadedAfterRedundantStop.MetricText);
        Assert.Equal(ModelRuntimeStatusKind.Fallback, cleared.Kind);
        Assert.True(loadingPlan.ShouldRender);
        Assert.True(loadingPlan.UpdateProgress);
        Assert.Equal("Loading Qwen at http://127.0.0.1:8083.", loadingPlan.StatusText);
        Assert.True(fallbackPlan.ShouldRender);
        Assert.False(fallbackPlan.UpdateProgress);
        Assert.Equal("", fallbackPlan.StatusText);
        Assert.True(loadedPlan.UpdateProgress);
        Assert.False(loadedTimerPlan.UpdateProgress);
        Assert.False(nonePlan.ShouldRender);
        Assert.Contains("_coreServices.Models.ModelRuntimeStatus.StartLoading", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeStatus.IsLoadingModel", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeStatusRender.LoadingTick", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeStatusRender.DashboardRefresh", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ApplyModelRuntimeStatusRenderPlan", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelRuntimeStatusKind.Loading", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelRuntimeStatusKind.Loaded", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelLoadingModelId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelLoadedStatusText", source, StringComparison.Ordinal);
    }


    [Fact]
    public async Task ModelRuntimeStatusControllerOwnsStatusTimers()
    {
        var source = ReadMainWindowSources();
        var lifecycle = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.ModelRuntimeLifecycle.cs"));
        var state = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.State.cs"));
        var controllerSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeStatusController.cs"));
        var factorySource = ReadAppServiceFactorySources();
        var timerFactory = new ManualUiTimerFactory();
        var controller = new ModelRuntimeStatusController(new ModelRuntimeStatusTracker(), timerFactory);
        var startedAt = new DateTimeOffset(2026, 5, 28, 12, 0, 0, TimeSpan.Zero);
        var loadingTicks = 0;
        var loadedExpired = 0;

        controller.StartLoading(
            "model-1",
            "Qwen",
            "http://127.0.0.1:8083",
            startedAt,
            () => loadingTicks++);

        Assert.Equal(1, loadingTicks);
        Assert.Single(timerFactory.Timers);
        Assert.True(timerFactory.Timers[0].Started);
        Assert.True(controller.IsLoadingModel("MODEL-1"));

        timerFactory.Timers[0].Fire();
        Assert.Equal(2, loadingTicks);

        var loaded = controller.StopLoading(showLoadedDuration: true, loadedModelName: "", startedAt.AddSeconds(4));
        Assert.NotNull(loaded);
        Assert.False(timerFactory.Timers[0].Started);
        Assert.False(controller.IsLoadingModel("model-1"));
        Assert.Equal("Loaded Model: Qwen\nLoading Time: 4s", loaded.MetricText);

        controller.StartLoadedStatusTimer(() =>
        {
            loadedExpired++;
            return Task.CompletedTask;
        });

        Assert.True(controller.HasLoadedStatusTimer);
        Assert.Equal(2, timerFactory.Timers.Count);
        Assert.True(timerFactory.Timers[1].Started);

        await timerFactory.Timers[1].FireAsync();
        Assert.Equal(1, loadedExpired);
        controller.StopLoadedStatusTimer(clearLoadedStatus: false);
        Assert.False(controller.HasLoadedStatusTimer);
        Assert.False(timerFactory.Timers[1].Started);
        Assert.Equal("Loaded Model: Qwen\nLoading Time: 4s", controller.StatusFor("model-1", "No model", startedAt.AddSeconds(5)).MetricText);
        Assert.Null(controller.StopLoading(showLoadedDuration: false, loadedModelName: "", startedAt.AddSeconds(6)));
        Assert.Equal("Loaded Model: Qwen\nLoading Time: 4s", controller.StatusFor("model-1", "No model", startedAt.AddSeconds(6)).MetricText);
        controller.StopLoadedStatusTimer();
        Assert.Equal("No model", controller.StatusFor("model-1", "No model", startedAt.AddSeconds(5)).MetricText);

        Assert.Contains("DispatcherUiTimerFactory", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherUiTimerFactory", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new ModelRuntimeStatusController()", state, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeStatus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Models.ModelRuntimeStatus.StartLoadedStatusTimer", lifecycle, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeStatus.StopLoadedStatusTimer(clearLoadedStatus)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelLoadingTimer", state + lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelLoadedStatusTimer", state + lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("new System.Windows.Threading.DispatcherTimer", lifecycle, StringComparison.Ordinal);
    }


    [Fact]
    public void ModelRuntimeStartFollowupServiceOwnsPostLaunchAndFailurePlans()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.ModelRuntimeLifecycle.cs"));
        var serviceSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeStartFollowupService.cs"));
        var applicationSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeStartFollowupApplicationService.cs"));
        var launchApplicationSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeLaunchApplicationService.cs"));
        var service = new ModelRuntimeStartFollowupService();

        var started = service.AfterSessionStarted();
        var loadedOffOverview = service.AfterInitialMetrics("Qwen", LlamaRuntimeState.Loading, isOverviewPage: false);
        var failedOnOverview = service.AfterInitialMetrics("Qwen", LlamaRuntimeState.Failed, isOverviewPage: true);

        Assert.True(started.SaveActiveRuntimeSessions);
        Assert.True(started.StartReadinessMonitor);
        Assert.True(started.StartRuntimeDashboardRefresh);
        Assert.True(started.RefreshOverview);
        Assert.True(started.RefreshOverviewModelSelector);
        Assert.Equal(TimeSpan.FromMilliseconds(750), started.InitialMetricsDelay);
        Assert.True(started.RefreshRuntimeMetrics);
        Assert.True(loadedOffOverview.StopRuntimeDashboardRefresh);
        Assert.True(loadedOffOverview.UpdateActionButtons);
        Assert.False(loadedOffOverview.StopLoadingTimer);
        Assert.False(loadedOffOverview.SaveActiveRuntimeSessions);
        Assert.True(loadedOffOverview.UpdateLoadingStatus);
        Assert.False(failedOnOverview.StopRuntimeDashboardRefresh);
        Assert.True(failedOnOverview.StopLoadingTimer);
        Assert.True(failedOnOverview.SaveActiveRuntimeSessions);
        Assert.False(failedOnOverview.UpdateLoadingStatus);
        Assert.Contains("Failed to load Qwen", failedOnOverview.StatusMessage, StringComparison.Ordinal);
        Assert.Contains("_followup.AfterSessionStarted()", launchApplicationSource, StringComparison.Ordinal);
        Assert.Contains("_followup.AfterInitialMetrics(", launchApplicationSource, StringComparison.Ordinal);
        Assert.Contains("_followupApplication.ApplyAfterSessionStartedAsync(", launchApplicationSource, StringComparison.Ordinal);
        Assert.Contains("_followupApplication.ApplyAfterInitialMetricsAsync(", launchApplicationSource, StringComparison.Ordinal);
        Assert.Contains("new ModelRuntimeStartSessionActions(", launchApplicationSource, StringComparison.Ordinal);
        Assert.Contains("new ModelRuntimeStartInitialMetricsActions(", launchApplicationSource, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeLaunchApplication.LaunchAsync(", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class ModelRuntimeStartFollowupApplicationService", applicationSource, StringComparison.Ordinal);
        Assert.Contains("if (plan.SaveActiveRuntimeSessions)", applicationSource, StringComparison.Ordinal);
        Assert.Contains("if (plan.StopRuntimeDashboardRefresh)", applicationSource, StringComparison.Ordinal);
        Assert.Contains("LlamaRuntimeState.Failed", serviceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("if (_llama.State == LlamaRuntimeState.Failed)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (startPlan.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (initialMetricsPlan.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Delay(750)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelRuntimeStartFollowup", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelRuntimeStartFollowupApplication", source, StringComparison.Ordinal);
    }


    [Fact]
    public async Task ModelRuntimeStartFollowupApplicationServiceAppliesPlansInOrder()
    {
        var planner = new ModelRuntimeStartFollowupService();
        var service = new ModelRuntimeStartFollowupApplicationService();
        var calls = new List<string>();

        await service.ApplyAfterSessionStartedAsync(
            planner.AfterSessionStarted(),
            new ModelRuntimeStartSessionActions(
                () => { calls.Add("save"); return Task.CompletedTask; },
                () => calls.Add("readiness"),
                () => calls.Add("dashboard"),
                () => calls.Add("loading"),
                () => { calls.Add("overview"); return Task.CompletedTask; },
                () => { calls.Add("selector"); return Task.CompletedTask; },
                delay => { calls.Add($"delay:{delay.TotalMilliseconds}"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; }));

        Assert.Equal(["save", "readiness", "dashboard", "loading", "overview", "selector", "delay:750", "metrics"], calls);

        calls.Clear();
        await service.ApplyAfterInitialMetricsAsync(
            new ModelRuntimeStartInitialMetricsPlan(
                StopRuntimeDashboardRefresh: true,
                UpdateActionButtons: true,
                StopLoadingTimer: true,
                SaveActiveRuntimeSessions: true,
                UpdateLoadingStatus: true,
                StatusMessage: "done"),
            new ModelRuntimeStartInitialMetricsActions(
                () => calls.Add("stop-dashboard"),
                () => calls.Add("actions"),
                () => calls.Add("stop-loading"),
                () => { calls.Add("save"); return Task.CompletedTask; },
                status => calls.Add($"status:{status}"),
                () => calls.Add("loading")));

        Assert.Equal(["stop-dashboard", "actions", "stop-loading", "save", "status:done", "loading"], calls);
    }


    [Fact]
    public async Task ModelRuntimeLaunchApplicationServiceOwnsPreparationStartAndFollowupBoundary()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(modelPath, new byte[1024 * 1024]);
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8084,
            ModelApiKey = new string('d', 32),
            GpuLayers = AppSettings.DefaultGpuLayers
        };
        var model = new ModelRecord("model", "Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var loadedModel = model with { Id = "loaded", Name = "Loaded" };
        var runtime = new RuntimeRecord("runtime-cuda", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, CreateRuntimeExecutable(root), "{}", DateTimeOffset.UtcNow);
        using var sessions = CreateLoadedModelSessionManager();
        sessions.AttachExisting(runtime, loadedModel, settings with { Port = 8081 }, "loaded.log", LlamaRuntimeState.Loaded, "", "loaded-session", DateTimeOffset.UtcNow);
        var coordinator = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var launchApplication = new ModelRuntimeLaunchApplicationService(
            new ModelRuntimeLaunchPreparationService(
                coordinator,
                new RuntimeLaunchPrerequisiteService(
                    new RuntimeToolPrerequisiteService(
                        _ => Task.FromResult(ReadyWslReport()),
                        () => WindowsBuildTools(),
                        new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", ""))),
                    (_, _) => Task.FromResult(false)),
                new RuntimeLaunchAdmissionService(new VramAdmissionService()),
                new GpuStatusProbeService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", "")))),
            new RuntimeSessionCommandService(coordinator, new RuntimeSessionActionDecisionService()),
            new ModelRuntimeStartFollowupService(),
            new ModelRuntimeStartFollowupApplicationService());
        var calls = new List<string>();

        var result = await launchApplication.LaunchAsync(
            new ModelRuntimeLaunchApplicationRequest(
                runtime,
                model,
                settings,
                InteractivePrompts: true,
                AutoLoadGatewayEnabled: true,
                AutoLoadGatewayPort: 8082),
            new ModelRuntimeLaunchApplicationActions(
                (launchSettings, _) =>
                {
                    calls.Add("ensure-key");
                    return Task.FromResult(launchSettings);
                },
                (_, _) => Task.FromResult(false),
                (_, _) =>
                {
                    calls.Add("confirm");
                    return Task.FromResult(false);
                },
                _ => Task.FromResult<VramMemorySnapshot?>(new VramMemorySnapshot(1.0, 24.0)),
                (_, _) => calls.Add("start-loading"),
                () => calls.Add("stop-loading"),
                _ => calls.Add("active"),
                () => { calls.Add("save"); return Task.CompletedTask; },
                (_, _) => calls.Add("readiness"),
                () => calls.Add("dashboard"),
                () => calls.Add("loading"),
                () => { calls.Add("overview"); return Task.CompletedTask; },
                () => { calls.Add("selector"); return Task.CompletedTask; },
                _ => { calls.Add("delay"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => LlamaRuntimeState.Loading,
                () => true,
                () => calls.Add("stop-dashboard"),
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}")),
            TestContext.Current.CancellationToken);

        Assert.False(result.Launched);
        Assert.Null(result.Session);
        Assert.Equal(["ensure-key", "confirm"], calls);
    }


    [Fact]
    public void MainWindowDelegatesRuntimeSessionMutationsToCoordinator()
    {
        var source = ReadMainWindowSources();
        var preparation = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeLaunchPreparationService.cs"));
        var commands = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeSessionCommandService.cs"));
        var sessionApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeSessionApplicationService.cs"));
        var launchApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeLaunchApplicationService.cs"));

        Assert.Contains("_runtimeSessions.EnsureLaunchPortAvailable", preparation, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeLaunchApplication.LaunchAsync", source, StringComparison.Ordinal);
        Assert.Contains("_preparation.PrepareAsync", launchApplication, StringComparison.Ordinal);
        Assert.Contains("_commands.StartModelAsync", launchApplication, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeSessionApplication.StopModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeSessionApplication.SwitchToModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("_followupApplication.ApplyStopAsync(", sessionApplication, StringComparison.Ordinal);
        Assert.Contains("_followupApplication.ApplySwitchAsync(", sessionApplication, StringComparison.Ordinal);
        Assert.Contains("ResetMetricCountersBeforeStop: false", sessionApplication, StringComparison.Ordinal);
        Assert.Contains("ResetMetricCountersBeforeStop: true", sessionApplication, StringComparison.Ordinal);
        Assert.Contains("_runtimeSessions.StartAsync", commands, StringComparison.Ordinal);
        Assert.Contains("_runtimeSessions.StopModelAsync", commands, StringComparison.Ordinal);
        Assert.Contains("_runtimeSessions.SelectModel", commands, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeSessions.StopAllAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sessions.StartAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sessions.StopSelectedAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sessions.StopModelAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sessions.StopAllAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sessions.SelectModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_sessions.SelectSession", source, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeDashboardAppliesIdleUnloadPolicyToAllLoadedSessions()
    {
        var dashboard = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeDashboard.cs"));
        var counters = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeMetricCounters.cs"));
        var refreshApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDashboardRefreshApplicationService.cs"));
        var service = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeIdleUnloadPolicyService.cs"));
        var state = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.State.cs"));
        var telemetry = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeTelemetryApplicationService.cs"));

        Assert.Contains("await actions.ApplyIdleUnloadPoliciesAsync(pollResults)", refreshApplication, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeTelemetryApplication.ApplyIdleUnloadPoliciesAsync(", counters, StringComparison.Ordinal);
        Assert.Contains("_idleUnloadPolicy.ApplyAsync(", telemetry, StringComparison.Ordinal);
        Assert.Contains("_tracker.RetainRuntimeKeys(pollResults.Select(result => result.RuntimeKey))", service, StringComparison.Ordinal);
        Assert.Contains("_tracker.Observe(result.RuntimeKey", service, StringComparison.Ordinal);
        Assert.Contains("RuntimeIdleUnloadActions()", counters, StringComparison.Ordinal);
        Assert.Contains("await actions.StopModelRuntimeAsync(model)", telemetry, StringComparison.Ordinal);
        Assert.Contains("AutoUnloadStatus(model, idleMinutes)", telemetry, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeTelemetryApplication", counters, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeDashboardRefreshApplication", dashboard, StringComparison.Ordinal);
        Assert.DoesNotContain("Auto-unloading {model.Name}", counters, StringComparison.Ordinal);
        Assert.DoesNotContain("StopLoadedRuntimeAsync()", counters, StringComparison.Ordinal);
        Assert.DoesNotContain("_llama.ActiveModelId", counters, StringComparison.Ordinal);
        Assert.DoesNotContain("_idleUnloadTracker", counters, StringComparison.Ordinal);
        Assert.DoesNotContain("_autoUnloadInProgress", state, StringComparison.Ordinal);
    }


}
