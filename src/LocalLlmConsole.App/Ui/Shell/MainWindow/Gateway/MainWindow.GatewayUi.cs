using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private GatewayRoutingOverviewStatus OverviewGatewayRoutingStatus(IReadOnlyList<LoadedModelSessionSnapshot> sessions)
    {
        var enabled = _settings.AutoLoadGatewayEnabled;
        var state = enabled
            ? _gateway?.IsListening == true ? Localization.Loc.T("Gateway.Listening") : Localization.Loc.T("Gateway.Enabled")
            : Localization.Loc.T("Gateway.Off");
        return new GatewayRoutingOverviewStatus(
            Visible: true,
            Enabled: enabled,
            Endpoint: enabled ? RuntimeEndpointService.GatewayEndpointDisplay(_settings) : "",
            State: state,
            Policy: AppPreferenceService.GatewayPolicyLabel(_settings),
            Exposure: AppPreferenceService.ModelAccessModeLabel(_settings.ModelAccessMode),
            RunningSessions: sessions.Count(session => session.IsRunning));
    }

    private void StartGatewayActivity(ModelRecord model, string phase)
    {
        _coreServices.Ui.GatewayActivity.Start(model, phase, DateTimeOffset.Now, UpdateGatewayStatusText);
    }

    private void SetGatewayActivityPhase(string phase)
    {
        _coreServices.Ui.GatewayActivity.SetPhase(phase, UpdateGatewayStatusText);
    }

    private void CompleteGatewayActivity()
    {
        _coreServices.Ui.GatewayActivity.Complete(UpdateGatewayStatusText);
    }

    private void FailGatewayActivity(string message)
    {
        _coreServices.Ui.GatewayActivity.Fail(message, UpdateGatewayStatusText);
    }

    private void UpdateGatewayStatusText()
    {
    }
}
