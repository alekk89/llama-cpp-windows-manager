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
        _viewModel.RuntimeMetrics.ReplaceSamples(plan.Samples);
        if (plan.LeadingRow is not null)
            _viewModel.RuntimeMetrics.Rows.Insert(0, plan.LeadingRow);
        _runtimeDashboardPage.RuntimeMetricsGrid?.Items.Refresh();
    }

    private void ApplyRuntimeMetricSummary(RuntimeMetricSummaryPresentation summary)
    {
        ClearLastKnownMetricText(_runtimeDashboardPage.TokensLastKnown);

        SetMetricText(_runtimeDashboardPage.TokensMetric, summary.Tokens);
        SetMetricText(_runtimeDashboardPage.MtpTokensMetric, summary.MtpTokens);
        SetMetricText(_runtimeDashboardPage.SlotsMetric, summary.Slots);
        SetMetricText(_runtimeDashboardPage.KvCacheMetric, summary.KvCache);
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

    private void RefreshRuntimeLogTail(RuntimeSlotSnapshot? slotSnapshot = null)
    {
        if (_runtimeDashboardPage.RuntimeLogBox is null) return;

        var tail = _coreServices.Runtime.RuntimeLogTail.Build(new RuntimeLogTailRequest(_llama.LogPath, _llama.IsRunning, slotSnapshot));
        _runtimeDashboardPage.RuntimeLogBox.Text = tail.Text;
        if (tail.HasActiveLog)
        {
            _runtimeDashboardPage.RuntimeLogBox.CaretIndex = _runtimeDashboardPage.RuntimeLogBox.Text.Length;
            _runtimeDashboardPage.RuntimeLogBox.ScrollToEnd();
        }
    }

    private RuntimeMtpTokenSnapshot? ReadMtpTokenStatsFromRuntimeLog()
    {
        var selectedLogPath = _sessions.SelectedSnapshot()?.LogPath;
        return _coreServices.Runtime.RuntimeLogTail.MtpTokenStats(
            string.IsNullOrWhiteSpace(selectedLogPath) ? _llama.LogPath : selectedLogPath);
    }

    private void ResetMetricCounters()
    {
        _coreServices.Runtime.RuntimeTelemetryApplication.ResetMetricCounters();
    }
}
