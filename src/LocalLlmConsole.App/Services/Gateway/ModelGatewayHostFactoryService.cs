namespace LocalLlmConsole.Services;

public sealed class ModelGatewayHostFactoryService
{
    private readonly Func<ModelGatewayRuntimeControllerActions, IModelGatewayRuntimeController> _createRuntimeController;
    private readonly Func<ModelGatewayOptions, IModelGatewayRuntimeController, IModelGatewayHost> _createGatewayHost;

    public ModelGatewayHostFactoryService(
        Func<ModelGatewayRuntimeControllerActions, IModelGatewayRuntimeController>? createRuntimeController = null,
        Func<ModelGatewayOptions, IModelGatewayRuntimeController, IModelGatewayHost>? createGatewayHost = null,
        GatewayPerformanceTracker? performance = null)
    {
        Performance = performance ?? new GatewayPerformanceTracker();
        _createRuntimeController = createRuntimeController ?? DefaultRuntimeControllerFactory;
        _createGatewayHost = createGatewayHost
            ?? ((options, runtime) => new ModelGatewayService(options, runtime,
                upstreamProxy: new ModelGatewayUpstreamProxy(performance: Performance),
                performance: Performance));
    }

    public GatewayPerformanceTracker Performance { get; }

    public IModelGatewayRuntimeController CreateRuntimeController(ModelGatewayRuntimeControllerActions actions)
    {
        ArgumentNullException.ThrowIfNull(actions);
        return _createRuntimeController(actions);
    }

    public IModelGatewayHost CreateGatewayHost(
        ModelGatewayOptions options,
        IModelGatewayRuntimeController runtime)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(runtime);
        return _createGatewayHost(options, runtime);
    }

    private static ModelGatewayRuntimeController DefaultRuntimeControllerFactory(
        ModelGatewayRuntimeControllerActions actions)
        => new(actions);

}
