using System.Windows;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private UIElement CreateLaunchSettingsPanel()
    {
        var panel = SelectorFavoriteBinding.ConfigureLaunchSettings(LaunchSettingsPanelFactory.Create(new LaunchSettingsPanelRequest(
            _settings,
            _viewModel.LaunchSettings.RuntimeChoices,
            _coreServices.Ui.AdvancedSections.ShowLaunchSettings,
            () =>
            {
                UpdateLaunchControlVisibility();
                _launchSettingsController.ScheduleProfileFitCapabilityProbe();
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
            _launchSettingsController.FitSelectedProfileToAvailableVramAsync,
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
            initialPath => _coreServices.App.FileSystemDialogs.PickFolder(initialPath))), () => _stateStore, SetStatus);
        ApplyLaunchSettingsPanelControls(panel);
        _launchSettingsController.ScheduleProfileFitCapabilityProbe();
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
