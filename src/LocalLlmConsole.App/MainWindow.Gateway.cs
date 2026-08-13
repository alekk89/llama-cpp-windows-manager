using System.Windows;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task StartModelGatewaySafelyAsync()
    {
        try
        {
            await RestartModelGatewayAsync();
        }
        catch (Exception ex)
        {
            Trace.TraceError($"Gateway startup failed: {ex}");
            SetStatus($"Gateway could not start: {ex.Message}");
        }
    }

    private async Task<bool> RestartModelGatewayAsync()
    {
        var result = await _coreServices.Models.ModelGatewayLifecycleApplication.RestartAsync(
            new ModelGatewayLifecycleRestartRequest(_gateway, _settings),
            new ModelGatewayLifecycleActions(
                gateway => _gateway = gateway,
                EnsureModelApiKeyAsync,
                CreateGatewayRuntimeController,
                _coreServices.Models.ModelGatewayHostFactory.CreateGatewayHost,
                UpdateGatewayStatusText,
                SetStatus));
        _settings = result.Settings;
        return result.GatewayStarted;
    }

    private async Task StopModelGatewayAsync()
        => await _coreServices.Models.ModelGatewayLifecycleApplication.StopAsync(
            new ModelGatewayLifecycleStopRequest(_gateway),
            new ModelGatewayLifecycleStopActions(
                gateway => _gateway = gateway,
                UpdateGatewayStatusText));

    private IModelGatewayRuntimeController CreateGatewayRuntimeController()
        => _coreServices.Models.ModelGatewayHostFactory.CreateRuntimeController(new ModelGatewayRuntimeControllerActions(
            cancellationToken => RunOnUiThreadAsync(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var modelLookup = AppServices.ModelLookupApplication;
                var models = await modelLookup.ListAsync();
                await EnsureDefaultModelLaunchProfilesAsync(models);
                var routes = new List<ModelGatewayModelRoute>();
                foreach (var model in models)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var profiles = ModelServices.LaunchProfiles is null
                        ? []
                        : await ModelServices.LaunchProfiles.ListNamedAsync(model);
                    routes.AddRange(profiles.Select(profile => new ModelGatewayModelRoute(model, profile)));
                }
                return ModelGatewayRouteId.EnsureUnique(routes);
            }),
            cancellationToken => RunOnUiThreadAsync(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>(_sessions.Snapshots()
                    .Where(session => session.IsRunning)
                    .ToArray());
            }),
            (route, policy, cancellationToken) => RunOnUiThreadAsync(() => EnsureGatewayModelLoadedAsync(route, policy, cancellationToken))));

    private Task<T> RunOnUiThreadAsync<T>(Func<Task<T>> action)
    {
        if (Dispatcher.CheckAccess())
            return action();

        return Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private async Task<LoadedModelSessionSnapshot> EnsureGatewayModelLoadedAsync(ModelGatewayModelRoute route, ModelGatewaySwapPolicy policy, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var model = route.Model;
        return await GatewayServices.GatewayRuntimeApplication.EnsureModelLoadedAsync(
            new GatewayRuntimeLoadApplicationRequest(
                model,
                route.Profile,
                policy,
                _settings,
                _sessions.SessionForModel(model.Id)),
                new GatewayRuntimeLoadApplicationActions(
                async (loadedModel, _) => await StopModelRuntimeAsync(loadedModel),
                async (runtime, runtimeModel, profile, launchSettings, _) =>
                {
                    await StartModelRuntimeAsync(
                        runtime,
                        runtimeModel,
                        launchSettings,
                        interactivePrompts: false,
                        launchProfileId: profile.Id,
                        launchProfileName: profile.Name);
                },
                async (launchSettings, token) => await _coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync(launchSettings, token),
                async (runtimeModel, launchSettings, _) => await MarkGatewayModelReadyAsync(runtimeModel, launchSettings),
                StartGatewayActivity,
                SetGatewayActivityPhase,
                CompleteGatewayActivity,
                FailGatewayActivity,
                RefreshOverviewAsync,
                RefreshRuntimeMetricsAsync,
                UpdateOverviewModelActions,
                SetStatus),
            cancellationToken);
    }

    private async Task<LoadedModelSessionSnapshot?> MarkGatewayModelReadyAsync(ModelRecord model, AppSettings launchSettings)
    {
        _sessions.MarkModelLoadedIfRunning(model.Id);
        await SelectOverviewLoadedModelAsync(model.Id);
        await SaveActiveRuntimeSessionsAsync();
        if (_coreServices.Models.ModelRuntimeStatus.IsLoadingModel(model.Id))
            StopModelLoadingTimer(showLoadedDuration: true, loadedModelName: model.Name);
        return _sessions.SessionForModel(model.Id);
    }
}
