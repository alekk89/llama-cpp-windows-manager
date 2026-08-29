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
    private ModelRecord? SelectedModel()
        => _modelsPage.SelectedModel;
    private string SelectedModelLaunchProfileId()
        => _modelsPage.SelectedLaunchProfileId;
    private RuntimeRecord? SelectedRuntime() => _runtimesPage.SelectedRuntime;

    private static ModelRecord? ModelFromRow(ModelGridRow row) => row.Model;

    private async Task<ModelRecord?> FindModelByIdAsync(string modelId)
    {
        var modelLookup = AppServices.ModelLookupApplication;
        return modelLookup is null ? null : await modelLookup.FindByIdAsync(modelId);
    }

    private static RuntimeRecord? RuntimeFromRow(RuntimeCatalogRow row) => row.Runtime;

    private static RuntimeSourceEntry? RuntimeSourceFromRow(RuntimeCatalogRow row) => row.Source;

    private static ModelRecord? ModelFromRowButton(object sender)
    {
        if ((sender as FrameworkElement)?.Tag is not ModelGridRow row) return null;
        return ModelFromRow(row);
    }

    private static ModelGridRow? ModelRowFromButton(object sender)
        => (sender as FrameworkElement)?.Tag as ModelGridRow;

    private void SelectModelGridRow(DataGrid? selectedGrid, DataGrid? otherGrid)
    {
        if (!_modelsPage.TrySelectModelGridRow(selectedGrid, otherGrid)) return;

        using var selection = _coreServices.Ui.SelectionReentrancy.TryBeginModelGridSelection();
        if (selection is null) return;

        if (selectedGrid?.SelectedItem is ModelGridRow { LaunchProfile: null } row)
        {
            _viewModel.Models.ShowLaunchProfilesForModel(row.Model.Id);
            _modelsPage.SelectDefaultLaunchProfile(_viewModel.Models.VariantRows);
        }

        ScheduleSelectedModelLaunchSettingsRefresh();
    }

    private RuntimeRecord? RuntimeFromRowButton(object sender)
    {
        if ((sender as FrameworkElement)?.Tag is not RuntimeCatalogRow row) return null;
        return RuntimeFromRow(row);
    }

    private RuntimeSourceEntry? RuntimeSourceFromRowButton(object sender)
    {
        if ((sender as FrameworkElement)?.Tag is not RuntimeCatalogRow row) return null;
        return RuntimeSourceFromRow(row);
    }

    private RuntimePackagePreset? RuntimePackagePresetFromRowButton(object sender)
    {
        if ((sender as FrameworkElement)?.Tag is not RuntimePackagePresetRow row) return null;
        return row.Preset;
    }

    private JobRecord? JobFromRowButton(object sender)
    {
        if ((sender as FrameworkElement)?.Tag is not HuggingFaceDownloadRow row) return null;
        return JobFromRow(row);
    }

    private static JobRecord? JobFromRow(HuggingFaceDownloadRow? row)
        => row?.Job;

    private JobRecord? SelectedDownloadJob()
    {
        if (_modelsPage.SelectedDownloadHistoryRow is not { } row) return null;
        return JobFromRow(row);
    }

    private async Task<HuggingFaceInstallInventory> InstalledHuggingFaceInventoryAsync()
    {
        var modelLookup = AppServices.ModelLookupApplication;
        return modelLookup is null
            ? HuggingFaceInstallStateService.BuildInventory([])
            : await modelLookup.BuildHuggingFaceInstallInventoryAsync();
    }

    private async Task RefreshHuggingFaceInstallStateAsync()
    {
        if (_downloadHistoryPageState.IsShowingHistory || !_modelsPage.HasHuggingFaceGrid || _viewModel.HuggingFace.SearchRows.Count == 0) return;
        var installed = await InstalledHuggingFaceInventoryAsync();
        foreach (var row in _viewModel.HuggingFace.SearchRows)
        {
            var file = row.File;
            var isInstalled = HuggingFaceInstallStateService.IsInstalled(file, installed, _settings.ModelsRoot);
            row.DownloadAction = isInstalled ? "Installed" : "Download";
            row.CanDownload = !isInstalled;
        }
    }

    private async Task CleanupActiveWslBuildsAsync()
        => await _coreServices.Runtime.RuntimeBuildMarkers.CleanupActiveAsync(_settings.WslDistro);

}
