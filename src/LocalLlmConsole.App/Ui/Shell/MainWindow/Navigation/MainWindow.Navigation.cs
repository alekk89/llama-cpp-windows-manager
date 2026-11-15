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
    private async void ShowOverview_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowOverview(); }
    private async void ShowModels_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowModels(); }
    private async void ShowRuntimes_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowRuntimes(); }
    private async void ShowBenchmarks_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowBenchmarks(); }
    private async void ShowWindows_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowWindows(); }
    private async void ShowWslLinux_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowWslLinux(); }
    private async void ShowSettings_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowSettings(); }
    private async void ShowLifetime_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowLifetime(); }
    private async void ShowLogs_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowLogs(); }
    private async void ShowUpdates_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowUpdates(); }
    private async void ShowHelp_Click(object sender, RoutedEventArgs e) { if (await CanLeaveCurrentPageAsync()) ShowHelp(); }

    // Leaving or rebuilding the Models page discards the launch settings form, so unsaved edits are confirmed first.
    private async Task<bool> CanLeaveCurrentPageAsync()
        => _viewModel.CurrentPage != "Models" || await _launchSettingsController.ConfirmLeavingEditsAsync();
}
