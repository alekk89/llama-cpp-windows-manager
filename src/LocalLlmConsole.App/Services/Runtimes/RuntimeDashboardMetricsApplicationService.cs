namespace LocalLlmConsole.Services;

public sealed record RuntimeMetricSummaryPresentation(
    string Tokens,
    string MtpTokens,
    string Slots,
    string KvCache,
    DateTimeOffset? LastKnownCapturedAt,
    RuntimeMetricGraphSample GraphSample,
    IReadOnlyList<PrometheusSample> Samples,
    RuntimeMetricAtomicSnapshot? Atomic = null)
{
    public static RuntimeMetricSummaryPresentation NoRuntime { get; } = new(
        "No runtime",
        "Inactive",
        "Active 0/0 | Queued 0\nBusy/decode 0.0",
        "Used Unknown\nCapacity Unknown",
        LastKnownCapturedAt: null,
        new RuntimeMetricGraphSample("", null, null, null, null, null),
        [],
        RuntimeMetricAtomicSnapshot.Empty);
}

public sealed record RuntimeDashboardMetricsApplicationRequest(
    bool RenderOverview,
    LoadedModelSessionSnapshot? SelectedSession,
    AppSettings MetricsSettings,
    RuntimeMetricPollResult? SelectedPollResult,
    string RuntimeKey);

public sealed record RuntimeDashboardMetricsApplicationActions(
    Func<RuntimeSlotSnapshot?, Task<RuntimeMtpTokenSnapshot?>> RefreshRuntimeLogTailAsync,
    Action<RuntimeMetricRowsRenderPlan> ApplyMetricRows,
    Action<RuntimeMetricSummaryPresentation> ApplyMetricSummary);

public sealed class RuntimeDashboardMetricsApplicationService
{
    private readonly RuntimeTelemetryApplicationService _telemetry;
    private readonly RuntimeDashboardRenderDecisionService _renderDecisions;
    private readonly RuntimeMetricRowsRenderService _rowsRender;

    public RuntimeDashboardMetricsApplicationService(
        RuntimeTelemetryApplicationService telemetry,
        RuntimeDashboardRenderDecisionService renderDecisions,
        RuntimeMetricRowsRenderService rowsRender)
    {
        _telemetry = telemetry ?? throw new ArgumentNullException(nameof(telemetry));
        _renderDecisions = renderDecisions ?? throw new ArgumentNullException(nameof(renderDecisions));
        _rowsRender = rowsRender ?? throw new ArgumentNullException(nameof(rowsRender));
    }

    public async Task<RuntimeDashboardRenderDecisionKind> ApplyAsync(
        RuntimeDashboardMetricsApplicationRequest request,
        RuntimeDashboardMetricsApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(request);
        Validate(actions);
        ArgumentNullException.ThrowIfNull(request.MetricsSettings);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.RuntimeKey);

        var decision = _renderDecisions.Decide(new RuntimeDashboardRenderDecisionRequest(
            request.SelectedSession,
            request.MetricsSettings,
            request.SelectedPollResult));

        if (decision.Kind == RuntimeDashboardRenderDecisionKind.NoRuntime)
        {
            _telemetry.ResetMetricCounters();
            if (request.RenderOverview)
            {
                await actions.RefreshRuntimeLogTailAsync(null);
                actions.ApplyMetricRows(_rowsRender.FromSamples([]));
                actions.ApplyMetricSummary(RuntimeMetricSummaryPresentation.NoRuntime);
            }
            return decision.Kind;
        }

        var mtpTokenStats = request.RenderOverview
            ? await actions.RefreshRuntimeLogTailAsync(decision.SlotSnapshot)
            : null;

        if (decision.Kind == RuntimeDashboardRenderDecisionKind.MetricsDisabled)
        {
            _telemetry.ResetMetricCounters();
            var summary = BuildSummary(request.RuntimeKey, [], request.MetricsSettings, decision.SlotSnapshot, mtpTokenStats);
            if (request.RenderOverview)
            {
                actions.ApplyMetricRows(_rowsRender.FromSamples([]));
                actions.ApplyMetricSummary(summary);
            }
            return decision.Kind;
        }

        if (decision.Kind == RuntimeDashboardRenderDecisionKind.FreshMetrics)
        {
            var summary = BuildSummary(request.RuntimeKey, decision.Samples, request.MetricsSettings, decision.SlotSnapshot, mtpTokenStats);
            if (request.RenderOverview)
            {
                actions.ApplyMetricRows(_rowsRender.FromSamples(decision.Samples));
                actions.ApplyMetricSummary(summary);
            }
            return decision.Kind;
        }

        var unavailableSummary = BuildSummary(request.RuntimeKey, [], request.MetricsSettings, decision.SlotSnapshot, mtpTokenStats);
        if (request.RenderOverview)
        {
            actions.ApplyMetricRows(_rowsRender.Unavailable(
                decision.Error,
                _telemetry.LastKnownSamples(request.RuntimeKey)));
            actions.ApplyMetricSummary(unavailableSummary with
            {
                Samples = _telemetry.LastKnownSamples(request.RuntimeKey)
            });
        }
        return decision.Kind;
    }

    private RuntimeMetricSummaryPresentation BuildSummary(
        string runtimeKey,
        IReadOnlyList<PrometheusSample> samples,
        AppSettings metricsSettings,
        RuntimeSlotSnapshot? slotSnapshot,
        RuntimeMtpTokenSnapshot? mtpTokenSnapshot)
    {
        var summary = _telemetry.ApplyMetricSummary(runtimeKey, samples, metricsSettings, slotSnapshot, mtpTokenSnapshot);
        return new RuntimeMetricSummaryPresentation(
            summary.Tokens,
            summary.MtpTokens,
            summary.Slots,
            summary.KvCache,
            summary.UsedLastKnown ? summary.LastKnownCapturedAt : null,
            summary.GraphSample,
            samples,
            summary.Atomic);
    }

    private static void Validate(RuntimeDashboardMetricsApplicationActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.RefreshRuntimeLogTailAsync);
        ArgumentNullException.ThrowIfNull(actions.ApplyMetricRows);
        ArgumentNullException.ThrowIfNull(actions.ApplyMetricSummary);
    }
}
