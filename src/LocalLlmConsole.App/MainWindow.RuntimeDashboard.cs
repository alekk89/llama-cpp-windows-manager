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
    private async Task RefreshJobsAsync()
    {
        var jobs = await AppServices.StateStore.ListJobsAsync();
        _viewModel.Jobs.ReplaceJobs(await _coreServices.Runtime.JobRows.ProjectAsync(jobs));
    }

    private async Task RefreshOverviewAsync()
    {
        await MarkLoadedSessionsIfReadyAsync();
        if (_modelsPage.ModelsFolderText is { } modelsFolderText)
        {
            modelsFolderText.Text = _settings.ModelsRoot;
            modelsFolderText.ToolTip = _settings.ModelsRoot;
        }
        if (_runtimesPage.RuntimesFolderText is { } runtimesFolderText)
        {
            runtimesFolderText.Text = _settings.RuntimeRoot;
            runtimesFolderText.ToolTip = _settings.RuntimeRoot;
        }
        RefreshOverviewSessionRows();
    }

    private async Task RefreshRuntimeMetricsAsync()
    {
        var renderUiFrame = _minimizedUiRefreshPolicy.ShouldRender(
            WindowState == WindowState.Minimized || !IsVisible,
            DateTimeOffset.UtcNow);
        var renderOverview = _viewModel.CurrentPage == "Overview" && renderUiFrame;
        using var dashboardUpdate = _runtimeDashboardPage.DeferDashboardUpdates();
        await _coreServices.Runtime.RuntimeDashboardRefreshApplication.RefreshAsync(
            new RuntimeDashboardRefreshApplicationRequest(
                new RuntimeDashboardRefreshTarget(
                    _sessions.HasRunningSessions,
                    _runtimeDashboardPage.IsAvailable,
                    _runtimeDashboardPage.RuntimeMetricsGrid is not null,
                    _runtimeDashboardPage.RuntimeLogBox is not null),
                renderOverview,
                _settings,
                _llama.ActiveModelId,
                _llama.ActiveRuntimeId,
                _llama.State,
                _llama.IsRunning),
            RuntimeDashboardRefreshActions(renderOverview, renderUiFrame));
        if (renderOverview)
            ApplyObservedGpuEnergy();
    }

    private Task RenderStoppedSelectedOverviewModelAsync(ModelRecord? selectedOverviewModel, bool renderOverview)
    {
        ResetMetricCounters();
        if (!renderOverview || selectedOverviewModel is null) return Task.CompletedTask;

        _runtimeDashboardPage.SetMetricValue(OverviewDashboardMetricIds.ModelStatus, $"Stopped: {selectedOverviewModel.Name}");
        _runtimeDashboardPage.SetRuntimeLogText("No runtime is loaded for the selected model.", followTail: false);
        ApplyRuntimeMetricRows(new RuntimeMetricRowsRenderPlan([], null));
        ApplyRuntimeMetricSummary(RuntimeMetricSummaryPresentation.NoRuntime);
        ApplyObservedGpuEnergy();
        return Task.CompletedTask;
    }

    private RuntimeDashboardMetricsApplicationActions RuntimeDashboardMetricsActions()
        => new(
            RefreshRuntimeLogTailAsync,
            ApplyRuntimeMetricRows,
            ApplyRuntimeMetricSummary);

    private RuntimeDashboardRefreshApplicationActions RuntimeDashboardRefreshActions(
        bool renderOverview,
        bool renderUiFrame)
        => new(
            MarkLoadedSessionsIfReadyAsync,
            RefreshOverviewSessionRows,
            () => _sessions.Snapshots(),
            ApplyRuntimeEndpointHealthAsync,
            pollResults => TrackLifetimeTokenDeltasAsync(pollResults, renderUiFrame),
            ApplyIdleUnloadPoliciesAsync,
            SelectedOverviewModel,
            IsModelActive,
            IsModelLoaded,
            _sessions.SessionForModel,
            _sessions.SelectedSnapshot,
            () => _sessions.ActiveSettings,
            () => _activeRuntimeSettings,
            modelId => _coreServices.Runtime.RuntimeSessions.SelectModel(modelId),
            settings => _activeRuntimeSettings = settings,
            ActiveRuntimeLabelsAsync,
            RefreshModelStatusMetric,
            SaveActiveRuntimeSessionsAsync,
            CachedGpuSummaryAsync,
            _runtimeDashboardPage.ApplyHardwareSummaryAsync,
            RenderStoppedSelectedOverviewModelAsync,
            RuntimeDashboardMetricsActions(),
            renderOverview ? UpdateOverviewModelActions : () => { });

    private async Task ApplyRuntimeEndpointHealthAsync(IReadOnlyList<RuntimeMetricPollResult> pollResults)
    {
        var transitions = _sessions.ApplyEndpointHealth(pollResults);
        foreach (var transition in transitions)
        {
            Trace.TraceInformation(
                $"Runtime endpoint health changed for {transition.ModelName} ({transition.SessionId}): " +
                $"{transition.Previous} -> {transition.Current}. {transition.Reason}");
            await RecordRuntimeLifecycleAsync(
                "endpoint-health",
                transition.SessionId,
                transition.ModelId,
                transition.ModelName,
                new
                {
                    previous = transition.Previous.ToString(),
                    current = transition.Current.ToString(),
                    transition.Reason
                });
        }
        if (transitions.Count > 0)
            RefreshOverviewSessionRows();
    }
}
