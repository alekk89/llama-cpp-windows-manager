using System.Windows;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task RefreshAllAsync()
    {
        await RefreshModelsAsync();
        await EnsureRuntimeRootScannedAsync();
        await RefreshRuntimesAsync();
        await RefreshOverviewAsync();
        if (_viewModel.CurrentPage == "Overview") await RefreshRuntimeMetricsAsync();
        if (_viewModel.CurrentPage == "Metrics") await RefreshLifetimeMetricsAsync();
        if (_viewModel.CurrentPage == "Benchmarks") await RefreshBenchmarksAsync();
        if (_viewModel.CurrentPage == "Windows") await RefreshWindowsAsync();
        if (_viewModel.CurrentPage == "WSL Linux") await RefreshWslLinuxAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(Loc.T("Status.Refreshing"), RefreshAllAsync);
    private void OpenWorkspace_Click(object sender, RoutedEventArgs e) => _coreServices.App.ShellIntegration.OpenFolder(_workspaceRoot);
    private void ShowOverview_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowOverview(); }
    private void ShowModels_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowModels(); }
    private void ShowRuntimes_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowRuntimes(); }
    private void ShowBenchmarks_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowBenchmarks(); }
    private void ShowWindows_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowWindows(); }
    private void ShowWslLinux_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowWslLinux(); }
    private void ShowSettings_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowSettings(); }
    private void ShowLifetime_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowLifetime(); }
    private void ShowLogs_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowLogs(); }
    private void ShowUpdates_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowUpdates(); }
    private void ShowHelp_Click(object sender, RoutedEventArgs e) { if (CanLeaveCurrentPage()) ShowHelp(); }

    // Leaving or rebuilding the Models page discards the launch settings form, so unsaved edits are confirmed first.
    private bool CanLeaveCurrentPage()
        => _viewModel.CurrentPage != "Models" || _launchSettingsController.ConfirmLeavingEdits();
}
