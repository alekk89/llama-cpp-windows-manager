namespace LocalLlmConsole.Services;

public sealed record RuntimeSwitchCommandResult(
    RuntimeSwitchDecision Decision,
    AppSettings? ActiveSettings);

public sealed class RuntimeSessionCommandService
{
    private readonly RuntimeSessionCoordinator _runtimeSessions;
    private readonly RuntimeSessionActionDecisionService _decisions;

    public RuntimeSessionCommandService(
        RuntimeSessionCoordinator runtimeSessions,
        RuntimeSessionActionDecisionService decisions)
    {
        _runtimeSessions = runtimeSessions ?? throw new ArgumentNullException(nameof(runtimeSessions));
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
    }

    public RuntimeStopDecision PlanStopSelected(
        LoadedModelSessionSnapshot? selectedSession,
        bool selectedModelIsLoading)
        => _decisions.StopSelected(selectedSession, selectedModelIsLoading);

    public RuntimeStopDecision PlanStopModel(
        ModelRecord model,
        bool modelIsSelected,
        bool modelIsLoading)
        => _decisions.StopModel(model, modelIsSelected, modelIsLoading);

    public Task<LoadedModelSessionSnapshot> StartModelAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings launchSettings,
        string launchProfileId = "",
        string launchProfileName = "")
        => _runtimeSessions.StartAsync(runtime, model, launchSettings, launchProfileId, launchProfileName);

    public Task<RuntimeSessionStopResult> StopSelectedAsync()
        => _runtimeSessions.StopSelectedAsync();

    public Task<RuntimeSessionStopResult> StopModelAsync(string modelId)
        => _runtimeSessions.StopModelAsync(modelId);

    public RuntimeSwitchCommandResult SwitchToModel(ModelRecord model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var selection = _runtimeSessions.SelectModel(model.Id);
        return new RuntimeSwitchCommandResult(
            _decisions.SwitchToModel(model, selection.Selected),
            selection.ActiveSettings);
    }
}
