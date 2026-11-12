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
                performance: Performance,
                upstreamActivity: UpstreamActivity));
    }

    public GatewayPerformanceTracker Performance { get; }

    // Shared across gateway restarts so idle unload sees requests that are
    // still finishing on a previous gateway instance.
    public ModelGatewayUpstreamActivity UpstreamActivity { get; } = new();

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
