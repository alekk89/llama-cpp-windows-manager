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
    private DateTimeOffset _lastRuntimeMetricPollAt = DateTimeOffset.MinValue;

    private void StartRuntimeDashboardRefreshTimer()
    {
        _coreServices.Ui.RuntimeDashboardRefreshTimer.Start(
            TimeSpan.FromSeconds(1),
            RuntimeDashboardTimerRefreshAsync,
            ex => SetStatus(Loc.T("Runtimes.RefreshFailed", ex.Message)),
            runImmediately: true);
    }

    private void StopRuntimeDashboardRefreshTimer()
    {
        _coreServices.Ui.RuntimeDashboardRefreshTimer.Stop();
    }

    private async Task RuntimeDashboardTimerRefreshAsync()
    {
        if (!_coreServices.Runtime.RuntimeTelemetryApplication.ShouldRunRefreshTimer(_viewModel.CurrentPage, _sessions.HasRunningSessions)) return;
        var now = DateTimeOffset.UtcNow;
        var minimizedOrHidden = WindowState == WindowState.Minimized || !IsVisible;
        if (!RuntimeMetricRefreshCadencePolicy.ShouldPoll(
                _viewModel.CurrentPage,
                minimizedOrHidden,
                now,
                _lastRuntimeMetricPollAt))
            return;
        _lastRuntimeMetricPollAt = now;
        await RefreshRuntimeMetricsAsync();
    }

}
