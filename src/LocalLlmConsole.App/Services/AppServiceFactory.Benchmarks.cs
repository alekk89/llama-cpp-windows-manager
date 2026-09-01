namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    public Lazy<BenchmarkApplicationService> CreateLazyBenchmarkApplicationService(
        StateStore stateStore,
        JobEngine jobs,
        LoadedModelSessionManager sessions,
        IProcessRunner processRunner)
        => new(
            () => new BenchmarkApplicationService(
                stateStore,
                jobs,
                sessions,
                new BenchmarkCapabilityService(processRunner),
                new BenchmarkProcessRunner(CreateWslRuntimeStopService(processRunner)),
                _workspaceRoot,
                gpuStatus: CreateGpuStatusProbeService(processRunner)),
            LazyThreadSafetyMode.ExecutionAndPublication);
}
