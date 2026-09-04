namespace LocalLlmConsole.Services;

public enum GatewayStatusVisualKind
{
    Normal,
    Activity,
    Warning
}

public sealed record GatewayActivityStatusSnapshot(
    string Line,
    string ToolTip,
    GatewayStatusVisualKind VisualKind);

public sealed class GatewayActivityStatusTracker
{
    private DateTimeOffset _startedAt;
    private string _modelName = "";
    private string _phase = "";
    private string _lastError = "";

    public void Start(ModelRecord model, string phase, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(model);
        _modelName = model.Name;
        _phase = phase;
        _startedAt = now;
        _lastError = "";
    }

    public void SetPhase(string phase)
    {
        if (!HasActivity) return;
        _phase = phase;
    }

    public void Complete()
    {
        _modelName = "";
        _phase = "";
        _lastError = "";
    }

    public void Fail(string message)
    {
        _modelName = "";
        _phase = "";
        _lastError = message;
    }

    public bool HasActivity => !string.IsNullOrWhiteSpace(_modelName);

    public GatewayActivityStatusSnapshot Build(
        AppSettings settings,
        bool gatewayListening,
        DateTimeOffset now)
    {
        if (!settings.AutoLoadGatewayEnabled)
        {
            return new GatewayActivityStatusSnapshot(
                "Gateway: disabled. Direct per-model endpoints still work for manually loaded models.",
                "Auto-load gateway is off. Clients should use each model's direct endpoint after the model is manually loaded.",
                GatewayStatusVisualKind.Normal);
        }

        var endpoint = RuntimeEndpointService.GatewayEndpointDisplay(settings);
        if (!settings.GatewayAutoLoadModels)
        {
            return new GatewayActivityStatusSnapshot(
                $"Gateway: {(gatewayListening ? "listening" : "not listening")} at {endpoint} | Policy: {AppPreferenceService.GatewayPolicyLabel(settings)}",
                Loc.T("Tooltip.Gateway.LoadedOnly"),
                GatewayStatusVisualKind.Normal);
        }
        if (HasActivity)
        {
            var elapsed = DisplayFormatService.Elapsed(now - _startedAt);
            var phase = string.IsNullOrWhiteSpace(_phase) ? "switching" : _phase;
            return new GatewayActivityStatusSnapshot(
                $"Gateway: {phase} {_modelName} ({elapsed}) | {endpoint}",
                "An external client requested this model through the shared gateway. The app is unloading/loading as needed before proxying the request.",
                GatewayStatusVisualKind.Activity);
        }

        if (!string.IsNullOrWhiteSpace(_lastError))
        {
            return new GatewayActivityStatusSnapshot(
                $"Gateway: last auto-load failed | {_lastError}",
                "The most recent external request could not auto-load its model. The error text is kept here until the next successful gateway load.",
                GatewayStatusVisualKind.Warning);
        }

        var state = gatewayListening ? "listening" : "not listening";
        var policy = AppPreferenceService.GatewayPolicyLabel(settings);
        return new GatewayActivityStatusSnapshot(
            $"Gateway: {state} at {endpoint} | Policy: {policy}",
            "The shared OpenAI-compatible endpoint can auto-load the requested registered model. Direct per-model endpoints remain available for loaded sessions.",
            GatewayStatusVisualKind.Normal);
    }
}
