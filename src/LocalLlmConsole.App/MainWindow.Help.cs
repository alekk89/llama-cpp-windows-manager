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
    private void ShowHelp()
    {
        SetPage("Help", Loc.T("Help.Summary"));
        var controller = new HelpPageController(_coreServices.App.HelpCatalog, NavigateFromHelp);
        var page = controller.Create();
        PageHost.Content = page.Content;
    }

    private void NavigateFromHelp(string target)
    {
        var plan = _coreServices.App.HelpNavigation.Plan(target);
        if (!plan.ShouldNavigate) return;

        ShowHelpNavigationDestination(plan.Destination);
        ApplyHelpNavigationFocus(plan.FocusTarget);
        SetStatus(plan.StatusMessage);
    }

    private void ShowHelpNavigationDestination(HelpNavigationDestination destination)
    {
        switch (destination)
        {
            case HelpNavigationDestination.Overview:
                ShowOverview();
                break;
            case HelpNavigationDestination.Models:
                ShowModels();
                break;
            case HelpNavigationDestination.Runtimes:
                ShowRuntimes();
                break;
            case HelpNavigationDestination.Windows:
                ShowWindows();
                break;
            case HelpNavigationDestination.WslLinux:
                ShowWslLinux();
                break;
            case HelpNavigationDestination.Settings:
                ShowSettings();
                break;
            case HelpNavigationDestination.Logs:
                ShowLogs();
                break;
            case HelpNavigationDestination.Lifetime:
                ShowLifetime();
                break;
            case HelpNavigationDestination.Updates:
                ShowUpdates();
                break;
            case HelpNavigationDestination.None:
                break;
        }
    }

    private void ApplyHelpNavigationFocus(HelpNavigationFocusTarget focusTarget)
    {
        switch (focusTarget)
        {
            case HelpNavigationFocusTarget.LoadedSessionsGrid:
                _overviewPage.FocusLoadedSessionsGrid();
                break;
            case HelpNavigationFocusTarget.ModelsGrid:
                _modelsPage.FocusModelsGrid();
                break;
            case HelpNavigationFocusTarget.HuggingFaceQueryBox:
                _modelsPage.FocusHuggingFaceQueryBox();
                break;
            case HelpNavigationFocusTarget.ModelCombo:
                _overviewPage.FocusModelCombo();
                break;
            case HelpNavigationFocusTarget.LogsGrid:
                _logsPage.FocusLogsGrid();
                break;
            case HelpNavigationFocusTarget.None:
                break;
        }
    }

    private void ApplyPendingHelpFocus()
    {
        if (_viewModel.CurrentPage == "Windows")
        {
            ClearWindowsHelpHighlights();
            return;
        }

        if (_viewModel.CurrentPage != "WSL Linux") return;
        ClearWslHelpHighlights();
    }

    private void ClearWslHelpHighlights()
    {
        foreach (var button in _wslPage.HelpButtons)
        {
            if (button is not null) button.Tag = null;
        }
    }

    private void ClearWindowsHelpHighlights()
    {
        foreach (var button in _windowsPage.HelpButtons)
        {
            if (button is not null) button.Tag = null;
        }
    }

    private void HighlightFirstVisibleHelpButton(params WpfButton?[] buttons)
    {
        foreach (var button in buttons)
        {
            if (button is null || button.Visibility != Visibility.Visible) continue;
            HighlightHelpButton(button, focus: true);
            return;
        }
    }

    private static void HighlightHelpButton(WpfButton? button, bool focus)
    {
        if (button is null || button.Visibility != Visibility.Visible) return;
        button.Tag = "Active";
        if (!focus) return;
        button.Dispatcher.BeginInvoke(new Action(() =>
        {
            button.Focus();
            button.BringIntoView();
        }), System.Windows.Threading.DispatcherPriority.ContextIdle);
    }
}
