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
    private async Task RunRuntimeSourceRowActionAsync(RuntimePackagePresetRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        switch (row.SourceActionKind)
        {
            case RuntimeSourceRowActionKind.Add:
                await AddCustomRuntimeRepositoryAsync();
                break;
            case RuntimeSourceRowActionKind.Check when row.SourcePreset is not null:
                await CheckRuntimePresetUpdateAsync(row.SourcePreset, null);
                break;
            case RuntimeSourceRowActionKind.Download when row.SourcePreset is not null:
                await DownloadRuntimeSourceAsync(row.SourcePreset);
                break;
            case RuntimeSourceRowActionKind.Build when row.DownloadedSource is not null:
                await BuildRuntimeSourceAsync(row.DownloadedSource, deleteSourceAfterBuild: true);
                break;
        }
    }

    private async Task DownloadRuntimeSourceAsync(RuntimeBuildPreset preset)
    {
        var sourceApplication = RuntimeServices.RuntimeSourceApplication;
        if (sourceApplication is null) return;
        await sourceApplication.DownloadAsync(
            preset,
            _settings,
            _runtimeCatalogState,
            MaxLogBytes(),
            RuntimeSourceApplicationActions());
    }

    private async Task CheckRuntimePresetUpdateAsync(RuntimeBuildPreset preset, RuntimeBuildPresetRow? row)
    {
        var sourceApplication = RuntimeServices.RuntimeSourceApplication;
        if (sourceApplication is null) return;
        await sourceApplication.CheckUpdateAsync(
            preset,
            row,
            _settings,
            _runtimeCatalogState,
            MaxLogBytes(),
            RuntimeSourceApplicationActions());
    }

    private RuntimeSourceApplicationActions RuntimeSourceApplicationActions()
        => new(
            RunResponsiveAsync,
            RefreshJobsAsync,
            RefreshRuntimesAsync,
            RefreshOverviewAsync,
            () => Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background).Task,
            _runtimesPage.RefreshRuntimeDownloadsGrid,
            SetStatus,
            (title, message) => _coreServices.App.Dialogs.Notify(this, message, title, MessageBoxImage.Information));
}
