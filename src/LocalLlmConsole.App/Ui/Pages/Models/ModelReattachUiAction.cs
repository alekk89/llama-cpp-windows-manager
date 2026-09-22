using System.Windows;

namespace LocalLlmConsole;

public static class ModelReattachUiAction
{
    public static async Task RunAsync(
        ModelGridRow row,
        Window owner,
        string modelsRoot,
        ModelReattachApplicationService application,
        FileSystemDialogService fileDialogs,
        DialogService dialogs,
        Func<ModelCatalogService> catalog,
        Func<ModelRecord, bool> isModelActive,
        Func<string, Func<Task>, Task> runBusyAsync,
        Func<Task> refreshModelsAsync,
        Func<Task> refreshOverviewAsync,
        Action<string> setStatus)
    {
        await application.ChooseAndReattachAsync(
            row.Model,
            modelsRoot,
            new ModelReattachApplicationActions(
                request => fileDialogs.PickOpenFile(request, owner),
                confirmation => dialogs.Confirm(owner, confirmation.Message, confirmation.Title, MessageBoxImage.Warning),
                confirmation => dialogs.Confirm(owner, confirmation.Message, confirmation.Title, MessageBoxImage.Warning),
                isModelActive,
                (model, path) => catalog().InspectReattachFileAsync(model, path),
                (model, inspection, confirmRole, confirmIdentityMismatch) => catalog().ReattachFileAsync(model, inspection, confirmRole, confirmIdentityMismatch),
                runBusyAsync,
                refreshModelsAsync,
                refreshOverviewAsync,
                setStatus));
    }
}
