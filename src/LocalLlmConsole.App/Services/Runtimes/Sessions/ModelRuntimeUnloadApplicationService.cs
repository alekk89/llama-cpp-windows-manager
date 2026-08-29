namespace LocalLlmConsole.Services;

public enum ModelRuntimeUnloadApplicationOutcome
{
    Status,
    Stopped
}

public sealed record ModelRuntimeUnloadApplicationRequest(
    ModelRecord? Model,
    bool ModelLoaded);

public sealed record ModelRuntimeUnloadApplicationActions(
    Func<ModelRecord, Task> StopModelRuntimeAsync,
    Action<string> SetStatus);

public sealed class ModelRuntimeUnloadApplicationService
{
    private readonly ModelRuntimeCommandDecisionService _commands;

    public ModelRuntimeUnloadApplicationService(ModelRuntimeCommandDecisionService commands)
    {
        _commands = commands ?? throw new ArgumentNullException(nameof(commands));
    }

    public async Task<ModelRuntimeUnloadApplicationOutcome> UnloadOverviewAsync(
        ModelRuntimeUnloadApplicationRequest request,
        ModelRuntimeUnloadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(actions);

        return await ApplyAsync(
            _commands.PlanOverviewUnload(request.Model, request.ModelLoaded),
            request.Model,
            actions);
    }

    private static async Task<ModelRuntimeUnloadApplicationOutcome> ApplyAsync(
        ModelRuntimeUnloadCommand command,
        ModelRecord? model,
        ModelRuntimeUnloadApplicationActions actions)
    {
        if (command.Kind == ModelRuntimeUnloadCommandKind.Status)
        {
            actions.SetStatus(ModelRuntimeCommandLocalization.Message(command.Status));
            return ModelRuntimeUnloadApplicationOutcome.Status;
        }

        await actions.StopModelRuntimeAsync(model!);
        return ModelRuntimeUnloadApplicationOutcome.Stopped;
    }

    private static void Validate(ModelRuntimeUnloadApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.StopModelRuntimeAsync);
        ArgumentNullException.ThrowIfNull(actions.SetStatus);
    }
}
