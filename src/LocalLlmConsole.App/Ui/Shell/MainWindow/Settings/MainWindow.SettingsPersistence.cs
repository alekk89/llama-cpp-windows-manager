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
    private void ScheduleSettingsApply()
    {
        var themeMode = _settingsPage.SelectedThemeValue;
        var values = _viewModel.Settings.Rows.ToDictionary(
            row => row.Key,
            row => row.Value,
            StringComparer.OrdinalIgnoreCase);
        _coreServices.Ui.SettingsAutoApply.Schedule(
            cancellationToken => SaveSettingsAsync(themeMode, values, cancellationToken),
            action => RunBackground(action, "Automatic settings apply failed"));
    }

    private async Task SaveSettingsAsync(
        string themeMode,
        IReadOnlyDictionary<string, string> values,
        CancellationToken cancellationToken)
    {
        var settingsApplication = AppServices.SettingsApplication;
        Require(settingsApplication);
        await settingsApplication!.SaveEditedAndApplyAsync(new AppSettingsSaveApplicationRequest(
            _settings,
            themeMode,
            values,
            _sessions.Snapshots()),
            SettingsSaveActions(),
            cancellationToken);
    }

    private AppSettingsSaveApplicationActions SettingsSaveActions()
        => new(
            settings =>
            {
                var runtimeLogOrderChanged = !string.Equals(
                    _settings.RuntimeLogOrder,
                    settings.RuntimeLogOrder,
                    StringComparison.OrdinalIgnoreCase);
                _settings = settings;
                ApplyTrayIconVisibilityPreference();
                if (_viewModel.CurrentPage == "Settings")
                    _settingsPage.Synchronize(() => _viewModel.Settings.ApplyPersistedSettings(settings));
                ApplyGpuEnergyTrackingBoundary();
                _overviewPage.ApplyUiPreferences(settings);
                _modelsPage.ApplyUiPreferences(settings);
                if (runtimeLogOrderChanged)
                    RunBackground(RefreshRuntimeLogOrderAsync, "Runtime log order refresh failed");
            },
            ApplicationThemeService.Apply,
            () => ApplyLaunchSettingsToControls(),
            RestartModelGatewayAsync,
            () => false,
            () => { },
            SetStatus);

    private async Task<AppSettings> EnsureModelApiKeyAsync(AppSettings settings)
    {
        var settingsApplication = AppServices.SettingsApplication;
        Require(settingsApplication);
        var result = await settingsApplication!.EnsureModelApiKeyAsync(_settings, settings);
        _settings = result.PersistedSettings;
        return result.Settings;
    }
}
