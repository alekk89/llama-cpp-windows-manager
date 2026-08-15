using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
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
                new RuntimeReadinessWorkflowService(),
                new RuntimeReadinessCompletionService()),
            new RuntimeReadinessCompletionApplicationService());
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
                "progress",
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
                    () => calls.Add("progress"),
                    () => calls.Add("actions"),
                    () =>
                    {
                        calls.Add("metrics");
                        return Task.CompletedTask;
                    }),
                (modelId, source) => calls.Add($"complete:{modelId}:{source.IsCancellationRequested}"));
    }


    [Fact]
    public void MainWindowDelegatesRuntimeReadinessPollingToWorkflow()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.ModelRuntimeLifecycle.cs"));
        var workflow = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeReadinessWorkflowService.cs"));
        var completion = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeReadinessCompletionService.cs"));
        var monitor = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeReadinessMonitorWorkflowService.cs"));
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeReadinessCompletionApplicationService.cs"));
        var monitorApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeReadinessMonitorApplicationService.cs"));

        Assert.Contains("_coreServices.Runtime.RuntimeReadinessMonitorApplication.RunAsync(", source, StringComparison.Ordinal);
        Assert.Contains("new RuntimeReadinessMonitorApplicationRequest(", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeReadinessMonitorActions(model.Id, model.Name, selectLoadedOverviewModel)", source, StringComparison.Ordinal);
        Assert.Contains("new RuntimeReadinessCompletionActions(", source, StringComparison.Ordinal);
        Assert.Contains("_workflow.RunAsync(new RuntimeReadinessMonitorWorkflowRequest(", monitorApplication, StringComparison.Ordinal);
        Assert.Contains("_completionApplication.ApplyAsync(result.CompletionPlan", monitorApplication, StringComparison.Ordinal);
        Assert.Contains("catch (OperationCanceledException)", monitorApplication, StringComparison.Ordinal);
        Assert.Contains("actions.CompleteMonitor(request.ModelId, request.CancellationSource)", monitorApplication, StringComparison.Ordinal);
        Assert.Contains("_readiness.WaitUntilReadyAsync(new RuntimeReadinessWorkflowRequest(", monitor, StringComparison.Ordinal);
        Assert.Contains("_completion.Build(new RuntimeReadinessCompletionRequest(", monitor, StringComparison.Ordinal);
        Assert.Contains("public sealed class RuntimeReadinessCompletionApplicationService", application, StringComparison.Ordinal);
        Assert.Contains("if (plan.StopLoadingStatus)", application, StringComparison.Ordinal);
        Assert.Contains("RuntimeReadinessStatus.NoLongerLoading", completion, StringComparison.Ordinal);
        Assert.Contains("RuntimeReadinessStatus.Loaded", completion, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(request.PollInterval ?? DefaultPollInterval, cancellationToken)", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeReadinessWorkflow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly RuntimeReadinessCompletionService _runtimeReadinessCompletion", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeReadinessCompletion.Build(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("if (plan.StopLoadingStatus)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("while (!cancellationToken.IsCancellationRequested)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("result.Status == RuntimeReadinessStatus", source, StringComparison.Ordinal);
        Assert.DoesNotContain("catch (OperationCanceledException)", source, StringComparison.Ordinal);
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
        Assert.True(loaded.UpdateRuntimeProgress);
        Assert.True(loaded.UpdateActionButtons);
        Assert.True(loaded.RefreshRuntimeMetrics);
        Assert.Contains("Loaded Qwen at http://127.0.0.1:8084/v1.", loaded.StatusMessage, StringComparison.Ordinal);
        Assert.True(alreadyStopped.StopLoadingStatus);
        Assert.False(alreadyStopped.SaveActiveRuntimeSessions);
        Assert.False(alreadyStopped.RefreshRuntimeMetrics);
        Assert.False(changed.StopLoadingStatus);
        Assert.False(changed.UpdateActionButtons);
        Assert.Equal("", changed.StatusMessage);
    }


    [Fact]
    public async Task RuntimeReadinessCompletionApplicationServiceAppliesPlanInOrder()
    {
        var service = new RuntimeReadinessCompletionApplicationService();
        var calls = new List<string>();

        await service.ApplyAsync(
            new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: true,
                ShowLoadedDuration: true,
                SelectLoadedOverviewModel: true,
                SaveActiveRuntimeSessions: true,
                UpdateRuntimeProgress: true,
                UpdateActionButtons: true,
                RefreshRuntimeMetrics: true,
                StatusMessage: "loaded"),
            new RuntimeReadinessCompletionActions(
                showLoaded => calls.Add($"stop-loading:{showLoaded}"),
                () => { calls.Add("select"); return Task.CompletedTask; },
                () => { calls.Add("save"); return Task.CompletedTask; },
                status => calls.Add($"status:{status}"),
                () => calls.Add("progress"),
                () => calls.Add("actions"),
                () => { calls.Add("metrics"); return Task.CompletedTask; }));

        Assert.Equal(["stop-loading:True", "select", "save", "status:loaded", "progress", "actions", "metrics"], calls);

        calls.Clear();
        await service.ApplyAsync(
            new RuntimeReadinessCompletionPlan(
                StopLoadingStatus: false,
                ShowLoadedDuration: false,
                SelectLoadedOverviewModel: false,
                SaveActiveRuntimeSessions: false,
                UpdateRuntimeProgress: false,
                UpdateActionButtons: false,
                RefreshRuntimeMetrics: false,
                StatusMessage: ""),
            new RuntimeReadinessCompletionActions(
                showLoaded => calls.Add($"stop-loading:{showLoaded}"),
                () => { calls.Add("select"); return Task.CompletedTask; },
                () => { calls.Add("save"); return Task.CompletedTask; },
                status => calls.Add($"status:{status}"),
                () => calls.Add("progress"),
                () => calls.Add("actions"),
                () => { calls.Add("metrics"); return Task.CompletedTask; }));

        Assert.Empty(calls);
    }


    [Fact]
    public void RuntimeReadinessMonitorRegistryReplacesCompletesAndStopsTokens()
    {
        var source = ReadMainWindowSources();
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeReadinessMonitorApplicationService.cs"));
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
        Assert.Contains("_coreServices.Ui.RuntimeReadinessMonitors.Start(model.Id)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.RuntimeReadinessMonitors.Complete", source, StringComparison.Ordinal);
        Assert.Contains("actions.CompleteMonitor(request.ModelId, request.CancellationSource)", application, StringComparison.Ordinal);
        Assert.DoesNotContain("Dictionary<string, CancellationTokenSource> _coreServices.Ui.RuntimeReadinessMonitors", source, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeSessionActionDecisionServiceOwnsStopAndSwitchRules()
    {
        var source = ReadMainWindowSources();
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeSessionApplicationService.cs"));
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

        Assert.Equal(model.Id, stopSelected.ReadinessMonitorModelId);
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
        Assert.Contains("_commands.PlanStopSelected(", application, StringComparison.Ordinal);
        Assert.Contains("_commands.PlanStopModel(", application, StringComparison.Ordinal);
        Assert.Contains("_commands.SwitchToModel(", application, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeSessionApplication.StopSelectedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeSessionApplication.StopModelAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeSessionApplication.SwitchToModelAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeSessionActions.", source, StringComparison.Ordinal);
    }


    [Fact]
    public void ModelRuntimeCommandDecisionServiceOwnsLoadAndUnloadGates()
    {
        var source = ReadMainWindowSources();
        var serviceSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeCommandDecisionService.cs"));
        var loadApplicationSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeLoadApplicationService.cs"));
        var unloadApplicationSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeUnloadApplicationService.cs"));
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
        var selectedUnloadMissing = service.PlanSelectedUnload(null, modelLoaded: false);
        var overviewUnloadMissing = service.PlanOverviewUnload(null, modelLoaded: false);
        var unloadLoaded = service.PlanSelectedUnload(model, modelLoaded: true);

        Assert.Equal(ModelRuntimeLoadCommandKind.Status, missingSelected.Kind);
        Assert.Equal("Select a model first.", missingSelected.StatusMessage);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, activeSelected.Kind);
        Assert.Equal("Selected model is already active.", activeSelected.StatusMessage);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, restartUnloaded.Kind);
        Assert.Equal("Load the selected model before restarting it.", restartUnloaded.StatusMessage);
        Assert.Equal(ModelRuntimeLoadCommandKind.SwitchLoaded, switchLoaded.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.RenderLaunchSettings, renderSettings.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Continue, continueSelected.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, missingOverview.Kind);
        Assert.Equal("Choose a model first.", missingOverview.StatusMessage);
        Assert.Equal(ModelRuntimeLoadCommandKind.SwitchLoaded, loadedOverview.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Continue, replacementOverview.Kind);
        Assert.Equal(ModelRuntimeLoadCommandKind.Status, startingOverview.Kind);
        Assert.Equal("App is still starting.", startingOverview.StatusMessage);
        Assert.Equal(ModelRuntimeLoadCommandKind.Continue, continueOverview.Kind);
        Assert.Equal(ModelRuntimeUnloadCommandKind.Status, selectedUnloadMissing.Kind);
        Assert.Equal("Select the loading or loaded model to unload it.", selectedUnloadMissing.StatusMessage);
        Assert.Equal(ModelRuntimeUnloadCommandKind.Status, overviewUnloadMissing.Kind);
        Assert.Equal("Choose the loading or loaded model to unload it.", overviewUnloadMissing.StatusMessage);
        Assert.Equal(ModelRuntimeUnloadCommandKind.Stop, unloadLoaded.Kind);
        Assert.Contains("_coreServices.Models.ModelRuntimeLoadApplication.LoadSelectedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeLoadApplication.LoadOverviewAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeUnloadApplication.UnloadSelectedAsync(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelRuntimeUnloadApplication.UnloadOverviewAsync(", source, StringComparison.Ordinal);
        Assert.Contains("ModelRuntimeLoadActions(", source, StringComparison.Ordinal);
        Assert.Contains("ModelRuntimeUnloadActions()", source, StringComparison.Ordinal);
        Assert.Contains("_commands.PlanSelectedLoad(", loadApplicationSource, StringComparison.Ordinal);
        Assert.Contains("_commands.PlanOverviewLoad(", loadApplicationSource, StringComparison.Ordinal);
        Assert.Contains("_commands.PlanSelectedUnload(", unloadApplicationSource, StringComparison.Ordinal);
        Assert.Contains("_commands.PlanOverviewUnload(", unloadApplicationSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class ModelRuntimeCommandDecisionService", serviceSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class ModelRuntimeLoadApplicationService", loadApplicationSource, StringComparison.Ordinal);
        Assert.Contains("public sealed class ModelRuntimeUnloadApplicationService", unloadApplicationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Models.ModelRuntimeCommands.PlanSelectedLoad(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Models.ModelRuntimeCommands.PlanOverviewLoad(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Models.ModelRuntimeCommands.PlanSelectedUnload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Models.ModelRuntimeCommands.PlanOverviewUnload(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Load the selected model before restarting it.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Choose the loading or loaded model to unload it.", source, StringComparison.Ordinal);
    }


}
