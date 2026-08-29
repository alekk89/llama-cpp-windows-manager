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
    private async Task RunAsync(string message, Func<Task> action)
        => await _coreServices.App.ForegroundTasks.RunBusyAsync(message, action, ForegroundTaskActions());

    private async Task RunResponsiveAsync(string message, Func<Task> action)
        => await _coreServices.App.ForegroundTasks.RunBusyAsync(message, action, ResponsiveTaskActions());

    private async Task RunTrayResponsiveAsync(string message, Func<Task> action)
        => await _coreServices.App.ForegroundTasks.RunBusyAsync(message, action, TrayResponsiveTaskActions());

    private async Task RunEventAsync(Func<Task> action)
        => await _coreServices.App.ForegroundTasks.RunEventAsync(action, ForegroundTaskActions());

    private void RunBackground(Func<Task> action, string failureMessage)
    {
        _ = _coreServices.App.BackgroundTasks.RunAsync(
            action,
            failureMessage,
            new BackgroundTaskApplicationActions(SetStatus, WriteAppLogAsync));
    }

    private ForegroundTaskApplicationActions ForegroundTaskActions()
        => new(
            TryBeginUiBusy,
            EndUiBusy,
            SetStatus,
            () => _viewModel.StatusText,
            async () => await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background),
            WriteAppLogAsync,
            ex => _coreServices.App.Dialogs.Notify(this, ex.Message, AppDisplayName, MessageBoxImage.Error));

    private ForegroundTaskApplicationActions ResponsiveTaskActions()
        => new(
            TryBeginResponsiveActivity,
            EndResponsiveActivity,
            SetStatus,
            () => _viewModel.StatusText,
            async () => await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background),
            WriteAppLogAsync,
            ex => _coreServices.App.Dialogs.Notify(this, ex.Message, AppDisplayName, MessageBoxImage.Error));

    private ForegroundTaskApplicationActions TrayResponsiveTaskActions()
        => new(
            TryBeginResponsiveActivity,
            EndResponsiveActivity,
            SetStatus,
            () => _viewModel.StatusText,
            async () => await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.Background),
            WriteAppLogAsync,
            ex => _trayIcon?.ShowNotification(ex.Message, error: true));
}
