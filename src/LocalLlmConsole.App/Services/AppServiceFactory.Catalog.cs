namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    public JobEngine CreateJobEngine(StateStore stateStore)
        => new(stateStore, LogRoot);

    public ModelCatalogService CreateModelCatalogService(StateStore stateStore)
        => new(stateStore);

    public ModelGroupService CreateModelGroupService(StateStore stateStore)
        => new(stateStore);

    public ModelCatalogRefreshApplicationService CreateModelCatalogRefreshApplicationService(
        StateStore stateStore,
        ModelCatalogService catalog)
        => new(stateStore, catalog);

    public ModelLaunchProfileService CreateModelLaunchProfileService(StateStore stateStore, LoadedModelSessionManager sessions)
        => new(stateStore, sessions);

    public TrayProfileMenuApplicationService CreateTrayProfileMenuApplicationService(
        StateStore stateStore,
        LoadedModelSessionManager sessions)
        => new(stateStore, sessions);

    public ModelLaunchVariantWorkflowService CreateModelLaunchVariantWorkflowService(
        ModelLaunchProfileService launchProfiles)
        => new(launchProfiles);

    public ModelLaunchSettingsWorkflowService CreateModelLaunchSettingsWorkflowService(
        ModelLaunchProfileService launchProfiles)
        => new(launchProfiles);

    public ModelCapabilityCacheService CreateModelCapabilityCacheService()
        => new();

    public RuntimeRegistryService CreateRuntimeRegistryService(StateStore stateStore)
        => new(stateStore);

    public RuntimePackageStatusService CreateRuntimePackageStatusService()
        => new();

    public RuntimePackageUpdateCheckService CreateRuntimePackageUpdateCheckService(HttpClient client, RuntimePackageStatusService status)
        => new(client, status);

    public RuntimePackageCheckWorkflowService CreateRuntimePackageCheckWorkflowService(
        RuntimePackageJobService jobs,
        RuntimePackageUpdateCheckService checks)
        => new(jobs, checks);

    public RuntimePackageInstallWorkflowService CreateRuntimePackageInstallWorkflowService(
        RuntimePackageInstallService installer,
        RuntimePackageJobService jobs,
        RuntimePackageWslFileService wslFiles)
        => new(installer, jobs, wslFiles);

    public RuntimePackageApplicationService CreateRuntimePackageApplicationService(
        StateStore stateStore,
        RuntimePackageStatusService packageStatus,
        RuntimePackageCheckWorkflowService packageCheck,
        RuntimePackageInstallWorkflowService packageInstall,
        RuntimeDeletionPlanner deletionPlanner,
        RuntimeDeletionExecutorService deletionExecutor,
        RuntimeBuildPrerequisiteService prerequisites)
        => new(stateStore, packageStatus, packageCheck, packageInstall, deletionPlanner, deletionExecutor, prerequisites);

    public RuntimeCatalogDataService CreateRuntimeCatalogDataService()
        => new();

    public RuntimeCatalogViewService CreateRuntimeCatalogViewService(RuntimePackageStatusService packageStatus)
        => new(packageStatus);

    public RuntimeCatalogApplicationService CreateRuntimeCatalogApplicationService(
        StateStore stateStore,
        RuntimeRegistryService registry,
        RuntimeDeletionPlanner deletion,
        RuntimeCatalogDataService data,
        RuntimeCatalogViewService view)
        => new(stateStore, registry, deletion, data, view);

    public RuntimeCustomRepositoryService CreateRuntimeCustomRepositoryService()
        => new();

    public RuntimeCatalogCommandApplicationService CreateRuntimeCatalogCommandApplicationService(
        RuntimeCustomRepositoryService customRepositories)
        => new(customRepositories);

    public RuntimeBuildDeletionApplicationService CreateRuntimeBuildDeletionApplicationService(
        RuntimeDeletionPlanner deletionPlanner,
        RuntimeDeletionExecutorService deletionExecutor,
        RuntimeCatalogDataService catalogData)
        => new(deletionPlanner, deletionExecutor, catalogData);

    public RuntimePackageInstallService CreateRuntimePackageInstallService(HttpClient client, RuntimeRegistryService runtimes)
        => new(client, runtimes);

    public RuntimePackageJobService CreateRuntimePackageJobService(JobEngine jobs)
        => new(jobs);

    public RuntimePackageWslFileService CreateRuntimePackageWslFileService(IProcessRunner processRunner)
        => new(processRunner);

    public RuntimeBuildMarkerService CreateRuntimeBuildMarkerService(IProcessRunner processRunner)
        => new(processRunner);

    public RuntimeBuildCancellationRegistry CreateRuntimeBuildCancellationRegistry()
        => new();

    public RuntimeBuildJobControlService CreateRuntimeBuildJobControlService(
        StateStore stateStore,
        JobEngine jobs,
        RuntimeBuildMarkerService markers,
        RuntimeBuildCancellationRegistry cancellations)
        => new(stateStore, jobs, markers, cancellations, _workspaceRoot);

    public RuntimeBuildExecutionService CreateRuntimeBuildExecutionService(
        IProcessRunner processRunner,
        RuntimeRegistryService runtimes,
        RuntimeBuildMarkerService markers)
        => new(_workspaceRoot, processRunner, runtimes, markers);

    public RuntimeSourceRepositoryService CreateRuntimeSourceRepositoryService(IProcessRunner processRunner)
        => new(processRunner);

    public RuntimeBuildWorkflowService CreateRuntimeBuildWorkflowService(
        JobEngine jobs,
        RuntimeBuildExecutionService executor,
        RuntimeSourceRepositoryService sources,
        StateStore stateStore)
        => new(jobs, executor, sources, stateStore);

    public RuntimeBuildApplicationService CreateRuntimeBuildApplicationService(
        JobEngine jobs,
        RuntimeBuildPrerequisiteService prerequisites,
        RuntimeBuildWorkflowService workflow,
        RuntimeBuildJobControlService jobControls,
        RuntimeCatalogDataService catalogData)
        => new(jobs, prerequisites, workflow, jobControls, catalogData);

    public RuntimeBuildJobApplicationService CreateRuntimeBuildJobApplicationService(
        RuntimeBuildJobControlService jobControls)
        => new(jobControls);

    public RuntimeSourceWorkflowService CreateRuntimeSourceWorkflowService(
        RuntimeSourceRepositoryService sources,
        JobEngine jobs)
        => new(sources, jobs);

    public RuntimeSourceApplicationService CreateRuntimeSourceApplicationService(
        StateStore stateStore,
        RuntimeCatalogDataService catalogData,
        RuntimeSourceWorkflowService sourceWorkflow)
        => new(stateStore, catalogData, sourceWorkflow);

    public HuggingFaceService CreateHuggingFaceService(StateStore stateStore, JobEngine jobs, ModelCatalogService catalog)
        => new(stateStore, jobs, catalog);

    public DownloadHistoryWorkflowService CreateDownloadHistoryWorkflowService(StateStore stateStore, HuggingFaceService huggingFace)
        => new(stateStore, huggingFace);

    public DownloadHistoryApplicationService CreateDownloadHistoryApplicationService(DownloadHistoryWorkflowService workflow)
        => new(workflow);

    public LocalAppService CreateLocalAppService(
        StateStore stateStore,
        JobEngine jobs,
        int port,
        LocalControlApi? controlApi = null,
        LocalControlDiscoveryService? discovery = null)
        => new(stateStore, jobs, port, controlApi, discovery);
}
