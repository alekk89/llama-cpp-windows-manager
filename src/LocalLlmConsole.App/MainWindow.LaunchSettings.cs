namespace LocalLlmConsole;

public partial class MainWindow
{
    private void ScheduleSelectedModelLaunchSettingsRefresh()
        => _launchSettingsController.ScheduleSelectedModelRefresh();

    private void CancelLaunchSettingsRefresh()
        => _launchSettingsController.CancelRefresh();

    private Task RenderSelectedModelLaunchSettingsAsync(CancellationToken cancellationToken = default)
        => _launchSettingsController.RenderSelectedAsync(cancellationToken);

    private Task SaveLaunchSettingsForSelectedModelAsync()
        => _launchSettingsController.SaveSelectedProfileAsync();

    private Task SaveLaunchSettingsAsNewModelAsync()
        => _launchSettingsController.SaveAsNewModelAsync();

    private Task SaveLaunchDefaultsFromControlsAsync()
        => _launchSettingsController.SaveDefaultsAsync();

    private void ResetLaunchSettingsToDefaults()
        => _launchSettingsController.ResetToDefaults();

    private Task ChooseVisionProjectorPathAsync()
        => _launchSettingsController.ChooseVisionProjectorAsync();

    private Task ChooseMtpHeadPathAsync()
        => _launchSettingsController.ChooseMtpHeadAsync();

    private Task ChooseDraftModelPathAsync()
        => _launchSettingsController.ChooseDraftModelAsync();

    private AppSettings ReadLaunchSettingsFromControls()
        => _launchSettingsController.ReadFromControls();

    private void ApplyLaunchSettingsToControls(AppSettings? source = null)
        => _launchSettingsController.ApplyToControls(source);

    private void AttachLaunchSettingsChangeHandlers()
        => _launchSettingsController.AttachChangeHandlers();

    private void ScheduleLaunchSettingsInputRefresh()
        => _launchSettingsController.ScheduleInputRefresh();

    private void UpdateLaunchSaveButtonState()
        => _launchSettingsController.UpdateSaveButtonState();
}
