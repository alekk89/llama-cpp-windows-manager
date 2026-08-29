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
    private async Task SelectWslDistroAsync(WslDistroRow? row)
    {
        var result = await _coreServices.Environment.WslDistroSelectionApplication.SelectAsync(
            _settings,
            row,
            WslDistroSelectionActions());
        _settings = result.Settings;
    }

    private async Task InstallWslAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.InstallWsl);

    private async Task InstallWslUbuntuAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.InstallUbuntu);

    private async Task CheckWslUpdatesAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.CheckWslUpdates);

    private async Task DeleteWslAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.DeleteWsl);

    private async Task CheckUbuntuUpdatesAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.CheckUbuntuUpdates);

    private async Task DeleteUbuntuAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.DeleteUbuntu);

    private async Task InstallUbuntuBuildToolsAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.InstallUbuntuBuildTools);

    private async Task DeleteUbuntuBuildToolsAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.DeleteUbuntuBuildTools);

    private async Task InstallUbuntuCudaToolkitAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.InstallUbuntuCudaToolkit);

    private async Task DeleteUbuntuCudaToolkitAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.DeleteUbuntuCudaToolkit);

    private async Task InstallUbuntuVulkanToolsAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.InstallUbuntuVulkanTools);

    private async Task DeleteUbuntuVulkanToolsAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.DeleteUbuntuVulkanTools);

    private async Task InstallUbuntuSyclRuntimeAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.InstallUbuntuSyclRuntime);

    private async Task DeleteUbuntuSyclRuntimeAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.DeleteUbuntuSyclRuntime);

    private async Task InstallUbuntuSyclOneApiAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.InstallUbuntuSyclOneApi);

    private async Task DeleteUbuntuSyclOneApiAsync()
        => await RunWslToolSetupAsync(WslToolSetupAction.DeleteUbuntuSyclOneApi);

    private async Task RunWslToolSetupAsync(WslToolSetupAction action)
    {
        _coreServices.Environment.WslToolSetupApplication.Run(action, SelectedUbuntuDistroName(), AppDisplayName, WslToolSetupActions());
        await Task.CompletedTask;
    }

    private WslToolSetupApplicationActions WslToolSetupActions()
        => new(
            plan => _coreServices.App.Dialogs.Confirm(
                this,
                plan.ConfirmationMessage,
                plan.Title,
                plan.IsWarning ? MessageBoxImage.Warning : MessageBoxImage.Information),
            SetStatus);

    private WslDistroSelectionApplicationActions WslDistroSelectionActions()
        => new(
            PersistSettingsAsync,
            RefreshWslLinuxAsync,
            SetStatus);

    private string SelectedUbuntuDistroName()
        => WslDistroSelectionApplicationService.PreferredUbuntuDistroName(
            _wslPage.SelectedDistroRow,
            _viewModel.WslLinux.Rows,
            _settings.WslDistro);
}
