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
    private async Task DeleteRuntimeDownloadRowAsync(RuntimePackagePresetRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        if (row.DeleteKind == RuntimeDownloadDeleteKind.Source && row.SourcePreset is not null)
        {
            await DeleteAllRuntimePresetBuildsAsync(row.SourcePreset);
            return;
        }

        if (row.DeleteKind == RuntimeDownloadDeleteKind.Package && row.Preset is not null)
            await DeleteRuntimePackageBuildsAsync(row.Preset);
    }

    private async Task InstallRuntimePackageAsync(RuntimePackagePreset preset)
    {
        var packageApplication = RuntimeServices.RuntimePackageApplication;
        if (packageApplication is null) return;
        await packageApplication.InstallAsync(preset, _settings, _runtimeCatalogState, MaxLogBytes(), RuntimePackageActions(responsive: true));
    }

    private async Task CheckRuntimePackageUpdateAsync(RuntimePackagePreset preset, RuntimePackagePresetRow? row)
    {
        var packageApplication = RuntimeServices.RuntimePackageApplication;
        if (packageApplication is null) return;
        await packageApplication.CheckUpdateAsync(preset, row, _settings, _runtimeCatalogState, MaxLogBytes(), RuntimePackageActions(responsive: true));
    }

    private async Task DeleteRuntimePackageBuildsAsync(RuntimePackagePreset preset)
    {
        var packageApplication = RuntimeServices.RuntimePackageApplication;
        if (packageApplication is null) return;
        await packageApplication.DeleteBuildsAsync(preset, _settings, _runtimeCatalogState, RuntimePackageActions());
    }

    private RuntimePackageApplicationActions RuntimePackageActions(bool responsive = false)
        => new(
            responsive ? RunResponsiveAsync : RunAsync,
            RefreshRuntimesAsync,
            RefreshOverviewAsync,
            RefreshJobsAsync,
            async () => await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background),
            _runtimesPage.RefreshRuntimePackageGrid,
            SetStatus,
            (title, message) => _coreServices.App.Dialogs.Notify(this, message, title, MessageBoxImage.Information),
            confirmation => _coreServices.App.Dialogs.Confirm(
                this,
                confirmation.Message,
                confirmation.Title,
                MessageBoxImage.Warning),
            ConfirmSkipChecksum: message => _coreServices.App.Dialogs.Confirm(
                this,
                message,
                "Skip checksum verification?",
                MessageBoxImage.Warning));
}
