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
    private void StartRuntimeDashboardRefreshTimer()
    {
        _coreServices.Ui.RuntimeDashboardRefreshTimer.Start(
            TimeSpan.FromSeconds(1),
            RuntimeDashboardTimerRefreshAsync,
            ex => SetStatus($"Runtime refresh failed: {ex.Message}"));
    }

    private void StopRuntimeDashboardRefreshTimer()
    {
        _coreServices.Ui.RuntimeDashboardRefreshTimer.Stop();
    }

    private async Task RuntimeDashboardTimerRefreshAsync()
    {
        if (!_coreServices.Runtime.RuntimeTelemetryApplication.ShouldRunRefreshTimer(_viewModel.CurrentPage, _sessions.HasRunningSessions)) return;
        await RefreshRuntimeMetricsAsync();
    }

    private async Task DeleteSelectedRuntimeAsync()
    {
        var runtimeCatalog = RuntimeServices.RuntimeCatalogApplication;
        if (runtimeCatalog is null) return;
        var runtime = SelectedRuntime();
        await runtimeCatalog.DeleteRegistrationAsync(runtime, RuntimeCatalogDeleteRegistrationActions());
    }

    private void RuntimeGrid_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (VisualTreeTraversal.FindAncestor<WpfButton>(e.OriginalSource as DependencyObject) is not null) return;
        var row = VisualTreeTraversal.FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        if (_runtimesPage.ClearSelectedRuntimeIfRowAlreadySelected(row))
        {
            e.Handled = true;
        }
    }

    private RuntimeCatalogDeleteRegistrationActions RuntimeCatalogDeleteRegistrationActions()
        => new(
            runtime => _coreServices.App.Dialogs.Confirm(
                this,
                $"Remove runtime registration?{Environment.NewLine}{Environment.NewLine}{runtime.Name}",
                "Remove runtime",
                MessageBoxImage.Warning),
            RefreshRuntimesAsync,
            RefreshOverviewAsync);

}
