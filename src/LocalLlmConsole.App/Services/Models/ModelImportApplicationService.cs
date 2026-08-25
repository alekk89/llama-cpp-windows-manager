namespace LocalLlmConsole.Services;

public enum ModelImportApplicationOutcome
{
    Cancelled,
    Blocked,
    Imported
}

public sealed record ModelImportRoleConfirmation(
    string Title,
    string Message,
    GgufFileClassification Classification);

public sealed record ModelImportApplicationActions(
    Func<OpenFilePickerRequest, string?> PickFile,
    Func<ModelImportRoleConfirmation, bool> ConfirmRoleOverride,
    Func<string, bool, Task<ModelRecord>> ImportFileAsync,
    Func<ModelRecord, Task> EnsureDefaultProfileAsync,
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RefreshAsync,
    Action<string> SetStatus);

public sealed class ModelImportApplicationService
{
    public async Task<ModelImportApplicationOutcome> ChooseAndImportAsync(
        string modelsRoot,
        ModelImportApplicationActions actions)
    {
        Validate(actions);
        var selected = actions.PickFile(BuildPickerRequest(modelsRoot));
        if (string.IsNullOrWhiteSpace(selected)) return ModelImportApplicationOutcome.Cancelled;

        var classification = await Task.Run(() => ModelCatalogService.ClassifyGguf(selected));
        if (classification.Role == GgufFileRole.Invalid)
        {
            actions.SetStatus(classification.Reason);
            return ModelImportApplicationOutcome.Blocked;
        }

        var confirmRole = classification.Role != GgufFileRole.MainModel;
        if (confirmRole && !actions.ConfirmRoleOverride(BuildConfirmation(classification)))
            return ModelImportApplicationOutcome.Cancelled;

        var imported = false;
        await actions.RunBusyAsync(Loc.T("Models.Import.Busy"), async () =>
        {
            var model = await actions.ImportFileAsync(selected, confirmRole);
            await actions.EnsureDefaultProfileAsync(model);
            await actions.RefreshAsync();
            actions.SetStatus(Loc.T("Models.Import.AddedStatus", model.Name));
            imported = true;
        });
        return imported ? ModelImportApplicationOutcome.Imported : ModelImportApplicationOutcome.Blocked;
    }

    public static OpenFilePickerRequest BuildPickerRequest(string modelsRoot)
        => new(
            Loc.T("Models.Import.PickerTitle"),
            Loc.T("Models.Import.FileFilter"),
            CheckFileExists: true,
            AddExtension: false,
            DefaultExt: ".gguf",
            FileName: "",
            InitialDirectory: FileSystemDialogService.ExistingDirectoryOrEmpty(modelsRoot));

    public static ModelImportRoleConfirmation BuildConfirmation(GgufFileClassification classification)
        => new(
            Loc.T("Models.Import.ConfirmationTitle"),
            Loc.T("Models.Import.ConfirmationMessage", RoleLabel(classification.Role), classification.Reason),
            classification);

    private static string RoleLabel(GgufFileRole role)
        => role switch
        {
            GgufFileRole.MainModel => Loc.T("Models.Import.Role.MainModel"),
            GgufFileRole.VisionProjector => Loc.T("Models.Import.Role.VisionProjector"),
            GgufFileRole.SpeculativeAssistant => Loc.T("Models.Import.Role.SpeculativeAssistant"),
            GgufFileRole.Ambiguous => Loc.T("Models.Import.Role.Ambiguous"),
            GgufFileRole.Invalid => Loc.T("Models.Import.Role.Invalid"),
            _ => role.ToString()
        };

    private static void Validate(ModelImportApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.PickFile);
        ArgumentNullException.ThrowIfNull(actions.ConfirmRoleOverride);
        ArgumentNullException.ThrowIfNull(actions.ImportFileAsync);
        ArgumentNullException.ThrowIfNull(actions.EnsureDefaultProfileAsync);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
