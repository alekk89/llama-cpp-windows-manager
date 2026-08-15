using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task<bool> StartModelRuntimeAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings launchSettings,
        bool interactivePrompts = true,
        string launchProfileId = "",
        string launchProfileName = "",
        bool selectLoadedOverviewModel = true)
    {
        var result = await _coreServices.Models.ModelRuntimeLaunchApplication.LaunchAsync(
            new ModelRuntimeLaunchApplicationRequest(
                runtime,
                model,
                launchSettings,
                interactivePrompts,
                _settings.AutoLoadGatewayEnabled,
                _settings.AutoLoadGatewayPort,
                launchProfileId,
                launchProfileName),
            new ModelRuntimeLaunchApplicationActions(
                async (settings, _) => await EnsureModelApiKeyAsync(settings),
                async (settings, token) => await _coreServices.Runtime.RuntimeEndpointProbe.IsRespondingAsync(settings, token),
                async (plan, _) => await ConfirmRuntimeLaunchAdmissionAsync(plan),
                async token => await _coreServices.App.GpuStatus.MemoryAsync(token),
                (loadingModel, settings) => StartModelLoadingTimer(loadingModel.Id, loadingModel.Name, settings),
                () => StopModelLoadingTimer(),
                settings => _activeRuntimeSettings = settings,
                SaveActiveRuntimeSessionsAsync,
                (loadingModel, settings) => StartRuntimeReadinessMonitor(loadingModel, settings, selectLoadedOverviewModel),
                StartRuntimeDashboardRefreshTimer,
                UpdateModelLoadingStatus,
                RefreshOverviewAsync,
                RefreshOverviewModelSelectorAsync,
                Task.Delay,
                RefreshRuntimeMetricsAsync,
                () => _llama.State,
                () => _viewModel.CurrentPage == "Overview",
                StopRuntimeDashboardRefreshTimer,
                UpdateOverviewModelActions,
                SetStatus));
        if (result is { Launched: true, Session: { } session })
            await RecordRuntimeLifecycleAsync(
                "started",
                session.SessionId,
                session.ModelId,
                session.ModelName,
                new
                {
                    session.RuntimeId,
                    session.RuntimeName,
                    session.LaunchProfileId,
                    session.LaunchProfileName,
                    session.ProcessId,
                    port = session.LaunchSettings.Port
                });
        return result.Launched;
    }

    private void StartModelLoadingTimer(string modelId, string modelName, AppSettings launchSettings)
    {
        StopModelLoadingTimer();
        _coreServices.Models.ModelRuntimeStatus.StartLoading(
            modelId,
            modelName,
            RuntimeEndpointService.EndpointDisplay(launchSettings),
            DateTimeOffset.Now,
            UpdateModelLoadingStatus);
    }

    private void UpdateModelLoadingStatus()
    {
        var status = _coreServices.Models.ModelRuntimeStatus.LoadingStatusFor(SelectedOverviewModel()?.Id, DateTimeOffset.Now);
        ApplyModelRuntimeStatusRenderPlan(_coreServices.Models.ModelRuntimeStatusRender.LoadingTick(status));
    }

    private void RefreshModelStatusMetric(string fallbackModelStatus)
    {
        var status = _coreServices.Models.ModelRuntimeStatus.StatusFor(SelectedOverviewModel()?.Id, fallbackModelStatus, DateTimeOffset.Now);
        ApplyModelRuntimeStatusRenderPlan(_coreServices.Models.ModelRuntimeStatusRender.DashboardRefresh(status, _coreServices.Models.ModelRuntimeStatus.HasLoadedStatusTimer));
    }

    private void StopModelLoadingTimer(bool showLoadedDuration = false, string loadedModelName = "")
    {
        var loadedStatus = _coreServices.Models.ModelRuntimeStatus.StopLoading(showLoadedDuration, loadedModelName, DateTimeOffset.Now);
        if (loadedStatus is not null)
            ShowModelLoadedStatus();
    }

    private void ShowModelLoadedStatus()
    {
        var selectedStatus = _coreServices.Models.ModelRuntimeStatus.LoadedStatusFor(SelectedOverviewModel()?.Id);
        ApplyModelRuntimeStatusRenderPlan(_coreServices.Models.ModelRuntimeStatusRender.LoadedStatus(selectedStatus));
    }

    private void StopModelLoadedStatusTimer(bool clearLoadedStatus = true)
    {
        _coreServices.Models.ModelRuntimeStatus.StopLoadedStatusTimer(clearLoadedStatus);
    }

    private void ApplyModelRuntimeStatusRenderPlan(ModelRuntimeStatusRenderPlan plan)
    {
        if (!plan.ShouldRender) return;

        MetricCardFactory.SetMetricText(_runtimeDashboardPage.ModelMetric, plan.MetricText);
        if (plan.UpdateProgress)
            UpdateRuntimeModelProgress();
        if (!string.IsNullOrWhiteSpace(plan.StatusText))
            SetStatus(plan.StatusText);
    }

    private void StartRuntimeReadinessMonitor(ModelRecord model, AppSettings launchSettings, bool selectLoadedOverviewModel = true)
    {
        var cts = _coreServices.Ui.RuntimeReadinessMonitors.Start(model.Id);
        RunBackground(
            () => _coreServices.Runtime.RuntimeReadinessMonitorApplication.RunAsync(
                new RuntimeReadinessMonitorApplicationRequest(
                    model.Id,
                    model.Name,
                    launchSettings,
                    _coreServices.Models.ModelRuntimeStatus.IsLoadingModel(model.Id),
                    _viewModel.CurrentPage == "Overview",
                    cts),
                RuntimeReadinessMonitorActions(model.Id, model.Name, selectLoadedOverviewModel)),
            "Runtime readiness monitor failed");
    }

    private void StopRuntimeReadinessMonitor()
    {
        _coreServices.Ui.RuntimeReadinessMonitors.StopAll();
    }

    private void StopRuntimeReadinessMonitor(string modelId)
    {
        _coreServices.Ui.RuntimeReadinessMonitors.Stop(modelId);
    }

    private RuntimeReadinessMonitorApplicationActions RuntimeReadinessMonitorActions(
        string modelId,
        string modelName,
        bool selectLoadedOverviewModel = true)
        => new(
            id => _sessions.SessionForModel(id),
            (settings, token) => _coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync(settings, token),
            id => _sessions.MarkModelLoadedIfRunning(id),
            new RuntimeReadinessCompletionActions(
                showLoadedDuration => StopModelLoadingTimer(showLoadedDuration, modelName),
                () => selectLoadedOverviewModel ? SelectOverviewLoadedModelAsync(modelId) : Task.CompletedTask,
                SaveActiveRuntimeSessionsAsync,
                SetStatus,
                UpdateRuntimeModelProgress,
                UpdateOverviewModelActions,
                RefreshRuntimeMetricsAsync),
            (modelId, source) => _coreServices.Ui.RuntimeReadinessMonitors.Complete(modelId, source));

    private async Task StopLoadedRuntimeAsync()
    {
        var selectedSession = _sessions.SelectedSnapshot();
        await _coreServices.Runtime.RuntimeSessionApplication.StopSelectedAsync(
            new RuntimeSessionStopSelectedApplicationRequest(
                selectedSession,
                _coreServices.Models.ModelRuntimeStatus.IsLoadingModel(selectedSession?.ModelId ?? "")),
            RuntimeStopActions());
        if (selectedSession is not null)
            await RecordRuntimeLifecycleAsync("unloaded", selectedSession.SessionId, selectedSession.ModelId, selectedSession.ModelName);
    }

    private async Task StopModelRuntimeAsync(ModelRecord model)
    {
        var stoppedSession = _sessions.SessionForModel(model.Id);
        await _coreServices.Runtime.RuntimeSessionApplication.StopModelAsync(
            new RuntimeSessionStopModelApplicationRequest(
                model,
                stoppedSession,
                IsModelActive(model),
                _coreServices.Models.ModelRuntimeStatus.IsLoadingModel(model.Id)),
            RuntimeStopActions());
        if (stoppedSession is not null)
            await RecordRuntimeLifecycleAsync("unloaded", stoppedSession.SessionId, stoppedSession.ModelId, stoppedSession.ModelName);
    }

    private async Task SwitchToLoadedModelAsync(ModelRecord model)
    {
        await _coreServices.Runtime.RuntimeSessionApplication.SwitchToModelAsync(
            model,
            new RuntimeSwitchApplicationActions(
                settings => _activeRuntimeSettings = settings,
                ResetMetricCounters,
                SaveActiveRuntimeSessionsAsync,
                StartRuntimeDashboardRefreshTimer,
                RefreshOverviewModelSelectorAsync,
                RefreshRuntimeMetricsAsync,
                UpdateOverviewModelActions,
                SetStatus));
    }

    private RuntimeStopApplicationActions RuntimeStopActions()
        => new(
                StopRuntimeReadinessMonitor,
                () => StopModelLoadingTimer(),
                ResetMetricCounters,
                ResetLifetimeCounters,
                ResetIdleCounters,
                settings => _activeRuntimeSettings = settings,
                SaveActiveRuntimeSessionsAsync,
                RefreshOverviewAsync,
                RefreshRuntimeMetricsAsync,
                UpdateOverviewModelActions,
                SetStatus);

    private async Task RecordRuntimeLifecycleAsync(
        string action,
        string sessionId,
        string modelId,
        string modelName,
        object? details = null)
    {
        try
        {
            await AppServices.StateStore.AppendHistoryAsync(
                "runtime-lifecycle",
                $"{action}: {modelName}",
                new { action, sessionId, modelId, modelName, details });
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not record runtime lifecycle event: {ex.Message}");
        }
    }
}
