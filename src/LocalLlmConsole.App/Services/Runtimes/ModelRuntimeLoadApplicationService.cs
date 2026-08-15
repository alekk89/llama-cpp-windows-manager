namespace LocalLlmConsole.Services;

public enum ModelRuntimeLoadApplicationOutcome
{
    Status,
    SwitchedLoaded,
    RenderedLaunchSettings,
    MissingRuntime,
    Started
}

public sealed record SelectedModelRuntimeLoadApplicationRequest(
    ModelRecord? Model,
    bool Restart,
    bool ModelLoaded,
    bool ModelActive,
    bool LaunchSettingsLoaded,
    string SelectedRuntimeId,
    RuntimeRecord? FallbackRuntime);

public sealed record OverviewModelRuntimeLoadApplicationRequest(
    ModelRecord? Model,
    bool ModelLoaded,
    bool ModelActive,
    bool AppReady,
    bool SelectedProfileLoaded);

public sealed record ModelRuntimeLoadApplicationActions(
    Func<string, Func<Task>, Task> RunBusyAsync,
    Func<ModelRecord, Task> SwitchToLoadedModelAsync,
    Func<Task> RenderLaunchSettingsAsync,
    Func<AppSettings> ReadLaunchSettings,
    Func<Task<IReadOnlyList<RuntimeRecord>>> ListRuntimesAsync,
    Func<ModelRecord, Task<ModelLaunchSettings>> DraftModelLaunchProfileAsync,
    Func<ModelRecord, Task> StopModelRuntimeAsync,
    Func<RuntimeRecord, ModelRecord, AppSettings, Task> StartModelRuntimeAsync,
    Action<string> SetStatus);

public sealed class ModelRuntimeLoadApplicationService
{
    private readonly ModelRuntimeCommandDecisionService _commands;
    private readonly LaunchRuntimeSelectionService _runtimeSelection;

    public ModelRuntimeLoadApplicationService(
        ModelRuntimeCommandDecisionService commands,
        LaunchRuntimeSelectionService runtimeSelection)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
        _runtimeSelection = runtimeSelection ?? throw new ArgumentNullException(nameof(runtimeSelection));
    }

    public async Task<ModelRuntimeLoadApplicationOutcome> LoadSelectedAsync(
        SelectedModelRuntimeLoadApplicationRequest request,
        ModelRuntimeLoadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(actions);

        var outcome = ModelRuntimeLoadApplicationOutcome.Status;
        await actions.RunBusyAsync(request.Restart ? "Preparing restart..." : "Preparing model load...", async () =>
        {
            outcome = await LoadSelectedCoreAsync(request, actions);
        });
        return outcome;
    }

    public async Task<ModelRuntimeLoadApplicationOutcome> LoadOverviewAsync(
        OverviewModelRuntimeLoadApplicationRequest request,
        ModelRuntimeLoadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(actions);

        var outcome = ModelRuntimeLoadApplicationOutcome.Status;
        await actions.RunBusyAsync("Preparing model load...", async () =>
        {
            outcome = await LoadOverviewCoreAsync(request, actions);
        });
        return outcome;
    }

    private async Task<ModelRuntimeLoadApplicationOutcome> LoadSelectedCoreAsync(
        SelectedModelRuntimeLoadApplicationRequest request,
        ModelRuntimeLoadApplicationActions actions)
    {
        var command = _commands.PlanSelectedLoad(
            request.Model,
            request.Restart,
            request.ModelLoaded,
            request.ModelActive,
            request.LaunchSettingsLoaded);
        if (await ApplyCommandAsync(command, request.Model, actions))
            return command.Kind == ModelRuntimeLoadCommandKind.SwitchLoaded
                ? ModelRuntimeLoadApplicationOutcome.SwitchedLoaded
                : ModelRuntimeLoadApplicationOutcome.Status;

        if (command.Kind == ModelRuntimeLoadCommandKind.RenderLaunchSettings)
            await actions.RenderLaunchSettingsAsync();

        var model = request.Model!;
        var launchSettings = actions.ReadLaunchSettings();
        var runtimes = await actions.ListRuntimesAsync();
        var runtime = _runtimeSelection.Resolve(runtimes, request.SelectedRuntimeId, request.FallbackRuntime);
        if (runtime is null)
        {
            actions.SetStatus(_runtimeSelection.MissingRuntimeStatus(runtimes, request.SelectedRuntimeId));
            return ModelRuntimeLoadApplicationOutcome.MissingRuntime;
        }

        if (request.Restart)
            await actions.StopModelRuntimeAsync(model);
        await actions.StartModelRuntimeAsync(runtime, model, launchSettings);
        return command.Kind == ModelRuntimeLoadCommandKind.RenderLaunchSettings
            ? ModelRuntimeLoadApplicationOutcome.RenderedLaunchSettings
            : ModelRuntimeLoadApplicationOutcome.Started;
    }

    private async Task<ModelRuntimeLoadApplicationOutcome> LoadOverviewCoreAsync(
        OverviewModelRuntimeLoadApplicationRequest request,
        ModelRuntimeLoadApplicationActions actions)
    {
        var command = _commands.PlanOverviewLoad(
            request.Model,
            request.ModelLoaded,
            request.ModelActive,
            request.AppReady,
            request.SelectedProfileLoaded);
        if (await ApplyCommandAsync(command, request.Model, actions))
            return command.Kind == ModelRuntimeLoadCommandKind.SwitchLoaded
                ? ModelRuntimeLoadApplicationOutcome.SwitchedLoaded
                : ModelRuntimeLoadApplicationOutcome.Status;

        var model = request.Model!;
        var profile = await actions.DraftModelLaunchProfileAsync(model);
        var runtimes = await actions.ListRuntimesAsync();
        var runtime = _runtimeSelection.Resolve(runtimes, profile.RuntimeId);
        if (runtime is null)
        {
            actions.SetStatus(_runtimeSelection.MissingRuntimeStatus(runtimes, profile.RuntimeId));
            return ModelRuntimeLoadApplicationOutcome.MissingRuntime;
        }

        if (request.ModelLoaded)
            await actions.StopModelRuntimeAsync(model);
        await actions.StartModelRuntimeAsync(runtime, model, profile.ApplyTo(actions.ReadLaunchSettings()));
        return ModelRuntimeLoadApplicationOutcome.Started;
    }

    private static async Task<bool> ApplyCommandAsync(
        ModelRuntimeLoadCommand command,
        ModelRecord? model,
        ModelRuntimeLoadApplicationActions actions)
    {
        if (command.Kind == ModelRuntimeLoadCommandKind.Status)
        {
            actions.SetStatus(command.StatusMessage);
            return true;
        }

        if (command.Kind == ModelRuntimeLoadCommandKind.SwitchLoaded)
        {
            await actions.SwitchToLoadedModelAsync(model!);
            return true;
        }

        return false;
    }

    private static void Validate(ModelRuntimeLoadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RunBusyAsync);
        ArgumentNullException.ThrowIfNull(actions.SwitchToLoadedModelAsync);
        ArgumentNullException.ThrowIfNull(actions.RenderLaunchSettingsAsync);
        ArgumentNullException.ThrowIfNull(actions.ReadLaunchSettings);
        ArgumentNullException.ThrowIfNull(actions.ListRuntimesAsync);
        ArgumentNullException.ThrowIfNull(actions.DraftModelLaunchProfileAsync);
        ArgumentNullException.ThrowIfNull(actions.StopModelRuntimeAsync);
        ArgumentNullException.ThrowIfNull(actions.StartModelRuntimeAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
