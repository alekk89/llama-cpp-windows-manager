using System.Windows;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task ImportModelFileAsync()
    {
        await _coreServices.Models.ModelImportApplication.ChooseAndImportAsync(
            _settings.ModelsRoot,
            new ModelImportApplicationActions(
                request => _coreServices.App.FileSystemDialogs.PickOpenFile(request, this),
                confirmation => _coreServices.App.Dialogs.Confirm(
                    this,
                    confirmation.Message,
                    confirmation.Title,
                    MessageBoxImage.Warning),
                async (path, confirmRole) =>
                {
                    var catalog = ModelServices.Catalog;
                    Require(catalog);
                    return await catalog!.ImportFileAsync(path, confirmRole);
                },
                async model =>
                {
                    var launchProfiles = ModelServices.LaunchProfiles;
                    Require(launchProfiles);
                    await launchProfiles!.EnsureDefaultAsync(model, _settings);
                },
                RunAsync,
                async () =>
                {
                    await RefreshModelsAsync();
                    await RefreshOverviewModelSelectorAsync();
                },
                SetStatus));
    }

    private void ShowModelScanDiagnostics(ModelScanResult result)
    {
        var review = result.Files
            .Where(file => file.Role is GgufFileRole.Ambiguous or GgufFileRole.Invalid)
            .Take(12)
            .ToArray();
        if (review.Length == 0) return;

        var details = string.Join(
            Environment.NewLine,
            review.Select(file => $"• {Path.GetFileName(file.Path)}: {file.Reason}"));
        var remaining = result.AmbiguousCount + result.InvalidCount - review.Length;
        if (remaining > 0)
            details += $"{Environment.NewLine}• {Loc.T("Models.ScanDiagnostics.MoreFiles", remaining)}";

        _coreServices.App.Dialogs.Notify(
            this,
            Loc.T("Models.ScanDiagnostics.Message", details),
            Loc.T("Models.ScanDiagnostics.Title"),
            MessageBoxImage.Warning);
    }
}
