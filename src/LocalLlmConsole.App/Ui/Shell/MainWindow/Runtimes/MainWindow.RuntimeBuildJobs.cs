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
    private async Task BuildRuntimeSourceAsync(RuntimeSourceEntry source, bool deleteSourceAfterBuild = false)
    {
        var buildApplication = RuntimeServices.RuntimeBuildApplication;
        if (buildApplication is null) return;
        var settings = deleteSourceAfterBuild
            ? _settings with { DeleteRuntimeSourceAfterSuccessfulBuild = true }
            : _settings;
        await buildApplication.BuildSourceAsync(source, settings, MaxLogBytes(), RuntimeBuildApplicationActions());
    }

    private async Task DeleteRuntimeSourceAsync(RuntimeSourceEntry source)
    {
        var deletion = RuntimeServices.RuntimeBuildDeletionApplication;
        if (deletion is null) return;
        await deletion.DeleteSourceAsync(source, _settings, RuntimeBuildDeletionActions());
    }

    private async Task DeleteAllRuntimePresetBuildsAsync(RuntimeBuildPreset preset)
    {
        var deletion = RuntimeServices.RuntimeBuildDeletionApplication;
        if (deletion is null) return;
        await deletion.DeletePresetBuildsAsync(preset, _settings, RuntimeBuildDeletionActions());
    }

    private RuntimeBuildDeletionApplicationActions RuntimeBuildDeletionActions()
        => new(
            confirmation => _coreServices.App.Dialogs.Confirm(
                this,
                confirmation.Message,
                confirmation.Title,
                MessageBoxImage.Warning),
            RunAsync,
            RefreshRuntimesAsync,
            RefreshOverviewAsync,
            SetStatus);

    private async Task BuildManagedRuntimeAsync(RuntimeBuildPreset preset, bool update, RuntimeSourceEntry? source = null)
    {
        var buildApplication = RuntimeServices.RuntimeBuildApplication;
        if (buildApplication is null) return;
        var settings = source is null
            ? _settings
            : _settings with { DeleteRuntimeSourceAfterSuccessfulBuild = true };
        await buildApplication.BuildAsync(
            new RuntimeBuildApplicationRequest(
                preset,
                settings,
                update,
                source,
                MaxLogBytes()),
            RuntimeBuildApplicationActions());
    }

    private RuntimeBuildApplicationActions RuntimeBuildApplicationActions()
        => new(
            RunResponsiveAsync,
            RefreshRuntimesAsync,
            RefreshOverviewAsync,
            SetStatus,
            (title, message) => _coreServices.App.Dialogs.Notify(this, message, title, MessageBoxImage.Information),
            ProgressStatusAsync: async message => await Dispatcher.InvokeAsync(() => SetStatus(message)));
}
