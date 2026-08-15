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
    private void SetPage(string title, string subtitle)
    {
        if (title != "Models") StopDownloadHistoryRefreshTimer();
        if (title != "Overview") _pageControllers.Overview.CancelPendingSelections();
        if (title != "Overview" && !_sessions.HasRunningSessions) StopRuntimeDashboardRefreshTimer();
        _viewModel.CurrentPage = title;
        SetActiveNavigation(title);
    }
    private bool TryBeginUiBusy(string message)
    {
        if (!_viewModel.TryBeginBusy(out var busyMessage))
        {
            SetStatus(busyMessage);
            return false;
        }

        _coreServices.Ui.UiBusyState.Begin(PageHost.IsEnabled, SetPageHostEnabled, SetWaitCursor);
        SetStatus(message);
        return true;
    }

    private void EndUiBusy()
    {
        if (!_viewModel.EndBusy()) return;
        _coreServices.Ui.UiBusyState.End(SetPageHostEnabled, SetWaitCursor);
    }

    private bool TryBeginResponsiveActivity(string message)
    {
        if (_viewModel.TryBeginBusy(out var busyMessage))
        {
            SetStatus(message);
            return true;
        }

        SetStatus(busyMessage);
        return false;
    }

    private void EndResponsiveActivity()
        => _viewModel.EndBusy();

    private void SetStatus(string text)
    {
        _viewModel.SetStatus(text);
        if (AppStatusText is not null)
            AppStatusText.Text = _viewModel.DisplayStatusText;
    }

    private static void Require(object? value) { if (value is null) throw new InvalidOperationException("App is still starting."); }

    private void SetPageHostEnabled(bool enabled)
        => PageHost.IsEnabled = enabled;

    private static void SetWaitCursor(bool enabled)
        => System.Windows.Input.Mouse.OverrideCursor = enabled
            ? System.Windows.Input.Cursors.Wait
            : null;
}
