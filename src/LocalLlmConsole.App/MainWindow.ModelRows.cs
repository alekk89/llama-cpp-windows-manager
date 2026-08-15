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
    private async Task DeleteModelRowAsync(ModelGridRow row)
    {
        if (row.LaunchProfile is null)
        {
            await DeleteModelAsync(row.Model);
            return;
        }

        var profile = row.LaunchProfile;
        if (!row.CanDelete)
        {
            SetStatus("This model must keep at least one launch profile. Add another profile before removing this one, and unload the model first if it is active.");
            return;
        }
        var confirmed = _coreServices.App.Dialogs.Confirm(
            this,
            $"Remove launch profile '{profile.Name}' from {row.Model.Name}? The model file will be kept.",
            "Remove launch profile",
            MessageBoxImage.Warning);
        if (!confirmed) return;

        var launchProfiles = ModelServices.LaunchProfiles;
        Require(launchProfiles);
        await RunAsync("Removing launch profile...", async () =>
        {
            var promoted = await launchProfiles!.DeleteNamedAsync(profile.Id);
            await RefreshModelsAsync();
            SetStatus(promoted is null
                ? $"Removed launch profile {profile.Name}. The model file was kept."
                : $"Removed launch profile {profile.Name}. {promoted.Name} is now the default; the model file was kept.");
        });
    }

    private async Task DeleteModelAsync(ModelRecord model)
        => await _coreServices.Models.ModelDeletionApplication.DeleteAsync(model, _settings.ModelsRoot, ModelDeletionActions());

    private ModelDeletionApplicationActions ModelDeletionActions()
        => new(
            IsModelLoaded,
            ConfirmModelDeletion,
            RunAsync,
            DeleteModelFromCatalogAsync,
            RefreshModelsAsync,
            RefreshOverviewAsync,
            SetStatus);

    private bool ConfirmModelDeletion(ModelDeletionConfirmation confirmation)
        => _coreServices.App.Dialogs.Confirm(
            this,
            confirmation.Message,
            confirmation.Title,
            MessageBoxImage.Warning);

    private async Task DeleteModelFromCatalogAsync(ModelRecord model, string modelsRoot)
    {
        var catalog = ModelServices.Catalog;
        Require(catalog);
        await catalog!.DeleteAsync(model, modelsRoot);
    }

    private ModelFolderApplicationActions ModelFolderActions()
        => new(
            path => _coreServices.App.ShellIntegration.OpenFolder(path),
            SetStatus);
}
