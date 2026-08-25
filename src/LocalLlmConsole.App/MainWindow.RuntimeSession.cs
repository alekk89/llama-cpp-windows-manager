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
    private async Task RecoverActiveRuntimeSessionAsync()
    {
        var appServices = AppServices;
        var modelLookup = appServices.ModelLookupApplication;

        await _coreServices.Runtime.RuntimeSessionRecoveryApplication.RecoverAsync(
            new RuntimeSessionRecoveryApplicationActions(
                modelLookup.ListAsync,
                appServices.StateStore.ListRuntimesAsync,
                async (settings, token) => await _coreServices.Runtime.RuntimeEndpointProbe.ServedModelsAsync(settings, token),
                async (settings, token) => await _coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync(settings, token),
                async (settings, token) => await _coreServices.Runtime.RuntimeEndpointProbe.IsRespondingAsync(settings, token),
                (model, settings) => StartModelLoadingTimer(model.Id, model.Name, settings),
                (model, settings) => StartRuntimeReadinessMonitor(model, settings),
                settings => _activeRuntimeSettings = settings,
                SetStatus,
                StartRuntimeDashboardRefreshTimer,
                RefreshOverviewModelSelectorAsync,
                RefreshRuntimeMetricsAsync));
        ApplyGpuEnergyTrackingBoundary();
    }

    private async Task MarkLoadedSessionsIfReadyAsync()
    {
        var result = await _coreServices.Runtime.RuntimeSessionReconciliationApplication.ReconcileAsync(
            new RuntimeSessionReconciliationApplicationActions(
                session => _coreServices.Runtime.RuntimeEndpointProbe.IsRespondingAsync(session.LaunchSettings),
                session => _coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync(session.LaunchSettings),
                settings => _activeRuntimeSettings = settings,
                transition =>
                {
                    if (_coreServices.Models.ModelRuntimeStatus.IsLoadingModel(transition.ModelId))
                        StopModelLoadingTimer(showLoadedDuration: true, loadedModelName: transition.ModelName);
                },
                RefreshOverviewSessionRows,
                UpdateOverviewModelActions));
        foreach (var session in result.RemovedSessions ?? [])
            await RecordRuntimeLifecycleAsync(
                session.Status == LoadedModelSessionStatus.Failed ? "failed" : "unloaded",
                session.SessionId,
                session.ModelId,
                session.ModelName,
                new { status = session.Status.ToString(), session.StatusReason, session.ProcessId });
        if ((result.RemovedSessions?.Count ?? 0) > 0)
            ApplyGpuEnergyTrackingBoundary();
    }

    private void RefreshOverviewSessionRows()
    {
        var metricsSessionId = _sessions.SelectedSnapshot()?.SessionId ?? "";
        var sessions = _sessions.OverviewSnapshots();
        _viewModel.Overview.ReplaceSessionsIfChanged(sessions, OverviewGatewayRoutingStatus(sessions));

        using var selectionScope = _coreServices.Ui.SelectionReentrancy.SuppressLoadedSessionSelection();
        _overviewPage.RestoreLoadedSessionSelection(metricsSessionId, _viewModel.Overview.SessionRows);
    }

    private async Task SaveActiveRuntimeSessionsAsync()
        => await _coreServices.Runtime.RuntimeSessionPersistence.SaveRunningAsync();

    private void ClearActiveRuntimeSession() => _coreServices.Runtime.RuntimeSessionPersistence.Clear();
}
