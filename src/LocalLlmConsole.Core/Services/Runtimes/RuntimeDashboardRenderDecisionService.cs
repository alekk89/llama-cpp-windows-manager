namespace LocalLlmConsole.Services;

public enum RuntimeDashboardRenderDecisionKind
{
    NoRuntime,
    MetricsDisabled,
    FreshMetrics,
    MetricsUnavailable
}

public sealed record RuntimeDashboardRenderDecisionRequest(
    LoadedModelSessionSnapshot? SelectedSession,
    AppSettings MetricsSettings,
    RuntimeMetricPollResult? SelectedPollResult);

public sealed record RuntimeDashboardRenderDecision(
    RuntimeDashboardRenderDecisionKind Kind,
    RuntimeSlotSnapshot? SlotSnapshot,
    IReadOnlyList<PrometheusSample> Samples,
    string Error);

public sealed class RuntimeDashboardRenderDecisionService
{
    public RuntimeDashboardRenderDecision Decide(RuntimeDashboardRenderDecisionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.MetricsSettings);

        var slotSnapshot = request.SelectedPollResult?.SlotSnapshot;
        if (request.SelectedSession is not { IsRunning: true })
            return new RuntimeDashboardRenderDecision(
                RuntimeDashboardRenderDecisionKind.NoRuntime,
                slotSnapshot,
                [],
                "");

        if (!request.MetricsSettings.EnableMetrics)
            return new RuntimeDashboardRenderDecision(
                RuntimeDashboardRenderDecisionKind.MetricsDisabled,
                slotSnapshot,
                [],
                "");

        if (request.SelectedPollResult is { Samples.Count: > 0 } pollResult)
            return new RuntimeDashboardRenderDecision(
                RuntimeDashboardRenderDecisionKind.FreshMetrics,
                pollResult.SlotSnapshot,
                pollResult.Samples,
                "");

        return new RuntimeDashboardRenderDecision(
            RuntimeDashboardRenderDecisionKind.MetricsUnavailable,
            slotSnapshot,
            [],
            string.IsNullOrWhiteSpace(request.SelectedPollResult?.Error)
                ? "No metrics response."
                : request.SelectedPollResult.Error);
    }
}
