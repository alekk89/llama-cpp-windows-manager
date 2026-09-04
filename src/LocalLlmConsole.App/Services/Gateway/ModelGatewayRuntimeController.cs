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
    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private CachedCatalog? _cached;
    private sealed record CachedCatalog(long Revision, ModelGatewayRouteSnapshot Routes);

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

        var cached = Volatile.Read(ref _cached);
        if (cached?.Revision == _stateStore.CatalogRevision) return cached.Routes;

        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var revision = _stateStore.CatalogRevision;
                cached = _cached;
                if (cached?.Revision == revision) return cached.Routes;

                var models = await _stateStore.ListModelsAsync();
                var profiles = await _stateStore.ListNamedModelLaunchProfilesAsync();
                cancellationToken.ThrowIfCancellationRequested();
                if (revision != _stateStore.CatalogRevision) continue;
                var missingDefaults = ModelsMissingDefaultProfiles(models, profiles);
                if (missingDefaults.Count > 0)
                {
                    await actions.EnsureDefaultProfilesAsync(missingDefaults, cancellationToken);
                    profiles = await _stateStore.ListNamedModelLaunchProfilesAsync();
                }

                var routes = new ModelGatewayRouteSnapshot(BuildRoutes(models, profiles, cancellationToken));
                // Both reads, repair, and route construction must describe one revision.
                if (revision != _stateStore.CatalogRevision) continue;
                Volatile.Write(ref _cached, new CachedCatalog(revision, routes));
                return routes;
            }
        }
        finally
        {
            _catalogGate.Release();
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
