namespace LocalLlmConsole.Services;

public sealed record ModelGatewayLifecycleRestartRequest(
    IModelGatewayHost? CurrentGateway,
    AppSettings Settings);

public sealed record ModelGatewayLifecycleActions(
    Action<IModelGatewayHost?> SetGateway,
    Func<AppSettings, Task<AppSettings>> EnsureModelApiKeyAsync,
    Func<IModelGatewayRuntimeController> CreateRuntimeController,
    Func<ModelGatewayOptions, IModelGatewayRuntimeController, IModelGatewayHost> CreateGateway,
    Action UpdateGatewayStatusText,
    Action<string> SetStatus);

public sealed record ModelGatewayLifecycleRestartResult(
    AppSettings Settings,
    bool GatewayStarted);

public sealed record ModelGatewayLifecycleStopRequest(
    IModelGatewayHost? CurrentGateway);

public sealed record ModelGatewayLifecycleStopActions(
    Action<IModelGatewayHost?> SetGateway,
    Action UpdateGatewayStatusText);

public sealed class ModelGatewayLifecycleApplicationService
{
    public async Task<ModelGatewayLifecycleRestartResult> RestartAsync(
        ModelGatewayLifecycleRestartRequest request,
        ModelGatewayLifecycleActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(request.Settings);

        await StopCurrentAsync(
            new ModelGatewayLifecycleStopRequest(request.CurrentGateway),
            new ModelGatewayLifecycleStopActions(actions.SetGateway, actions.UpdateGatewayStatusText),
            updateWhenMissing: false);

        if (!request.Settings.AutoLoadGatewayEnabled)
        {
            actions.UpdateGatewayStatusText();
            return new ModelGatewayLifecycleRestartResult(request.Settings, GatewayStarted: false);
        }

        var settings = await actions.EnsureModelApiKeyAsync(request.Settings);
        cancellationToken.ThrowIfCancellationRequested();

        var options = ModelGatewayOptions.FromSettings(settings);
        var gateway = actions.CreateGateway(options, actions.CreateRuntimeController());
        actions.SetGateway(gateway);

        try
        {
            await gateway.StartAsync(cancellationToken);
            actions.SetStatus($"Gateway listening at {RuntimeEndpointService.GatewayEndpointDisplay(settings)}. {AppPreferenceService.GatewayPolicyLabel(settings)}.");
            actions.UpdateGatewayStatusText();
            return new ModelGatewayLifecycleRestartResult(settings, GatewayStarted: true);
        }
        catch (Exception ex)
        {
            await gateway.DisposeAsync();
            actions.SetGateway(null);
            actions.UpdateGatewayStatusText();
            actions.SetStatus($"Gateway could not start: {ex.Message}");
            Trace.TraceError($"Gateway restart failed: {ex}");
            return new ModelGatewayLifecycleRestartResult(settings, GatewayStarted: false);
        }
    }

    public async Task<bool> StopAsync(
        ModelGatewayLifecycleStopRequest request,
        ModelGatewayLifecycleStopActions actions)
        => await StopCurrentAsync(request, actions, updateWhenMissing: true);

    private static async Task<bool> StopCurrentAsync(
        ModelGatewayLifecycleStopRequest request,
        ModelGatewayLifecycleStopActions actions,
        bool updateWhenMissing)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);

        if (request.CurrentGateway is null)
        {
            if (updateWhenMissing)
                actions.UpdateGatewayStatusText();
            return false;
        }

        await request.CurrentGateway.DisposeAsync();
        actions.SetGateway(null);
        actions.UpdateGatewayStatusText();
        return true;
    }
}
