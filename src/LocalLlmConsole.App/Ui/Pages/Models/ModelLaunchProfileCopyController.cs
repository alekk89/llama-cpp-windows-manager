using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public sealed record ModelLaunchProfileCopyControllerActions(
    WpfWindow Owner,
    Func<MainWindowLoadedModelServices> ModelServices,
    Func<IReadOnlyList<RuntimeChoice>> RuntimeChoices,
    Func<StateStore> StateStore,
    Func<AppSettings> Settings,
    Func<string> SelectedLaunchRuntimeId,
    Func<Task> RenderSelectedModelLaunchSettingsAsync,
    Func<Task> RefreshModelsAsync,
    Action<string> SelectLaunchProfileAfterRefresh,
    Func<Task> RefreshOverviewModelSelectorAsync,
    Action<string> SetStatus,
    Func<string, Func<Task>, Task> RunBusyAsync);

public sealed class ModelLaunchProfileCopyController
{
    private readonly ModelLaunchProfileCopyControllerActions _actions;

    public ModelLaunchProfileCopyController(ModelLaunchProfileCopyControllerActions actions)
        => _actions = actions;

    public async Task CopyToAnotherModelAsync(ModelRecord model, NamedModelLaunchProfile profile)
    {
        var result = ModelLaunchProfileCopyDialogFactory.Show(
            _actions.Owner,
            model,
            profile,
            await _actions.ModelServices().Catalog.ListAsync(),
            _actions.RuntimeChoices(),
            _actions.StateStore);
        if (!result.Accepted || result.TargetModel is null)
            return;

        await _actions.ModelServices().LaunchProfileCopy.CopySelectedAsync(
            model,
            result.TargetModel,
            profile,
            result.Name,
            _actions.Settings(),
            new ModelLaunchProfileCopySelectedActions(
                _actions.RunBusyAsync,
                _actions.RenderSelectedModelLaunchSettingsAsync,
                _actions.Settings,
                _actions.SelectedLaunchRuntimeId,
                request => _actions.ModelServices().LaunchVariants.CopyProfileAsync(request),
                new ModelLaunchProfileCopyActions(
                    _actions.RefreshModelsAsync,
                    _actions.SelectLaunchProfileAfterRefresh,
                    _actions.RenderSelectedModelLaunchSettingsAsync,
                    _actions.RefreshOverviewModelSelectorAsync,
                    _actions.SetStatus)));
    }
}
