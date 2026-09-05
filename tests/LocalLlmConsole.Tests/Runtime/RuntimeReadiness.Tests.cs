using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeReadinessTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task RuntimeReadinessWorkflowWaitsForAliveEndpointThenMarksLoaded()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var loading = RuntimeSession(root, settings, LoadedModelSessionStatus.Loading, isRunning: true);
        var probes = 0;
        var marked = false;
        var service = new RuntimeReadinessWorkflowService();

        var result = await service.WaitUntilReadyAsync(new RuntimeReadinessWorkflowRequest(
            loading.ModelId,
            settings,
            _ => loading,
            (_, _) =>
            {
                probes++;
                return Task.FromResult(probes > 1);
            },
            _ =>
            {
                marked = true;
                return true;
            },
            TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeReadinessStatus.Loaded, result.Status);
        Assert.True(marked);
        Assert.True(probes >= 2);
    }


    [Fact]
    public async Task RuntimeReadinessWorkflowExitsWhenSessionIsNoLongerLoading()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var running = RuntimeSession(root, settings, LoadedModelSessionStatus.Running, isRunning: true);
        var service = new RuntimeReadinessWorkflowService();
        var probes = 0;

        var result = await service.WaitUntilReadyAsync(new RuntimeReadinessWorkflowRequest(
            running.ModelId,
            settings,
            _ => running,
            (_, _) =>
            {
                probes++;
                return Task.FromResult(true);
            },
            _ => true,
            TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeReadinessStatus.NoLongerLoading, result.Status);
        Assert.Equal(0, probes);
    }


    [Fact]
    public async Task RuntimeReadinessWorkflowReportsSessionChangedWhenMarkLoadedFails()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var loading = RuntimeSession(root, settings, LoadedModelSessionStatus.Loading, isRunning: true);
        var service = new RuntimeReadinessWorkflowService();

        var result = await service.WaitUntilReadyAsync(new RuntimeReadinessWorkflowRequest(
            loading.ModelId,
            settings,
            _ => loading,
            (_, _) => Task.FromResult(true),
            _ => false,
            TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeReadinessStatus.SessionChanged, result.Status);
    }

    [Fact]
    public async Task RuntimeReadinessWorkflowStopsBeforeLoadedWhenAuthenticationIsNotEnforced()
    {
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { Port = 8084 };
        var session = RuntimeSession(CreateTempRoot(), settings, LoadedModelSessionStatus.Loading, isRunning: true);
        var markedLoaded = false;

        var result = await new RuntimeReadinessWorkflowService().WaitUntilReadyAsync(
            new RuntimeReadinessWorkflowRequest(
                session.ModelId,
                settings,
                _ => session,
                (_, _) => Task.FromResult(true),
                _ => markedLoaded = true,
                TimeSpan.Zero,
                (_, _) => Task.FromResult(new RuntimeAuthenticationProbeResult(
                    RuntimeAuthenticationProbeStatus.NotEnforced,
                    "Authentication is not enforced."))),
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeReadinessStatus.AuthenticationFailed, result.Status);
        Assert.Contains("not enforced", result.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.False(markedLoaded);

        var plan = new RuntimeReadinessCompletionService().Build(new RuntimeReadinessCompletionRequest(
            result.Status,
            session.ModelName,
            settings,
            ModelIsStillLoading: true,
            IsOverviewPage: true,
            result.Reason));
        Assert.True(plan.StopUnsafeRuntime);
        Assert.True(plan.SaveActiveRuntimeSessions);
        Assert.Contains("Stopped", plan.StatusMessage, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeReadinessMonitorWorkflowCombinesPollingAndCompletionPlan()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8084 };
        var loading = RuntimeSession(root, settings, LoadedModelSessionStatus.Loading, isRunning: true);
        var service = new RuntimeReadinessMonitorWorkflowService(
            new RuntimeReadinessWorkflowService(),
            new RuntimeReadinessCompletionService());

        var result = await service.RunAsync(new RuntimeReadinessMonitorWorkflowRequest(
            loading.ModelId,
            "Qwen",
            settings,
            ModelIsStillLoading: true,
            IsOverviewPage: true,
            _ => loading,
            (_, _) => Task.FromResult(true),
            _ => true,
            TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeReadinessStatus.Loaded, result.Status);
        Assert.True(result.CompletionPlan.StopLoadingStatus);
        Assert.True(result.CompletionPlan.ShowLoadedDuration);
        Assert.True(result.CompletionPlan.SaveActiveRuntimeSessions);
        Assert.True(result.CompletionPlan.RefreshRuntimeMetrics);
        Assert.Contains("Loaded Qwen at http://127.0.0.1:8084/v1.", result.CompletionPlan.StatusMessage, StringComparison.Ordinal);
    }


    [Fact]
    public async Task RuntimeReadinessMonitorApplicationServiceRunsCompletionAndAlwaysCompletes()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8084 };
        var loading = RuntimeSession(root, settings, LoadedModelSessionStatus.Loading, isRunning: true);
        var service = new RuntimeReadinessMonitorApplicationService(
            new RuntimeReadinessMonitorWorkflowService(
                new RuntimeReadinessWorkflowService((interval, token) =>
                {
                    Assert.Equal(TimeSpan.FromSeconds(2), interval);
                    token.ThrowIfCancellationRequested();
                    return Task.CompletedTask;
                }),
                new RuntimeReadinessCompletionService()));
        var calls = new List<string>();

        using var cts = new CancellationTokenSource();
        var completed = await service.RunAsync(
            new RuntimeReadinessMonitorApplicationRequest(
                loading.ModelId,
                "Qwen",
                settings,
                ModelIsStillLoading: true,
                IsOverviewPage: true,
                cts),
            Actions(loading, endpointAlive: true));

        Assert.Equal(RuntimeReadinessMonitorApplicationOutcome.Completed, completed);
        Assert.Equal(
            [
                $"session:{loading.ModelId}",
                "alive:8084",
                $"mark:{loading.ModelId}",
                "stop-loading:True",
                "select",
                "save",
                "status:Loaded Qwen at http://127.0.0.1:8084/v1.",
                "actions",
                "metrics",
                $"complete:{loading.ModelId}:False"
            ],
            calls);

        calls.Clear();
        using var cancelledCts = new CancellationTokenSource();
        cancelledCts.Cancel();

        var cancelled = await service.RunAsync(
            new RuntimeReadinessMonitorApplicationRequest(
                loading.ModelId,
                "Qwen",
                settings,
                ModelIsStillLoading: true,
                IsOverviewPage: true,
                cancelledCts),
            Actions(loading with { IsRunning = true }, endpointAlive: false));

        Assert.Equal(RuntimeReadinessMonitorApplicationOutcome.Cancelled, cancelled);
        Assert.Contains($"complete:{loading.ModelId}:True", calls);
        Assert.DoesNotContain("metrics", calls);

        RuntimeReadinessMonitorApplicationActions Actions(LoadedModelSessionSnapshot session, bool endpointAlive)
            => new(
                modelId =>
                {
                    calls.Add($"session:{modelId}");
                    return session;
                },
                (launchSettings, _) =>
                {
                    calls.Add($"alive:{launchSettings.Port}");
                    return Task.FromResult(endpointAlive);
                },
                (_, _) => Task.FromResult(new RuntimeAuthenticationProbeResult(
                    RuntimeAuthenticationProbeStatus.Verified,
                    "verified")),
                modelId =>
                {
                    calls.Add($"mark:{modelId}");
                    return true;
                },
                new RuntimeReadinessCompletionActions(
                    showLoaded => calls.Add($"stop-loading:{showLoaded}"),
                    () =>
                    {
                        calls.Add("select");
                        return Task.CompletedTask;
                    },
                    () =>
                    {
                        calls.Add("save");
                        return Task.CompletedTask;
                    },
                    status => calls.Add($"status:{status}"),
                    () => calls.Add("actions"),
                    () =>
                    {
                        calls.Add("metrics");
                        return Task.CompletedTask;
                    }),
                (modelId, source) => calls.Add($"complete:{modelId}:{source.IsCancellationRequested}"));
    }




    [Fact]
    public void RuntimeReadinessCompletionServiceOwnsLoadedAndStoppedFollowupRules()
    {
        var root = CreateTempRoot();
        var service = new RuntimeReadinessCompletionService();
        var settings = AppSettings.CreateDefault(root) with { Port = 8084 };

        var loaded = service.Build(new RuntimeReadinessCompletionRequest(
            RuntimeReadinessStatus.Loaded,
            "Qwen",
            settings,
            ModelIsStillLoading: true,
            IsOverviewPage: true));
        var alreadyStopped = service.Build(new RuntimeReadinessCompletionRequest(
            RuntimeReadinessStatus.NoLongerLoading,
            "Qwen",
            settings,
            ModelIsStillLoading: true,
            IsOverviewPage: true));
        var changed = service.Build(new RuntimeReadinessCompletionRequest(
            RuntimeReadinessStatus.SessionChanged,
            "Qwen",
            settings,
            ModelIsStillLoading: true,
            IsOverviewPage: true));

        Assert.True(loaded.StopLoadingStatus);
        Assert.True(loaded.ShowLoadedDuration);
        Assert.True(loaded.SelectLoadedOverviewModel);
        Assert.True(loaded.SaveActiveRuntimeSessions);
        Assert.True(loaded.UpdateActionButtons);
        Assert.True(loaded.RefreshRuntimeMetrics);
        Assert.Contains("Loaded Qwen at http://127.0.0.1:8084/v1.", loaded.StatusMessage, StringComparison.Ordinal);
        Assert.True(alreadyStopped.StopLoadingStatus);
        Assert.True(alreadyStopped.UpdateActionButtons);
        Assert.False(alreadyStopped.SaveActiveRuntimeSessions);
        Assert.False(alreadyStopped.RefreshRuntimeMetrics);
        Assert.False(changed.StopLoadingStatus);
        Assert.False(changed.UpdateActionButtons);
        Assert.Equal("", changed.StatusMessage);
    }


    [Fact]
    public async Task RuntimeReadinessMonitorApplicationServiceAppliesCompletionPlanInOrder()
    {
        var calls = new List<string>();

        await RuntimeReadinessMonitorApplicationService.ApplyCompletionAsync(
            new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: true,
                ShowLoadedDuration: true,
                SelectLoadedOverviewModel: true,
                SaveActiveRuntimeSessions: true,
                UpdateActionButtons: true,
                RefreshRuntimeMetrics: true,
                StatusMessage: "loaded"),
            new RuntimeReadinessCompletionActions(
                showLoaded => calls.Add($"stop-loading:{showLoaded}"),
                () => { calls.Add("select"); return Task.CompletedTask; },
                () => { calls.Add("save"); return Task.CompletedTask; },
                status => calls.Add($"status:{status}"),
                () => calls.Add("actions"),
                () => { calls.Add("metrics"); return Task.CompletedTask; }));

        Assert.Equal(["stop-loading:True", "select", "save", "status:loaded", "actions", "metrics"], calls);

        calls.Clear();
        await RuntimeReadinessMonitorApplicationService.ApplyCompletionAsync(
            new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: false,
                ShowLoadedDuration: false,
                SelectLoadedOverviewModel: false,
                SaveActiveRuntimeSessions: false,
                UpdateActionButtons: false,
                RefreshRuntimeMetrics: false,
                StatusMessage: ""),
            new RuntimeReadinessCompletionActions(
                showLoaded => calls.Add($"stop-loading:{showLoaded}"),
                () => { calls.Add("select"); return Task.CompletedTask; },
                () => { calls.Add("save"); return Task.CompletedTask; },
                status => calls.Add($"status:{status}"),
                () => calls.Add("actions"),
                () => { calls.Add("metrics"); return Task.CompletedTask; }));

        Assert.Empty(calls);

        calls.Clear();
        await RuntimeReadinessMonitorApplicationService.ApplyCompletionAsync(
            new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: true,
                ShowLoadedDuration: false,
                SelectLoadedOverviewModel: false,
                SaveActiveRuntimeSessions: true,
                UpdateActionButtons: true,
                RefreshRuntimeMetrics: false,
                StatusMessage: "stopped",
                StopUnsafeRuntime: true),
            new RuntimeReadinessCompletionActions(
                showLoaded => calls.Add($"stop-loading:{showLoaded}"),
                () => { calls.Add("select"); return Task.CompletedTask; },
                () => { calls.Add("save"); return Task.CompletedTask; },
                status => calls.Add($"status:{status}"),
                () => calls.Add("actions"),
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => { calls.Add("stop-unsafe"); return Task.CompletedTask; }));

        Assert.Equal(["stop-unsafe", "stop-loading:False", "save", "status:stopped", "actions"], calls);
    }


    [Fact]
    public void RuntimeReadinessMonitorRegistryReplacesCompletesAndStopsTokens()
    {
        using var registry = new RuntimeReadinessMonitorRegistry();

        var first = registry.Start("model-1");
        var firstToken = first.Token;
        var second = registry.Start("MODEL-1");
        var third = registry.Start("model-2");
        var thirdToken = third.Token;
        using var stale = new CancellationTokenSource();

        Assert.True(firstToken.IsCancellationRequested);
        Assert.True(registry.Contains("model-1"));
        Assert.True(registry.Contains("MODEL-1"));
        Assert.True(registry.Contains("model-2"));
        Assert.False(registry.Complete("model-1", stale));
        Assert.True(registry.Complete("model-1", second));
        Assert.False(registry.Contains("model-1"));
        registry.StopAll();
        Assert.True(thirdToken.IsCancellationRequested);
        Assert.Equal(0, registry.Count);
    }


    [Fact]
    public void RuntimeSessionActionDecisionServiceOwnsStopAndSwitchRules()
    {
        var service = new RuntimeSessionActionDecisionService();
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { Port = 8083 };
        var runtime = new RuntimeRecord("runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model-1", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var session = new LoadedModelSessionSnapshot(
            "session-1",
            model.Id,
            model.Name,
            runtime.Id,
            runtime.Name,
            runtime.Mode,
            runtime.Backend,
            settings,
            "runtime.log",
            DateTimeOffset.UtcNow,
            "",
            123,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true);

        var stopSelected = service.StopSelected(session, selectedModelIsLoading: true);
        var stopWithoutSelection = service.StopSelected(null, selectedModelIsLoading: false);
        var stopSelectedModel = service.StopModel(model, modelIsSelected: true, modelIsLoading: false);
        var stopBackgroundModel = service.StopModel(model, modelIsSelected: false, modelIsLoading: true);
        var switchMissing = service.SwitchToModel(model, selected: false);
        var switchLoaded = service.SwitchToModel(model, selected: true);

        Assert.Equal(session.SessionId, stopSelected.ReadinessMonitorModelId);
        Assert.True(stopSelected.StopLoadingStatus);
        Assert.True(stopSelected.ResetMetricCounters);
        Assert.Equal("Runtime stopped.", stopSelected.StatusMessage);
        Assert.Equal("", stopWithoutSelection.ReadinessMonitorModelId);
        Assert.True(stopWithoutSelection.StopLoadingStatus);
        Assert.True(stopSelectedModel.ResetMetricCounters);
        Assert.False(stopSelectedModel.StopLoadingStatus);
        Assert.Equal($"Unloaded {model.Name}.", stopSelectedModel.StatusMessage);
        Assert.False(stopBackgroundModel.ResetMetricCounters);
        Assert.True(stopBackgroundModel.StopLoadingStatus);
        Assert.False(switchMissing.Selected);
        Assert.False(switchMissing.ResetMetricCounters);
        Assert.Equal($"{model.Name} is not loaded.", switchMissing.StatusMessage);
        Assert.True(switchLoaded.Selected);
        Assert.False(switchLoaded.ResetMetricCounters);
        Assert.True(switchLoaded.StartDashboardRefresh);
        Assert.Equal($"Selected loaded model {model.Name}.", switchLoaded.StatusMessage);
    }


    [Fact]
    public void ModelRuntimeCommandDecisionServiceOwnsLoadAndUnloadGates()
    {
        var service = new ModelRuntimeCommandDecisionService();
        var model = new ModelRecord("model", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);

        var missingSelected = service.PlanSelectedLoad(null, restart: false, modelLoaded: false, modelActive: false, launchSettingsLoaded: false);
        var activeSelected = service.PlanSelectedLoad(model, restart: false, modelLoaded: true, modelActive: true, launchSettingsLoaded: true);
        var restartUnloaded = service.PlanSelectedLoad(model, restart: true, modelLoaded: false, modelActive: false, launchSettingsLoaded: true);
        var switchLoaded = service.PlanSelectedLoad(model, restart: false, modelLoaded: true, modelActive: false, launchSettingsLoaded: true);
        var renderSettings = service.PlanSelectedLoad(model, restart: false, modelLoaded: false, modelActive: false, launchSettingsLoaded: false);
        var continueSelected = service.PlanSelectedLoad(model, restart: true, modelLoaded: true, modelActive: false, launchSettingsLoaded: true);
        var missingOverview = service.PlanOverviewLoad(null, modelLoaded: false, modelActive: false, appReady: true, selectedProfileLoaded: false);
        var loadedOverview = service.PlanOverviewLoad(model, modelLoaded: true, modelActive: false, appReady: true, selectedProfileLoaded: true);
        var replacementOverview = service.PlanOverviewLoad(model, modelLoaded: true, modelActive: true, appReady: true, selectedProfileLoaded: false);
        var startingOverview = service.PlanOverviewLoad(model, modelLoaded: false, modelActive: false, appReady: false, selectedProfileLoaded: false);
        var continueOverview = service.PlanOverviewLoad(model, modelLoaded: false, modelActive: false, appReady: true, selectedProfileLoaded: false);
        var overviewUnloadMissing = service.PlanOverviewUnload(null, modelLoaded: false);
        var unloadLoaded = service.PlanOverviewUnload(model, modelLoaded: true);

        Assert.Equal(ModelRuntimeLoadCommandKind.Status, missingSelected.Kind);
        Assert.Equal(ModelRuntimeCommandStatus.SelectModelFirst, missingSelected.Status);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, activeSelected.Kind);
        Assert.Equal(ModelRuntimeCommandStatus.ModelAlreadyActive, activeSelected.Status);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, restartUnloaded.Kind);
        Assert.Equal(ModelRuntimeCommandStatus.LoadBeforeRestart, restartUnloaded.Status);
        Assert.Equal(ModelRuntimeLoadCommandKind.SwitchLoaded, switchLoaded.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.RenderLaunchSettings, renderSettings.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Continue, continueSelected.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, missingOverview.Kind);
        Assert.Equal(ModelRuntimeCommandStatus.ChooseModelFirst, missingOverview.Status);
        Assert.Equal(ModelRuntimeLoadCommandKind.SwitchLoaded, loadedOverview.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Continue, replacementOverview.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, startingOverview.Kind);
        Assert.Equal(ModelRuntimeCommandStatus.AppStarting, startingOverview.Status);
        Assert.Equal(ModelRuntimeLoadCommandKind.Continue, continueOverview.Kind);
        Assert.Equal(ModelRuntimeUnloadCommandKind.Status, overviewUnloadMissing.Kind);
        Assert.Equal(ModelRuntimeCommandStatus.ChooseLoadedModelToUnload, overviewUnloadMissing.Status);
        Assert.Equal(ModelRuntimeUnloadCommandKind.Stop, unloadLoaded.Kind);
    }


}
