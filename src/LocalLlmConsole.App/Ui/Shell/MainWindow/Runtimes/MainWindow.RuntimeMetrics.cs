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
    private void ApplyRuntimeMetricRows(RuntimeMetricRowsRenderPlan plan)
    {
        _viewModel.RuntimeMetrics.ReplaceSamples(plan.Samples, plan.LeadingRow);
    }

    private void ApplyRuntimeMetricSummary(RuntimeMetricSummaryPresentation summary)
    {
        _runtimeDashboardPage.ApplyMetricSummary(summary);
        _runtimeDashboardPage.DashboardController?.ApplyGatewayPerformance(
            _coreServices.Models.ModelGatewayHostFactory.Performance.Snapshot());
    }

    private async Task<HostHardwareSnapshot> CachedGpuSummaryAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var selected = _sessions.SelectedSnapshot();
        var hostTask = _coreServices.Ui.RuntimeGpuSummaryApplication.SnapshotAsync(null, now);
        if (selected is null || !selected.IsRunning) return await hostTask;
        var processTask = _coreServices.App.GpuStatus.ProcessSummaryAsync(selected.ProcessId);
        await Task.WhenAll(hostTask, processTask);
        var host = await hostTask;
        var process = await processTask;
        return string.Equals(process, "Unavailable", StringComparison.OrdinalIgnoreCase)
            ? host
            : HostHardwareSnapshotParser.Parse(
                $"{host.Summary}{Environment.NewLine}{process}",
                now);
    }

    private void StartGpuEnergyTrackingTimer()
    {
        _coreServices.Ui.GpuEnergyTrackingTimer.Start(
            TimeSpan.FromSeconds(10),
            CaptureGpuEnergySampleAsync,
            ex => Trace.TraceWarning($"GPU energy sample was not persisted: {ex.Message}"),
            runImmediately: true);
    }

    private async Task CaptureGpuEnergySampleAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var lifetimeMetrics = AppServices.LifetimeMetricsApplication;
        if (lifetimeMetrics is null) return;
        var plan = CurrentGpuEnergySamplingPlan();
        lifetimeMetrics.SetGpuEnergyPersistenceActive(plan.PersistHistory);
        if (!lifetimeMetrics.ReserveGpuEnergySample(now, plan.Interval)) return;
        var powerSnapshot = await _coreServices.Ui.RuntimeGpuSummaryApplication.PowerSnapshotAsync(now);
        await lifetimeMetrics.ObserveGpuPowerAsync(powerSnapshot, now, plan.PersistHistory);
        if (_viewModel.CurrentPage == "Overview"
            && WindowState != WindowState.Minimized
            && IsVisible)
            ApplyObservedGpuEnergy();
        if (LifetimeMetricsRefreshPolicy.ShouldRefresh(
                WindowState != WindowState.Minimized && IsVisible,
                _viewModel.CurrentPage == "Metrics",
                lifetimeMetrics.DataVersion,
                _lastLifetimeReportDataVersion,
                now,
                _nextLifetimeReportRefreshAt))
            await RefreshLifetimeMetricsAsync();
    }

    private GpuEnergySamplingPlan CurrentGpuEnergySamplingPlan()
        => GpuEnergySamplingPolicy.Decide(
            _sessions.HasRunningSessions,
            _settings.TrackGpuEnergyWhileIdle);

    private void ApplyGpuEnergyTrackingBoundary()
    {
        var lifetimeMetrics = AppServices.LifetimeMetricsApplication;
        if (lifetimeMetrics is null) return;
        lifetimeMetrics.SetGpuEnergyPersistenceActive(
            CurrentGpuEnergySamplingPlan().PersistHistory);
    }

    private void ApplyObservedGpuEnergy()
        => _runtimeDashboardPage.ApplyObservedGpuEnergy(
            AppServices.LifetimeMetricsApplication?.ObservedGpuEnergySnapshot(
                ElectricityTariffPolicy.FromSettings(_settings),
                TimeZoneInfo.Local));

    private async Task<RuntimeMtpTokenSnapshot?> RefreshRuntimeLogTailAsync(RuntimeSlotSnapshot? slotSnapshot = null)
        => await RefreshRuntimeLogTailAsync(slotSnapshot, forceOrderPosition: false);

    private async Task<RuntimeMtpTokenSnapshot?> RefreshRuntimeLogTailAsync(
        RuntimeSlotSnapshot? slotSnapshot,
        bool forceOrderPosition)
    {
        if (_runtimeDashboardPage.RuntimeLogBox is null) return null;

        var selectedLogPath = _sessions.SelectedSnapshot()?.LogPath;
        var logPath = string.IsNullOrWhiteSpace(selectedLogPath) ? _llama.LogPath : selectedLogPath;
        var capture = await _coreServices.Runtime.RuntimeLogTail.CaptureAsync(logPath);
        var newestFirst = AppPreferenceService.RuntimeLogNewestFirst(_settings.RuntimeLogOrder);
        var tail = _coreServices.Runtime.RuntimeLogTail.Build(new RuntimeLogTailRequest(
            logPath,
            _llama.IsRunning,
            slotSnapshot,
            NewestFirst: newestFirst), capture);
        _runtimeDashboardPage.SetRuntimeLogText(
            tail.Text,
            followTail: !newestFirst,
            forceFollowTail: forceOrderPosition && !newestFirst,
            forceTop: forceOrderPosition && newestFirst);
        return capture.MtpTokenStats;
    }

    private async Task RefreshRuntimeLogOrderAsync()
        => await RefreshRuntimeLogTailAsync(null, forceOrderPosition: true);

    private void ResetMetricCounters()
    {
        _coreServices.Runtime.RuntimeTelemetryApplication.ResetMetricCounters();
    }
}
