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
    private void ShowWindows()
    {
        SetPage("Windows", "Detect native Windows build tools for llama.cpp and open setup actions.");
        var page = WindowsPageFactory.Create(new WindowsPageRequest(
            _viewModel,
            _pageControllers.Windows.Build(),
            ButtonToolTip));
        _windowsPage.Apply(page);
        PageHost.Content = page.Root;
        if (_environmentPageSnapshots.TryGetWindowsTools(out var cachedWindowsTools))
            PopulateWindowsPage(cachedWindowsTools);

        ApplyPendingHelpFocus();
        if (_environmentPageSnapshots.TryStartWindowsAutoRefresh())
            RunBackground(RefreshWindowsAsync, "Windows tool refresh failed");
    }

    private async Task RefreshWindowsAsync()
    {
        await _coreServices.Environment.WindowsToolSetupApplication.RefreshAsync(new WindowsToolRefreshApplicationActions(
            RunAsync,
            () => _coreServices.Environment.WindowsToolSetupWorkflow.RefreshAsync(),
            _environmentPageSnapshots.StoreWindowsTools,
            PopulateWindowsPage,
            SetStatus));
    }

    private void PopulateWindowsPage(WindowsToolSnapshot tools)
    {
        MetricCardFactory.SetMetricText(_windowsPage.StatusMetric, WindowsEnvironmentService.Status(tools));
        MetricCardFactory.SetMetricText(_windowsPage.CpuMetric, tools.CpuToolsInstalled ? "Ready" : "Incomplete");
        MetricCardFactory.SetMetricText(_windowsPage.CudaMetric, tools.CudaToolsInstalled ? "Ready" : "Incomplete");
        MetricCardFactory.SetMetricText(_windowsPage.VulkanMetric, tools.VulkanToolsInstalled ? "Ready" : "Incomplete");
        MetricCardFactory.SetMetricText(_windowsPage.SyclMetric, tools.SyclToolsInstalled ? "Ready" : "Incomplete");

        if (_windowsPage.InstallCpuToolsButton is not null)
            _windowsPage.InstallCpuToolsButton.Content = WindowsEnvironmentService.CpuToolsActionLabel(tools);
        if (_windowsPage.InstallCudaToolkitButton is not null)
            _windowsPage.InstallCudaToolkitButton.Content = WindowsEnvironmentService.CudaToolsActionLabel(tools);
        if (_windowsPage.InstallVulkanToolsButton is not null)
            _windowsPage.InstallVulkanToolsButton.Content = WindowsEnvironmentService.VulkanToolsActionLabel(tools);
        if (_windowsPage.InstallSyclToolsButton is not null)
            _windowsPage.InstallSyclToolsButton.Content = WindowsEnvironmentService.SyclToolsActionLabel(tools);

        _viewModel.Windows.ReplaceToolRows(tools);
    }

    private async Task InstallWindowsCpuToolsAsync()
        => await RunWindowsToolSetupAsync(WindowsToolSetupAction.Cpu);

    private async Task InstallWindowsCudaToolkitAsync()
        => await RunWindowsToolSetupAsync(WindowsToolSetupAction.Cuda);

    private async Task InstallWindowsVulkanToolsAsync()
        => await RunWindowsToolSetupAsync(WindowsToolSetupAction.Vulkan);

    private async Task InstallWindowsSyclToolsAsync()
        => await RunWindowsToolSetupAsync(WindowsToolSetupAction.Sycl);

    private async Task RunWindowsToolSetupAsync(WindowsToolSetupAction action)
    {
        _coreServices.Environment.WindowsToolSetupApplication.Run(action, WindowsToolSetupActions());
        await Task.CompletedTask;
    }

    private WindowsToolSetupApplicationActions WindowsToolSetupActions()
        => new(
            plan => _coreServices.App.Dialogs.Confirm(
                this,
                plan.ConfirmationMessage,
                plan.Title,
                MessageBoxImage.Information),
            SetStatus);
}
