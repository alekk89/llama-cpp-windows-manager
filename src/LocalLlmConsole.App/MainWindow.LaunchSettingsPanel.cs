using System.Windows;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private UIElement CreateLaunchSettingsPanel()
    {
        var panel = LaunchSettingsPanelFactory.Create(new LaunchSettingsPanelRequest(
            _settings,
            _viewModel.LaunchSettings.RuntimeChoices,
            _coreServices.Ui.AdvancedSections.ShowLaunchSettings,
            () =>
            {
                UpdateLaunchControlVisibility();
                ScheduleRuntimeLaunchOptionDiscovery();
                UpdateLaunchSaveButtonState();
            },
            showAdvanced =>
            {
                _coreServices.Ui.AdvancedSections.SetLaunchSettings(showAdvanced);
                UpdateLaunchControlVisibility();
            },
            ScheduleLaunchSettingsInputRefresh,
            SaveLaunchSettingsForSelectedModelAsync,
            SaveLaunchDefaultsFromControlsAsync,
            ResetLaunchSettingsToDefaults,
            SaveLaunchSettingsAsNewModelAsync,
            ChooseVisionProjectorPathAsync,
            ChooseDraftModelPathAsync,
            ChooseMtpHeadPathAsync,
            UpdateLaunchSaveButtonState,
            initialPath => _coreServices.App.FileSystemDialogs.PickOpenFile(new OpenFilePickerRequest(
                "Choose launch option file",
                "All files (*.*)|*.*",
                CheckFileExists: true,
                AddExtension: false,
                DefaultExt: "",
                FileName: File.Exists(initialPath) ? Path.GetFileName(initialPath) : "",
                InitialDirectory: File.Exists(initialPath) ? Path.GetDirectoryName(initialPath) ?? "" : ""), this),
            initialPath => _coreServices.App.FileSystemDialogs.PickFolder(initialPath)));

        ApplyLaunchSettingsPanelControls(panel);
        AttachLaunchSettingsChangeHandlers();
        ApplyLaunchSettingsToControls();
        RunBackground(() => RenderSelectedModelLaunchSettingsAsync(), "Launch settings refresh failed");
        return panel.Root;
    }

    private void ApplyLaunchSettingsPanelControls(LaunchSettingsPanelControls panel)
    {
        _launchSettingsPanel.Apply(panel);
    }
}
