using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed record ModelsPageActionControllerActions(
    Func<Task> ScanModelsFolderAsync,
    Func<Task> ChooseModelsFolderAsync,
    Action OpenModelsFolder,
    Func<Task> ManageModelGroupsAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> AssignLaunchProfileGroupAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> RemoveLaunchProfileGroupAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> LoadLaunchProfileAsync,
    Action BeginNewLaunchProfile,
    Action<DataGrid, DataGrid?> SelectModelGridRow,
    ModelsPageRowActionController RowActions,
    Func<Task> SearchHuggingFaceAsync,
    Func<Task> ShowDownloadHistoryAsync,
    Action<DataGrid> ConfigureModelGridColumnSizing);

public sealed class ModelsPageActionController
{
    private readonly ModelsPageActionControllerActions _actions;

    public ModelsPageActionController(ModelsPageActionControllerActions actions)
    {
        _actions = actions;
    }

    public ModelsPageActions Build()
        => new(
            _actions.ScanModelsFolderAsync,
            _actions.ChooseModelsFolderAsync,
            _actions.OpenModelsFolder,
            _actions.ManageModelGroupsAsync,
            _actions.AssignLaunchProfileGroupAsync,
            _actions.RemoveLaunchProfileGroupAsync,
            _actions.LoadLaunchProfileAsync,
            _actions.BeginNewLaunchProfile,
            _actions.SelectModelGridRow,
            _actions.RowActions.OpenModelFolderRow_Click,
            _actions.RowActions.DeleteModelRow_Click,
            _actions.SearchHuggingFaceAsync,
            _actions.ShowDownloadHistoryAsync,
            _actions.ConfigureModelGridColumnSizing);
}
