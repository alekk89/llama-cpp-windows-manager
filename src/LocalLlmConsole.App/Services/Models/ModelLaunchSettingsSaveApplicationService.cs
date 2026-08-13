namespace LocalLlmConsole.Services;

public sealed record ModelLaunchProfileSaveApplicationRequest(
    string ModelId,
    string ProfileId,
    ModelLaunchSettingsSaveResult Result);

public enum ModelLaunchProfileSaveApplicationOutcome
{
    NoModelSelected,
    Saved
}

public enum LaunchDefaultsSaveApplicationOutcome
{
    Saved
}

public sealed record ModelLaunchProfileSaveActions(
    Action<string, string, ModelLaunchSettings> MarkSaved,
    Action UpdateLaunchSaveButtonState,
    Action<string> SetStatus);

public sealed record ModelLaunchProfileSaveSelectedActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<string, string, bool> IsEditorLoadedForModel,
    Func<string> SelectedProfileId,
    Func<Task> RenderSelectedModelLaunchSettingsAsync,
    Func<AppSettings> ReadLaunchSettings,
    Func<ModelRecord, AppSettings, string, Task<ModelLaunchSettingsSaveResult>> SaveProfileAsync,
    ModelLaunchProfileSaveActions ResultActions);

public sealed record LaunchDefaultsSaveActions(
    Action<AppSettings> SetSettings,
    Func<Task> PersistSettingsAsync,
    Action UpdateLaunchSaveButtonState,
    Action<string> SetStatus);

public sealed record LaunchDefaultsSaveFromControlsActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<AppSettings> ReadLaunchSettings,
    Func<AppSettings, LaunchDefaultsSaveResult> SaveDefaults,
    LaunchDefaultsSaveActions ResultActions);

public sealed class ModelLaunchSettingsSaveApplicationService
{
    public async Task<ModelLaunchProfileSaveApplicationOutcome> SaveSelectedProfileAsync(
        ModelRecord? model,
        ModelLaunchProfileSaveSelectedActions actions)
    {
        Validate(actions);

        if (model is null)
        {
            actions.ResultActions.SetStatus("Select a model before saving launch settings.");
            return ModelLaunchProfileSaveApplicationOutcome.NoModelSelected;
        }

        await actions.RunBusyAsync("Saving model launch profile...", async () =>
        {
            var profileId = actions.SelectedProfileId();
            if (!actions.IsEditorLoadedForModel(model.Id, profileId))
                await actions.RenderSelectedModelLaunchSettingsAsync();

            var launchSettings = actions.ReadLaunchSettings();
            var result = await actions.SaveProfileAsync(model, launchSettings, profileId);
            ApplyProfileSave(
                new ModelLaunchProfileSaveApplicationRequest(model.Id, profileId, result),
                actions.ResultActions);
        });

        return ModelLaunchProfileSaveApplicationOutcome.Saved;
    }

    public void ApplyProfileSave(
        ModelLaunchProfileSaveApplicationRequest request,
        ModelLaunchProfileSaveActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);

        actions.MarkSaved(request.ModelId, request.ProfileId, request.Result.SavedSettings);
        actions.UpdateLaunchSaveButtonState();
        actions.SetStatus(request.Result.StatusMessage);
    }

    public async Task<LaunchDefaultsSaveApplicationOutcome> SaveDefaultsFromControlsAsync(
        LaunchDefaultsSaveFromControlsActions actions)
    {
        Validate(actions);

        await actions.RunBusyAsync("Saving launch defaults...", async () =>
        {
            var launchDefaults = actions.ReadLaunchSettings();
            var result = actions.SaveDefaults(launchDefaults);
            await ApplyDefaultsSaveAsync(result, actions.ResultActions);
        });

        return LaunchDefaultsSaveApplicationOutcome.Saved;
    }

    public async Task ApplyDefaultsSaveAsync(
        LaunchDefaultsSaveResult result,
        LaunchDefaultsSaveActions actions)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(actions);

        actions.SetSettings(result.Settings);
        await actions.PersistSettingsAsync();
        actions.UpdateLaunchSaveButtonState();
        actions.SetStatus(result.StatusMessage);
    }

    private static void Validate(ModelLaunchProfileSaveSelectedActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.IsEditorLoadedForModel);
        ArgumentNullException.ThrowIfNull(actions.SelectedProfileId);
        ArgumentNullException.ThrowIfNull(actions.RenderSelectedModelLaunchSettingsAsync);
        ArgumentNullException.ThrowIfNull(actions.ReadLaunchSettings);
        ArgumentNullException.ThrowIfNull(actions.SaveProfileAsync);
        Validate(actions.ResultActions);
    }

    private static void Validate(ModelLaunchProfileSaveActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.MarkSaved);
        ArgumentNullException.ThrowIfNull(actions.UpdateLaunchSaveButtonState);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }

    private static void Validate(LaunchDefaultsSaveFromControlsActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.ReadLaunchSettings);
        ArgumentNullException.ThrowIfNull(actions.SaveDefaults);
        Validate(actions.ResultActions);
    }

    private static void Validate(LaunchDefaultsSaveActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.SetSettings);
        ArgumentNullException.ThrowIfNull(actions.PersistSettingsAsync);
        ArgumentNullException.ThrowIfNull(actions.UpdateLaunchSaveButtonState);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
