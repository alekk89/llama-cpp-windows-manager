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
    private async Task SaveSettingsAsync()
    {
        var settingsApplication = AppServices.SettingsApplication;
        Require(settingsApplication);
        await settingsApplication!.SaveEditedAndApplyAsync(new AppSettingsSaveApplicationRequest(
            _settings,
            _settingsPage.SelectedThemeValue,
            _viewModel.Settings.Rows.ToDictionary(row => row.Key, row => row.Value, StringComparer.OrdinalIgnoreCase),
            _sessions.Snapshots()),
            SettingsSaveActions());
    }

    private AppSettingsSaveApplicationActions SettingsSaveActions()
        => new(
            settings => _settings = settings,
            ApplyTheme,
            () => ApplyLaunchSettingsToControls(),
            RestartModelGatewayAsync,
            () => _viewModel.CurrentPage == "Settings",
            ShowSettings,
            SetStatus);

    private async Task<AppSettings> EnsureModelApiKeyAsync(AppSettings settings)
    {
        var settingsApplication = AppServices.SettingsApplication;
        Require(settingsApplication);
        var result = await settingsApplication!.EnsureModelApiKeyAsync(_settings, settings);
        if (result.GeneratedApiKey)
            _settings = result.PersistedSettings;
        return result.Settings;
    }
}
