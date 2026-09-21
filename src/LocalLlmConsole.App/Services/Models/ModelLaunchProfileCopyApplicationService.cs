namespace LocalLlmConsole.Services;

public sealed record ModelLaunchProfileCopyApplicationRequest(
    ModelLaunchVariantWorkflowResult Result);

public enum ModelLaunchProfileCopyApplicationOutcome
{
    NoModelSelected,
    NoTargetModel,
    Failed,
    Saved
}

public sealed record ModelLaunchProfileCopyActions(
    Func<Task> RefreshModelsAsync,
    Action<string> SelectLaunchProfileAfterRefresh,
    Func<Task> RenderSelectedModelLaunchSettingsAsync,
    Func<Task> RefreshOverviewModelSelectorAsync,
    Action<string> SetStatus);

public sealed record ModelLaunchProfileCopySelectedActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<Task> RenderSelectedModelLaunchSettingsAsync,
    Func<AppSettings> ReadLaunchSettings,
    Func<string> SelectedRuntimeId,
    Func<ModelLaunchProfileCopyRequest, Task<ModelLaunchVariantWorkflowResult>> CopyProfileAsync,
    ModelLaunchProfileCopyActions ResultActions);

public sealed class ModelLaunchProfileCopyApplicationService
{
    public async Task<ModelLaunchProfileCopyApplicationOutcome> CopySelectedAsync(
        ModelRecord? source,
        ModelRecord? target,
        NamedModelLaunchProfile? sourceProfile,
        string requestedName,
        AppSettings settings,
        ModelLaunchProfileCopySelectedActions actions)
    {
        Validate(actions);

        if (source is null)
        {
            actions.ResultActions.SetStatus("Select a model before copying a launch profile.");
            return ModelLaunchProfileCopyApplicationOutcome.NoModelSelected;
        }

        if (target is null || sourceProfile is null)
        {
            actions.ResultActions.SetStatus("Choose a target model to copy the launch profile to.");
            return ModelLaunchProfileCopyApplicationOutcome.NoTargetModel;
        }

        var saved = false;
        await actions.RunBusyAsync("Copying launch profile...", async () =>
        {
            var result = await actions.CopyProfileAsync(new ModelLaunchProfileCopyRequest(
                source!,
                target!,
                sourceProfile!,
                requestedName,
                actions.SelectedRuntimeId(),
                settings));
            saved = await ApplyAsync(
                new ModelLaunchProfileCopyApplicationRequest(result),
                actions.ResultActions);
        });

        return saved
            ? ModelLaunchProfileCopyApplicationOutcome.Saved
            : ModelLaunchProfileCopyApplicationOutcome.Failed;
    }

    public async Task<bool> ApplyAsync(
        ModelLaunchProfileCopyApplicationRequest request,
        ModelLaunchProfileCopyActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);

        var result = request.Result ?? throw new ArgumentNullException(nameof(request.Result));
        if (!result.Success || result.Profile is null)
        {
            actions.SetStatus(result.StatusMessage);
            return false;
        }

        await actions.RefreshModelsAsync();
        actions.SelectLaunchProfileAfterRefresh(result.Profile.Id);
        await actions.RenderSelectedModelLaunchSettingsAsync();
        await actions.RefreshOverviewModelSelectorAsync();
        actions.SetStatus(result.StatusMessage);
        return true;
    }

    private static void Validate(ModelLaunchProfileCopySelectedActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.RenderSelectedModelLaunchSettingsAsync);
        ArgumentNullException.ThrowIfNull(actions.ReadLaunchSettings);
        ArgumentNullException.ThrowIfNull(actions.SelectedRuntimeId);
        ArgumentNullException.ThrowIfNull(actions.CopyProfileAsync);
        Validate(actions.ResultActions);
    }

    private static void Validate(ModelLaunchProfileCopyActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RefreshModelsAsync);
        ArgumentNullException.ThrowIfNull(actions.SelectLaunchProfileAfterRefresh);
        ArgumentNullException.ThrowIfNull(actions.RenderSelectedModelLaunchSettingsAsync);
        ArgumentNullException.ThrowIfNull(actions.RefreshOverviewModelSelectorAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
