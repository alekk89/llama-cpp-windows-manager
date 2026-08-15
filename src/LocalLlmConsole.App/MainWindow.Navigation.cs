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
    private async Task RefreshAllAsync()
    {
        await RefreshModelsAsync();
        await EnsureRuntimeRootScannedAsync();
        await RefreshRuntimesAsync();
        await RefreshJobsAsync();
        await RefreshOverviewAsync();
        if (_viewModel.CurrentPage == "Lifetime") await RefreshLifetimeMetricsAsync();
        if (_viewModel.CurrentPage == "Windows") await RefreshWindowsAsync();
        if (_viewModel.CurrentPage == "WSL Linux") await RefreshWslLinuxAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(Loc.T("Status.Refreshing"), RefreshAllAsync);
    private void OpenWorkspace_Click(object sender, RoutedEventArgs e) => _coreServices.App.ShellIntegration.OpenFolder(_workspaceRoot);
    private void ShowOverview_Click(object sender, RoutedEventArgs e) => ShowOverview();
    private void ShowModels_Click(object sender, RoutedEventArgs e) => ShowModels();
    private void ShowRuntimes_Click(object sender, RoutedEventArgs e) => ShowRuntimes();
    private void ShowWindows_Click(object sender, RoutedEventArgs e) => ShowWindows();
    private void ShowWslLinux_Click(object sender, RoutedEventArgs e) => ShowWslLinux();
    private void ShowSettings_Click(object sender, RoutedEventArgs e) => ShowSettings();
    private void ShowLifetime_Click(object sender, RoutedEventArgs e) => ShowLifetime();
    private void ShowLogs_Click(object sender, RoutedEventArgs e) => ShowLogs();
    private void ShowUpdates_Click(object sender, RoutedEventArgs e) => ShowUpdates();
    private void ShowHelp_Click(object sender, RoutedEventArgs e) => ShowHelp();
}
