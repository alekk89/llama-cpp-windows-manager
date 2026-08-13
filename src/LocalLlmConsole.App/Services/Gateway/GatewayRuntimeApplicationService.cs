namespace LocalLlmConsole.Services;

public sealed record GatewayRuntimeLoadApplicationRequest(
    ModelRecord Model,
    NamedModelLaunchProfile Profile,
    ModelGatewaySwapPolicy Policy,
    AppSettings Settings,
    LoadedModelSessionSnapshot? ExistingSession);

public sealed record GatewayRuntimeLoadApplicationActions(
    GatewayModelStopper StopModelAsync,
    GatewayModelStarter StartModelAsync,
    GatewayEndpointProbe EndpointAliveAsync,
    GatewayReadyMarker MarkReadyAsync,
    Action<ModelRecord, string> StartActivity,
    Action<string> SetActivityPhase,
    Action CompleteActivity,
    Action<string> FailActivity,
    Func<Task> RefreshOverviewAsync,
    Func<Task> RefreshRuntimeMetricsAsync,
    Action UpdateOverviewModelActions,
    Action<string> SetStatus);

public sealed class GatewayRuntimeApplicationService
{
    private readonly GatewayModelLoadWorkflowService _workflow;

    public GatewayRuntimeApplicationService(GatewayModelLoadWorkflowService workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
    }

    public async Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(
        GatewayRuntimeLoadApplicationRequest request,
        GatewayRuntimeLoadApplicationActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(actions);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.ExistingSession is { IsRunning: true } loaded
            && string.Equals(loaded.LaunchProfileId, request.Profile.Id, StringComparison.OrdinalIgnoreCase))
            return loaded;

        actions.StartActivity(request.Model, "switching to");
        try
        {
            var result = await _workflow.EnsureLoadedAsync(new GatewayModelLoadWorkflowRequest(
                request.Model,
                request.Profile,
                request.Policy,
                request.Settings,
                actions.StopModelAsync,
                (runtime, model, profile, launchSettings, token) => StartModelWithStatusAsync(runtime, model, profile, launchSettings, token, actions),
                actions.EndpointAliveAsync,
                (model, launchSettings, token) => MarkReadyWithStatusAsync(model, launchSettings, token, actions),
                actions.SetActivityPhase),
                cancellationToken);

            actions.CompleteActivity();
            await actions.RefreshOverviewAsync();
            await actions.RefreshRuntimeMetricsAsync();
            actions.UpdateOverviewModelActions();
            return result.Session;
        }
        catch (GatewayModelLoadException ex)
        {
            actions.FailActivity(ex.Message);
            actions.SetStatus(ex.Message);
            throw new InvalidOperationException(ex.Message, ex);
        }
        catch (OperationCanceledException)
        {
            actions.CompleteActivity();
            throw;
        }
    }

    private static async Task StartModelWithStatusAsync(
        RuntimeRecord runtime,
        ModelRecord model,
        NamedModelLaunchProfile profile,
        AppSettings launchSettings,
        CancellationToken cancellationToken,
        GatewayRuntimeLoadApplicationActions actions)
    {
        actions.SetStatus($"Gateway auto-loading {model.Name} with profile {profile.Name}...");
        await actions.StartModelAsync(runtime, model, profile, launchSettings, cancellationToken);
    }

    private static async Task<LoadedModelSessionSnapshot?> MarkReadyWithStatusAsync(
        ModelRecord model,
        AppSettings launchSettings,
        CancellationToken cancellationToken,
        GatewayRuntimeLoadApplicationActions actions)
    {
        var session = await actions.MarkReadyAsync(model, launchSettings, cancellationToken);
        actions.SetStatus($"Gateway loaded {model.Name} at {RuntimeEndpointService.EndpointDisplay(launchSettings)}.");
        return session;
    }
}
