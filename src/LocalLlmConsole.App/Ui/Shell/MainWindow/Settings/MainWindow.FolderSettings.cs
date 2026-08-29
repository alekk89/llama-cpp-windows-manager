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
    private async Task ChooseModelsFolderAsync(bool scanAfter)
    {
        var result = await _coreServices.App.FolderSettingsApplication.ChooseModelsFolderAsync(_settings, scanAfter, FolderSettingsActions());
        _settings = result.Settings;
    }

    private async Task ChooseRuntimeFolderAsync(bool scanAfter)
    {
        var result = await _coreServices.App.FolderSettingsApplication.ChooseRuntimeFolderAsync(_settings, scanAfter, FolderSettingsActions());
        _settings = result.Settings;
    }

    private async Task PersistSettingsAsync()
        => _settings = await PersistSettingsAsync(_settings);

    private async Task<AppSettings> PersistSettingsAsync(AppSettings settings)
    {
        var settingsApplication = AppServices.SettingsApplication;
        Require(settingsApplication);
        _settings = await settingsApplication!.PersistAsync(settings);
        return _settings;
    }

    private FolderSettingsApplicationActions FolderSettingsActions()
        => new(
            initial => _coreServices.App.FileSystemDialogs.PickFolder(initial),
            RunAsync,
            PersistSettingsAsync,
            ScanModelsRootAsync,
            ScanRuntimeRootAsync,
            RefreshAllAsync,
            () => _viewModel.CurrentPage == "Models",
            () => _viewModel.CurrentPage == "Runtimes",
            () => _viewModel.CurrentPage == "Settings",
            ShowModels,
            ShowRuntimes,
            ShowSettings,
            SetStatus);

    private async Task ScanModelsRootAsync(string modelsRoot)
    {
        var catalog = ModelServices.Catalog;
        Require(catalog);
        await catalog!.ScanAsync(modelsRoot);
    }

    private async Task ScanRuntimeRootAsync(string runtimeRoot)
    {
        var runtimeCatalog = RuntimeServices.RuntimeCatalogApplication;
        Require(runtimeCatalog);
        await runtimeCatalog!.ScanAndMarkRuntimeRootAsync(runtimeRoot, _runtimeCatalogState);
    }
}
