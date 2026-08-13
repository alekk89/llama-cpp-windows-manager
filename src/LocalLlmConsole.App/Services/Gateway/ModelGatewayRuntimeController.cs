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
