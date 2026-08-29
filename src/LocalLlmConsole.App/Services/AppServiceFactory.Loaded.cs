namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    public MainWindowLoadedServices CreateMainWindowLoadedServices(MainWindowLoadedServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.StateStore);
        ArgumentNullException.ThrowIfNull(request.Sessions);
        ArgumentNullException.ThrowIfNull(request.ProcessRunner);
        ArgumentNullException.ThrowIfNull(request.RuntimePackageClient);
        ArgumentNullException.ThrowIfNull(request.CoreServices);

        var stateStore = request.StateStore;
        var core = request.CoreServices;
        var settingsWorkflow = CreateAppSettingsWorkflowService(stateStore, core.App.SettingsUpdates);
        var settingsApplication = CreateAppSettingsApplicationService(
            settingsWorkflow,
            CreateWindowsStartupRegistrationService());
        var cacheClearWorkflow = CreateCacheClearWorkflowService(stateStore);
        var logPageWorkflow = CreateLogPageWorkflowService(stateStore);
        var logPageApplication = CreateLogPageApplicationService(logPageWorkflow);
        var lifetimeMetricsApplication = CreateLifetimeMetricsApplicationService(stateStore);
        var modelLookupApplication = CreateModelLookupApplicationService(stateStore);
        var jobs = CreateJobEngine(stateStore);
        var benchmarks = CreateLazyBenchmarkApplicationService(stateStore, jobs, request.Sessions, request.ProcessRunner);
        var catalog = CreateModelCatalogService(stateStore);
        var modelCatalogRefreshApplication = CreateModelCatalogRefreshApplicationService(stateStore, catalog);
        var modelGroups = CreateModelGroupService(stateStore);
        var launchProfiles = CreateModelLaunchProfileService(stateStore, request.Sessions);
        var trayProfiles = CreateTrayProfileMenuApplicationService(stateStore, request.Sessions);
        var launchVariants = CreateModelLaunchVariantWorkflowService(launchProfiles);
        var modelLaunchSettingsWorkflow = CreateModelLaunchSettingsWorkflowService(launchProfiles);
        var gatewayRouteCatalog = CreateModelGatewayRouteCatalogApplicationService(stateStore);
        var gatewayModelLoadWorkflow = CreateGatewayModelLoadWorkflowService(stateStore, launchProfiles, core.Runtime.RuntimeSessions);
        var gatewayRuntimeApplication = CreateGatewayRuntimeApplicationService(gatewayModelLoadWorkflow);
        var runtimeDeletion = CreateRuntimeDeletionPlanner(stateStore, launchProfiles, request.Sessions);
        var runtimeDeletionExecutor = CreateRuntimeDeletionExecutorService(stateStore);
        var runtimes = CreateRuntimeRegistryService(stateStore);
        var runtimePackageStatus = CreateRuntimePackageStatusService();
        var runtimePackageUpdateChecker = CreateRuntimePackageUpdateCheckService(request.RuntimePackageClient, runtimePackageStatus);
        var runtimeCatalogView = CreateRuntimeCatalogViewService(runtimePackageStatus);
        var customRuntimeRepositories = CreateRuntimeCustomRepositoryService();
        var runtimeCatalogCommands = CreateRuntimeCatalogCommandApplicationService(customRuntimeRepositories);
        var runtimePackageInstaller = CreateRuntimePackageInstallService(request.RuntimePackageClient, runtimes);
        var runtimePackageJobs = CreateRuntimePackageJobService(jobs);
        var runtimePackageWslFiles = CreateRuntimePackageWslFileService(request.ProcessRunner);
        var runtimePackageCheckWorkflow = CreateRuntimePackageCheckWorkflowService(runtimePackageJobs, runtimePackageUpdateChecker);
        var runtimePackageInstallWorkflow = CreateRuntimePackageInstallWorkflowService(
            runtimePackageInstaller,
            runtimePackageJobs,
            runtimePackageWslFiles);
        var runtimePackageApplication = CreateRuntimePackageApplicationService(
            stateStore,
            runtimePackageStatus,
            runtimePackageCheckWorkflow,
            runtimePackageInstallWorkflow,
            runtimeDeletion,
            runtimeDeletionExecutor,
            core.Runtime.RuntimeBuildPrerequisites);
        var runtimeBuildDeletionApplication = CreateRuntimeBuildDeletionApplicationService(
            runtimeDeletion,
            runtimeDeletionExecutor,
            core.Runtime.RuntimeCatalogData);
        var runtimeBuildExecutor = CreateRuntimeBuildExecutionService(request.ProcessRunner, runtimes, core.Runtime.RuntimeBuildMarkers);
        var runtimeBuildWorkflow = CreateRuntimeBuildWorkflowService(jobs, runtimeBuildExecutor, core.Runtime.RuntimeSources, stateStore);
        var runtimeBuildJobControls = CreateRuntimeBuildJobControlService(
            stateStore,
            jobs,
            core.Runtime.RuntimeBuildMarkers,
            core.Runtime.RuntimeBuildCancellations);
        var runtimeBuildApplication = CreateRuntimeBuildApplicationService(
            jobs,
            core.Runtime.RuntimeBuildPrerequisites,
            runtimeBuildWorkflow,
            runtimeBuildJobControls,
            core.Runtime.RuntimeCatalogData);
        var runtimeBuildJobApplication = CreateRuntimeBuildJobApplicationService(runtimeBuildJobControls);
        var runtimeCatalogApplication = CreateRuntimeCatalogApplicationService(
            stateStore,
            runtimes,
            runtimeDeletion,
            core.Runtime.RuntimeCatalogData,
            runtimeCatalogView);
        var huggingFace = CreateHuggingFaceService(stateStore, jobs, catalog);
        var downloadHistoryWorkflow = CreateDownloadHistoryWorkflowService(stateStore, huggingFace);
        var downloadHistoryApplication = CreateDownloadHistoryApplicationService(downloadHistoryWorkflow);
        var runtimeSourceWorkflow = CreateRuntimeSourceWorkflowService(core.Runtime.RuntimeSources, jobs);
        var runtimeSourceApplication = CreateRuntimeSourceApplicationService(stateStore, core.Runtime.RuntimeCatalogData, runtimeSourceWorkflow);

        return new MainWindowLoadedServices(
            new MainWindowLoadedAppServices(
                stateStore,
                settingsWorkflow,
                settingsApplication,
                cacheClearWorkflow,
                logPageWorkflow,
                logPageApplication,
                lifetimeMetricsApplication,
                modelLookupApplication,
                jobs,
                benchmarks,
                huggingFace,
                downloadHistoryWorkflow,
                downloadHistoryApplication),
            new MainWindowLoadedModelServices(
                catalog,
                modelCatalogRefreshApplication,
                modelGroups,
                launchProfiles,
                trayProfiles,
                launchVariants,
                modelLaunchSettingsWorkflow),
            new MainWindowLoadedGatewayServices(
                gatewayRouteCatalog,
                gatewayModelLoadWorkflow,
                gatewayRuntimeApplication),
            new MainWindowLoadedRuntimeServices(
                runtimeDeletion,
                runtimeDeletionExecutor,
                runtimes,
                runtimePackageStatus,
                runtimePackageCheckWorkflow,
                runtimePackageInstallWorkflow,
                runtimePackageApplication,
                runtimeCatalogView,
                runtimeCatalogApplication,
                customRuntimeRepositories,
                runtimeCatalogCommands,
                runtimeBuildDeletionApplication,
                runtimeBuildApplication,
                runtimeBuildJobApplication,
                runtimeSourceApplication));
    }
}
