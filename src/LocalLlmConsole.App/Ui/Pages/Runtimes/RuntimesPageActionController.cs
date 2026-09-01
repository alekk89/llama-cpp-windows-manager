using System.Windows.Controls;
using System.Windows.Input;

namespace LocalLlmConsole;

public sealed record RuntimesPageActionControllerActions(
    Func<Task> ChooseRuntimeFolderAsync,
    Func<Task> ChangeCudaPackagePreferenceAsync,
    Func<RuntimeRecord, Task> ToggleRuntimeFavoriteAsync,
    RuntimesPageRowActionController RowActions,
    Action<DataGrid> ConfigureRuntimeGridColumnSizing,
    Action<DataGrid> ConfigureRuntimeBuildGridColumnSizing);

public sealed class RuntimesPageActionController
{
    private readonly RuntimesPageActionControllerActions _actions;

    public RuntimesPageActionController(RuntimesPageActionControllerActions actions)
    {
        _actions = actions;
    }

    public RuntimesPageActions Build()
        => new(
            _actions.ChooseRuntimeFolderAsync,
            _actions.ChangeCudaPackagePreferenceAsync,
            _actions.ToggleRuntimeFavoriteAsync,
            _actions.RowActions.VerifyRuntimeRow_Click,
            _actions.RowActions.DeleteRuntimeRow_Click,
            _actions.RowActions.RuntimeSourceRow_Click,
            _actions.RowActions.InstallRuntimePackageRow_Click,
            _actions.RowActions.CheckRuntimePackageUpdateRow_Click,
            _actions.RowActions.DeleteRuntimePackageRow_Click,
            _actions.ConfigureRuntimeGridColumnSizing,
            _actions.ConfigureRuntimeBuildGridColumnSizing);
}
