namespace LocalLlmConsole.Services;

public sealed record LaunchSettingsRenderActions(
    Func<ModelRecord?> SelectedModel,
    Func<string> SelectedProfileId,
    Action ClearEditor,
    Action<ModelRecord?> UpdateSaveAsNewName,
    Func<ModelRecord, AppSettings, string, CancellationToken, Task<ModelLaunchSettingsViewState>> BuildViewStateAsync,
    Action<ModelLaunchSettingsViewState> LoadEditor,
    Func<string, Task> RefreshRuntimeSelectorAsync,
    Action<AppSettings> ApplyLaunchSettingsToControls,
    Func<ModelRecord?, CancellationToken, Task> ApplyModelCapabilitiesAsync,
    Action UpdateLaunchSaveButtonState);

public sealed class LaunchSettingsRenderApplicationService
{
    public async Task RenderSelectedAsync(
        ModelRecord? model,
        AppSettings settings,
        LaunchSettingsRenderActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(actions);

        if (model is null)
        {
            actions.ClearEditor();
            actions.UpdateSaveAsNewName(null);
            await actions.RefreshRuntimeSelectorAsync("");
            cancellationToken.ThrowIfCancellationRequested();
            actions.ApplyLaunchSettingsToControls(settings);
            await actions.ApplyModelCapabilitiesAsync(null, cancellationToken);
            actions.UpdateLaunchSaveButtonState();
            return;
        }

        var selectedId = model.Id;
        var selectedProfileId = actions.SelectedProfileId();
        actions.UpdateSaveAsNewName(model);
        var viewState = await actions.BuildViewStateAsync(model, settings, selectedProfileId, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (!string.Equals(actions.SelectedModel()?.Id, selectedId, StringComparison.OrdinalIgnoreCase))
            return;
        if (!string.Equals(actions.SelectedProfileId(), selectedProfileId, StringComparison.OrdinalIgnoreCase))
            return;

        actions.LoadEditor(viewState);
        await actions.RefreshRuntimeSelectorAsync(viewState.RuntimeId);
        cancellationToken.ThrowIfCancellationRequested();
        actions.ApplyLaunchSettingsToControls(viewState.LaunchSettings);
        await actions.ApplyModelCapabilitiesAsync(model, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        actions.UpdateLaunchSaveButtonState();
    }
}
