namespace LocalLlmConsole.Services;

public enum ModelReattachApplicationOutcome
{
    Cancelled,
    Blocked,
    Reattached
}

public sealed record ModelReattachIdentityConfirmation(
    string Title,
    string Message,
    ModelReattachInspection Inspection);

public sealed record ModelReattachApplicationActions(
    Func<OpenFilePickerRequest, string?> PickFile,
    Func<ModelImportRoleConfirmation, bool> ConfirmRoleOverride,
    Func<ModelReattachIdentityConfirmation, bool> ConfirmIdentityMismatch,
    Func<ModelRecord, bool> IsModelLoaded,
    Func<ModelRecord, string, Task<ModelReattachInspection>> InspectAsync,
    Func<ModelRecord, ModelReattachInspection, bool, bool, Task<ModelReattachResult>> ReattachAsync,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshModelsAsync,
    Func<Task> RefreshOverviewAsync,
    Action<string> SetStatus);

public sealed class ModelReattachApplicationService
{
    public async Task<ModelReattachApplicationOutcome> ChooseAndReattachAsync(
        ModelRecord? model,
        string modelsRoot,
        ModelReattachApplicationActions actions)
    {
        Validate(actions);
        if (model is null) return ModelReattachApplicationOutcome.Blocked;
        if (File.Exists(model.ModelPath))
        {
            actions.SetStatus(Loc.T("Models.Reattach.NotMissingStatus", model.Name));
            return ModelReattachApplicationOutcome.Blocked;
        }
        if (actions.IsModelLoaded(model))
        {
            actions.SetStatus(Loc.T("Models.Reattach.LoadedStatus", model.Name));
            return ModelReattachApplicationOutcome.Blocked;
        }

        var selected = actions.PickFile(BuildPickerRequest(model, modelsRoot));
        if (string.IsNullOrWhiteSpace(selected)) return ModelReattachApplicationOutcome.Cancelled;

        var inspection = await actions.InspectAsync(model, selected);
        if (inspection.DuplicateModels.Any(actions.IsModelLoaded))
        {
            actions.SetStatus(Loc.T("Models.Reattach.TargetLoadedStatus"));
            return ModelReattachApplicationOutcome.Blocked;
        }

        var confirmRole = inspection.Classification.Role != GgufFileRole.MainModel;
        if (confirmRole && !actions.ConfirmRoleOverride(ModelImportApplicationService.BuildConfirmation(inspection.Classification)))
            return ModelReattachApplicationOutcome.Cancelled;

        var confirmIdentityMismatch = inspection.RequiresIdentityConfirmation;
        if (confirmIdentityMismatch && !actions.ConfirmIdentityMismatch(BuildIdentityConfirmation(inspection)))
            return ModelReattachApplicationOutcome.Cancelled;

        var completed = false;
        await actions.RunBusyAsync(Loc.T("Models.Reattach.Busy"), async () =>
        {
            var result = await actions.ReattachAsync(model, inspection, confirmRole, confirmIdentityMismatch);
            await actions.RefreshModelsAsync();
            await actions.RefreshOverviewAsync();
            actions.SetStatus(result.RemovedDuplicateRecords > 0
                ? Loc.T("Models.Reattach.CompletedMergedStatus", result.Model.Name, result.RemovedDuplicateRecords)
                : Loc.T("Models.Reattach.CompletedStatus", result.Model.Name));
            completed = true;
        });
        return completed ? ModelReattachApplicationOutcome.Reattached : ModelReattachApplicationOutcome.Blocked;
    }

    public static OpenFilePickerRequest BuildPickerRequest(ModelRecord model, string modelsRoot)
    {
        ArgumentNullException.ThrowIfNull(model);
        var previousFolder = Path.GetDirectoryName(model.ModelPath) ?? "";
        var initialDirectory = Directory.Exists(previousFolder) ? previousFolder : modelsRoot;
        return new OpenFilePickerRequest(
            Loc.T("Models.Reattach.PickerTitle", model.Name),
            Loc.T("Models.Import.FileFilter"),
            CheckFileExists: true,
            AddExtension: false,
            DefaultExt: ".gguf",
            FileName: Path.GetFileName(model.ModelPath),
            InitialDirectory: FileSystemDialogService.ExistingDirectoryOrEmpty(initialDirectory));
    }

    public static ModelReattachIdentityConfirmation BuildIdentityConfirmation(ModelReattachInspection inspection)
        => new(
            Loc.T("Models.Reattach.IdentityTitle"),
            Loc.T("Models.Reattach.IdentityMessage", string.Join(Environment.NewLine, inspection.IdentityWarnings.Select(warning => $"• {warning}"))),
            inspection);

    private static void Validate(ModelReattachApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.PickFile);
        ArgumentNullException.ThrowIfNull(actions.ConfirmRoleOverride);
        ArgumentNullException.ThrowIfNull(actions.ConfirmIdentityMismatch);
        ArgumentNullException.ThrowIfNull(actions.IsModelLoaded);
        ArgumentNullException.ThrowIfNull(actions.InspectAsync);
        ArgumentNullException.ThrowIfNull(actions.ReattachAsync);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshModelsAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
