namespace LocalLlmConsole.Services;

public sealed record ModelGatewayRuntimeControllerActions(
    Func<CancellationToken, Task<IReadOnlyList<ModelGatewayModelRoute>>> ListModelsAsync,
    Func<CancellationToken, Task<IReadOnlyList<LoadedModelSessionSnapshot>>> RunningSessionsAsync,
    Func<ModelGatewayModelRoute, ModelGatewaySwapPolicy, CancellationToken, Task<LoadedModelSessionSnapshot>> EnsureModelLoadedAsync);

public sealed class ModelGatewayRuntimeController : IModelGatewayRuntimeController
{
    private readonly ModelGatewayRuntimeControllerActions _actions;

    public ModelGatewayRuntimeController(ModelGatewayRuntimeControllerActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
    }

    public Task<IReadOnlyList<ModelGatewayModelRoute>> ListModelsAsync(CancellationToken cancellationToken = default)
        => _actions.ListModelsAsync(cancellationToken);

    public Task<IReadOnlyList<LoadedModelSessionSnapshot>> RunningSessionsAsync(CancellationToken cancellationToken = default)
        => _actions.RunningSessionsAsync(cancellationToken);

    public Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(
        ModelGatewayModelRoute route,
        ModelGatewaySwapPolicy policy,
        CancellationToken cancellationToken = default)
        => _actions.EnsureModelLoadedAsync(route, policy, cancellationToken);
}

public sealed record ModelGatewayRouteCatalogActions(
    Func<IReadOnlyList<ModelRecord>, CancellationToken, Task> EnsureDefaultProfilesAsync);

public sealed class ModelGatewayRouteCatalogApplicationService
{
    private readonly StateStore _stateStore;
    private readonly SemaphoreSlim _defaultRepairGate = new(1, 1);

    public ModelGatewayRouteCatalogApplicationService(StateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async Task<IReadOnlyList<ModelGatewayModelRoute>> ListAsync(
        ModelGatewayRouteCatalogActions actions,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(actions.EnsureDefaultProfilesAsync);
        cancellationToken.ThrowIfCancellationRequested();

        var models = await _stateStore.ListModelsAsync();
        cancellationToken.ThrowIfCancellationRequested();
        var profiles = await _stateStore.ListNamedModelLaunchProfilesAsync();
        var missingDefaults = ModelsMissingDefaultProfiles(models, profiles);
        if (missingDefaults.Count > 0)
            profiles = await RepairDefaultProfilesAsync(models, profiles, actions, cancellationToken);

        return BuildRoutes(models, profiles, cancellationToken);
    }

    private async Task<IReadOnlyList<NamedModelLaunchProfile>> RepairDefaultProfilesAsync(
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<NamedModelLaunchProfile> observedProfiles,
        ModelGatewayRouteCatalogActions actions,
        CancellationToken cancellationToken)
    {
        await _defaultRepairGate.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profiles = observedProfiles;
            var missingDefaults = ModelsMissingDefaultProfiles(models, profiles);
            if (missingDefaults.Count == 0)
                return profiles;

            // Recheck under the gate so simultaneous gateway requests do not all
            // dispatch the same default-profile repair to the UI thread.
            profiles = await _stateStore.ListNamedModelLaunchProfilesAsync();
            missingDefaults = ModelsMissingDefaultProfiles(models, profiles);
            if (missingDefaults.Count == 0)
                return profiles;

            await actions.EnsureDefaultProfilesAsync(missingDefaults, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            return await _stateStore.ListNamedModelLaunchProfilesAsync();
        }
        finally
        {
            _defaultRepairGate.Release();
        }
    }

    private static IReadOnlyList<ModelRecord> ModelsMissingDefaultProfiles(
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<NamedModelLaunchProfile> profiles)
    {
        var modelIdsWithDefaults = profiles
            .Where(profile => profile.IsDefault)
            .Select(profile => profile.ModelId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return models.Where(model => !modelIdsWithDefaults.Contains(model.Id)).ToArray();
    }

    private static IReadOnlyList<ModelGatewayModelRoute> BuildRoutes(
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        CancellationToken cancellationToken)
    {
        var profilesByModel = profiles
            .GroupBy(profile => profile.ModelId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
        var routes = new List<ModelGatewayModelRoute>(profiles.Count);
        foreach (var model in models)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!profilesByModel.TryGetValue(model.Id, out var modelProfiles))
                continue;
            routes.AddRange(modelProfiles.Select(profile => new ModelGatewayModelRoute(model, profile)));
        }

        return ModelGatewayRouteId.EnsureUnique(routes);
    }
}
