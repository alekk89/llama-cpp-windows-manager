namespace LocalLlmConsole.Services;

public sealed record RuntimeStopDecision(
    string ReadinessMonitorModelId,
    bool StopLoadingStatus,
    bool ResetMetricCounters,
    string StatusMessage);

public sealed record RuntimeSwitchDecision(
    bool Selected,
    bool ResetMetricCounters,
    bool StartDashboardRefresh,
    string StatusMessage);

public sealed class RuntimeSessionActionDecisionService
{
    public RuntimeStopDecision StopSelected(LoadedModelSessionSnapshot? selectedSession, bool selectedModelIsLoading)
    {
        var selectedModelId = selectedSession?.ModelId ?? "";
        return new RuntimeStopDecision(
            selectedModelId,
            string.IsNullOrWhiteSpace(selectedModelId) || selectedModelIsLoading,
            ResetMetricCounters: true,
            "Runtime stopped.");
    }

    public RuntimeStopDecision StopModel(ModelRecord model, bool modelIsSelected, bool modelIsLoading)
    {
        ArgumentNullException.ThrowIfNull(model);

        return new RuntimeStopDecision(
            model.Id,
            modelIsLoading,
            modelIsSelected,
            $"Unloaded {model.Name}.");
    }

    public RuntimeSwitchDecision SwitchToModel(ModelRecord model, bool selected)
    {
        ArgumentNullException.ThrowIfNull(model);

        return selected
            ? new RuntimeSwitchDecision(true, ResetMetricCounters: false, StartDashboardRefresh: true, $"Selected loaded model {model.Name}.")
            : new RuntimeSwitchDecision(false, ResetMetricCounters: false, StartDashboardRefresh: false, $"{model.Name} is not loaded.");
    }
}
