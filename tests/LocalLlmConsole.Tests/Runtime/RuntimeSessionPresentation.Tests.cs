using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeSessionPresentationTests : ManagerRegressionTestBase
{
    [Fact]
    public void ModelRuntimeStatusTrackerOwnsTransientLoadingAndLoadedText()
    {
        var source = ReadMainWindowSources();
        var lifecycle = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.ModelRuntimeLifecycle.cs"));
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
        Assert.Equal("Loading Qwen at http://127.0.0.1:8083.", loadingPlan.StatusText);
        Assert.True(fallbackPlan.ShouldRender);
        Assert.Equal("", fallbackPlan.StatusText);
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
        var lifecycle = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.ModelRuntimeLifecycle.cs"));
        var state = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Core", "MainWindow.State.cs"));
        var controllerSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Telemetry", "ModelRuntimeStatusController.cs"));
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
        var source = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Ui", "Shell", "MainWindow", "Runtimes", "MainWindow.ModelRuntimeLifecycle.cs"));
        var serviceSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Sessions", "ModelRuntimeStartFollowupService.cs"));
        var applicationSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Sessions", "ModelRuntimeStartFollowupApplicationService.cs"));
        var launchApplicationSource = File.ReadAllText(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Services", "Runtimes", "Launch", "ModelRuntimeLaunchApplicationService.cs"));
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


}
