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
        MetricCardFactory.ClearLastKnownMetricText(_runtimeDashboardPage.TokensLastKnown);

        MetricCardFactory.SetMetricText(_runtimeDashboardPage.TokensMetric, summary.Tokens);
        MetricCardFactory.SetMetricText(_runtimeDashboardPage.MtpTokensMetric, summary.MtpTokens);
        MetricCardFactory.SetMetricText(_runtimeDashboardPage.SlotsMetric, summary.Slots);
        MetricCardFactory.SetMetricText(_runtimeDashboardPage.KvCacheMetric, summary.KvCache);
        _runtimeDashboardPage.TokensGraph?.Push(
            summary.GraphSample.RuntimeKey,
            summary.GraphSample.GenerationRate,
            summary.GraphSample.PromptRate);
        _runtimeDashboardPage.MtpTokensGraph?.Push(
            summary.GraphSample.RuntimeKey,
            summary.GraphSample.SpeculativeGeneratedRate,
            summary.GraphSample.SpeculativeAcceptedRate);
        _runtimeDashboardPage.KvCacheGraph?.Push(
            summary.GraphSample.RuntimeKey,
            summary.GraphSample.KvCacheUsagePercent);
    }

    private void UpdateRuntimeModelProgress()
        => SetRuntimeModelProgress(_llama.State);

    private void SetRuntimeModelProgress(LlamaRuntimeState state)
    {
        if (_runtimeDashboardPage.ModelProgress is null) return;

        switch (state)
        {
            case LlamaRuntimeState.Loading:
                _runtimeDashboardPage.ModelProgress.Visibility = Visibility.Visible;
                _runtimeDashboardPage.ModelProgress.IsIndeterminate = true;
                _runtimeDashboardPage.ModelProgress.Value = 0;
                break;
            case LlamaRuntimeState.Loaded:
                _runtimeDashboardPage.ModelProgress.Visibility = Visibility.Visible;
                _runtimeDashboardPage.ModelProgress.IsIndeterminate = false;
                _runtimeDashboardPage.ModelProgress.Value = 100;
                break;
            default:
                _runtimeDashboardPage.ModelProgress.Visibility = Visibility.Collapsed;
                _runtimeDashboardPage.ModelProgress.IsIndeterminate = false;
                _runtimeDashboardPage.ModelProgress.Value = 0;
                break;
        }
    }

    private async Task<string> CachedGpuSummaryAsync()
    {
        var active = _sessions.SelectedSnapshot();
        return await _coreServices.Ui.RuntimeGpuSummaryApplication.SummaryAsync(active, DateTimeOffset.UtcNow);
    }

    private async Task<RuntimeMtpTokenSnapshot?> RefreshRuntimeLogTailAsync(RuntimeSlotSnapshot? slotSnapshot = null)
    {
        if (_runtimeDashboardPage.RuntimeLogBox is null) return null;

        var selectedLogPath = _sessions.SelectedSnapshot()?.LogPath;
        var logPath = string.IsNullOrWhiteSpace(selectedLogPath) ? _llama.LogPath : selectedLogPath;
        var capture = await _coreServices.Runtime.RuntimeLogTail.CaptureAsync(logPath);
        var tail = _coreServices.Runtime.RuntimeLogTail.Build(new RuntimeLogTailRequest(logPath, _llama.IsRunning, slotSnapshot), capture);
        _runtimeDashboardPage.SetRuntimeLogText(tail.Text, tail.HasActiveLog);
        return capture.MtpTokenStats;
    }

    private void ResetMetricCounters()
    {
        _coreServices.Runtime.RuntimeTelemetryApplication.ResetMetricCounters();
    }
}
