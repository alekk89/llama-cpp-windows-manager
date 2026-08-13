namespace LocalLlmConsole.Services;

public sealed record MainWindowInfrastructureServices(
    AppUpdateService AppUpdates,
    LoadedModelSessionManager Sessions,
    TrackedProcessRunner ProcessRunner,
    WindowsEnvironmentService WindowsEnvironment,
    WslEnvironmentService WslEnvironment,
    HttpClient RuntimeProbeClient,
    HttpClient MetricsClient,
    HttpClient RuntimePackageClient)
{
    public MainWindowCoreServiceRequest CoreServiceRequest()
        => new(
            AppUpdates,
            Sessions,
            ProcessRunner,
            WindowsEnvironment,
            WslEnvironment,
            RuntimeProbeClient,
            MetricsClient);

    public MainWindowLoadedServiceRequest LoadedServiceRequest(
        StateStore stateStore,
        MainWindowCoreServices coreServices)
        => new(
            stateStore,
            Sessions,
            ProcessRunner,
            RuntimePackageClient,
            coreServices);
}

public sealed record MainWindowCoreServiceRequest(
    AppUpdateService AppUpdates,
    LoadedModelSessionManager Sessions,
    IProcessRunner ProcessRunner,
    WindowsEnvironmentService WindowsEnvironment,
    WslEnvironmentService WslEnvironment,
    HttpClient RuntimeProbeClient,
    HttpClient MetricsClient);

public sealed record MainWindowCoreUiServices(
    MainWindowUiState UiState,
    UiAsyncRefreshTimerController DownloadHistoryRefreshTimer,
    UiAsyncRefreshTimerController RuntimeDashboardRefreshTimer,
    GatewayActivityStatusController GatewayActivity,
    SelectedModelCapabilityController SelectedCapabilities,
    AdvancedSectionStateController AdvancedSections,
    LaunchSettingsEditorSession LaunchSettingsEditor,
    SelectionReentrancyCoordinator SelectionReentrancy,
    GpuSummaryCache GpuSummaryCache,
    RuntimeGpuSummaryApplicationService RuntimeGpuSummaryApplication,
    UiBusyStateController UiBusyState,
    TrayWindowStateController TrayWindowState,
    RuntimeReadinessMonitorRegistry RuntimeReadinessMonitors,
    DebouncedAsyncAction LaunchSettingsRefresh);

public sealed record MainWindowCoreAppServices(
    StateStoreInitializationService StateStoreInitialization,
    BackgroundTaskApplicationService BackgroundTasks,
    ForegroundTaskApplicationService ForegroundTasks,
    ShellIntegrationService ShellIntegration,
    GpuStatusProbeService GpuStatus,
    FileSystemDialogService FileSystemDialogs,
    ClipboardService Clipboard,
    DialogService Dialogs,
    DownloadCompletionApplicationService DownloadCompletionApplication,
    AppStartupApplicationService StartupApplication,
    AppStartupBackgroundApplicationService StartupBackgroundApplication,
    AppSettingsUpdateService SettingsUpdates,
    AppUpdateWorkflowService AppUpdateWorkflow,
    AppUpdateApplicationService AppUpdateApplication,
    CacheClearApplicationService CacheClearApplication,
    SettingsRowActionApplicationService SettingsRowActions,
    FolderSettingsApplicationService FolderSettingsApplication,
    AppLogApplicationService AppLogApplication,
    LifetimeMetricResetApplicationService LifetimeMetricResetApplication,
    AppShutdownDecisionService ShutdownDecisions,
    AppShutdownStateController ShutdownState,
    AppShutdownApplicationService ShutdownApplication,
    AppShutdownCleanupApplicationService ShutdownCleanupApplication,
    SettingsPageDefinitionService SettingsPageDefinitions,
    HelpSectionService HelpSections,
    HelpNavigationApplicationService HelpNavigation,
    LocalAppServiceStartupService LocalAppStartup);

public sealed record MainWindowCoreHuggingFaceServices(
    HuggingFaceModelCardApplicationService HuggingFaceModelCards,
    HuggingFaceSearchApplicationService HuggingFaceSearchApplication,
    HuggingFaceDownloadApplicationService HuggingFaceDownloadApplication);

public sealed record MainWindowCoreRuntimeServices(
    RuntimeCatalogDataService RuntimeCatalogData,
    ActiveRuntimeSessionStore ActiveSessions,
    RuntimeBuildMarkerService RuntimeBuildMarkers,
    RuntimeBuildCancellationRegistry RuntimeBuildCancellations,
    RuntimeSessionCoordinator RuntimeSessions,
    RuntimeSessionPersistenceService RuntimeSessionPersistence,
    RuntimeSourceRepositoryService RuntimeSources,
    RuntimeSessionRecoveryService RuntimeSessionRecovery,
    RuntimeSessionRecoveryApplicationService RuntimeSessionRecoveryApplication,
    RuntimeSessionReconciliationApplicationService RuntimeSessionReconciliationApplication,
    RuntimeReadinessWorkflowService RuntimeReadinessWorkflow,
    RuntimeReadinessCompletionService RuntimeReadinessCompletion,
    RuntimeReadinessMonitorWorkflowService RuntimeReadinessMonitorWorkflow,
    RuntimeReadinessCompletionApplicationService RuntimeReadinessCompletionApplication,
    RuntimeReadinessMonitorApplicationService RuntimeReadinessMonitorApplication,
    RuntimeSessionApplicationService RuntimeSessionApplication,
    RuntimeEndpointProbeService RuntimeEndpointProbe,
    RuntimeTelemetryApplicationService RuntimeTelemetryApplication,
    RuntimeDashboardSelectionService RuntimeDashboardSelection,
    RuntimeDashboardMetricsApplicationService RuntimeDashboardMetricsApplication,
    RuntimeDashboardRefreshApplicationService RuntimeDashboardRefreshApplication,
    RuntimeLogTailService RuntimeLogTail,
    RuntimeOverviewStatusService RuntimeOverviewStatus,
    OverviewModelSelectionApplicationService OverviewModelSelectionApplication,
    OverviewLoadedSessionSelectionApplicationService OverviewLoadedSessionSelectionApplication,
    RuntimeLaunchAdmissionService RuntimeLaunchAdmission,
    RuntimeToolPrerequisiteService RuntimeToolPrerequisites,
    RuntimeBuildPrerequisiteService RuntimeBuildPrerequisites,
    RuntimeLaunchPrerequisiteService RuntimeLaunchPrerequisites);

public sealed record MainWindowCoreModelServices(
    ModelCapabilityCacheService ModelCapabilities,
    ModelRuntimeStatusController ModelRuntimeStatus,
    ModelRuntimeStatusRenderService ModelRuntimeStatusRender,
    ModelRuntimeCommandDecisionService ModelRuntimeCommands,
    ModelRuntimeLoadApplicationService ModelRuntimeLoadApplication,
    ModelRuntimeUnloadApplicationService ModelRuntimeUnloadApplication,
    LaunchRuntimeSelectionService LaunchRuntimeSelection,
    ModelFolderApplicationService ModelFolderApplication,
    ModelDeletionApplicationService ModelDeletionApplication,
    ModelGatewayHostFactoryService ModelGatewayHostFactory,
    ModelGatewayLifecycleApplicationService ModelGatewayLifecycleApplication,
    LaunchSettingsControlStateService LaunchSettingsControlStates,
    ModelRuntimeLaunchApplicationService ModelRuntimeLaunchApplication,
    LaunchSettingsRenderApplicationService LaunchSettingsRenderApplication,
    ModelLaunchHeadSelectionApplicationService ModelLaunchHeadSelectionApplication,
    ModelLaunchSettingsSaveApplicationService ModelLaunchSettingsSaveApplication,
    ModelLaunchVariantSaveApplicationService ModelLaunchVariantSaveApplication);

public sealed record MainWindowCoreEnvironmentServices(
    WindowsToolSetupWorkflowService WindowsToolSetupWorkflow,
    WindowsToolSetupApplicationService WindowsToolSetupApplication,
    WslToolSetupWorkflowService WslToolSetupWorkflow,
    WslToolSetupApplicationService WslToolSetupApplication,
    WslDistroSelectionApplicationService WslDistroSelectionApplication,
    WslPageWorkflowService WslPageWorkflow);

public sealed record MainWindowCoreServices(
    MainWindowCoreUiServices Ui,
    MainWindowCoreAppServices App,
    MainWindowCoreHuggingFaceServices HuggingFaceServices,
    MainWindowCoreRuntimeServices Runtime,
    MainWindowCoreModelServices Models,
    MainWindowCoreEnvironmentServices Environment);

public sealed record MainWindowLoadedServiceRequest(
    StateStore StateStore,
    LoadedModelSessionManager Sessions,
    IProcessRunner ProcessRunner,
    HttpClient RuntimePackageClient,
    MainWindowCoreServices CoreServices);

public sealed record MainWindowLoadedAppServices(
    StateStore StateStore,
    AppSettingsWorkflowService SettingsWorkflow,
    AppSettingsApplicationService SettingsApplication,
    CacheClearWorkflowService CacheClearWorkflow,
    LogPageWorkflowService LogPageWorkflow,
    LogPageApplicationService LogPageApplication,
    LifetimeMetricsApplicationService LifetimeMetricsApplication,
    ModelLookupApplicationService ModelLookupApplication,
    JobEngine Jobs,
    HuggingFaceService HuggingFace,
    DownloadHistoryWorkflowService DownloadHistoryWorkflow,
    DownloadHistoryApplicationService DownloadHistoryApplication);

public sealed record MainWindowLoadedModelServices(
    ModelCatalogService Catalog,
    ModelCatalogRefreshApplicationService ModelCatalogRefreshApplication,
    ModelLaunchProfileService LaunchProfiles,
    ModelLaunchVariantWorkflowService LaunchVariants,
    ModelLaunchSettingsWorkflowService ModelLaunchSettingsWorkflow);

public sealed record MainWindowLoadedGatewayServices(
    GatewayModelLoadWorkflowService GatewayModelLoadWorkflow,
    GatewayRuntimeApplicationService GatewayRuntimeApplication);

public sealed record MainWindowLoadedRuntimeServices(
    RuntimeDeletionPlanner RuntimeDeletion,
    RuntimeDeletionExecutorService RuntimeDeletionExecutor,
    RuntimeRegistryService Runtimes,
    RuntimePackageStatusService RuntimePackageStatus,
    RuntimePackageCheckWorkflowService RuntimePackageCheckWorkflow,
    RuntimePackageInstallWorkflowService RuntimePackageInstallWorkflow,
    RuntimePackageApplicationService RuntimePackageApplication,
    RuntimeCatalogViewService RuntimeCatalogView,
    RuntimeCatalogApplicationService RuntimeCatalogApplication,
    RuntimeCustomRepositoryService CustomRuntimeRepositories,
    RuntimeCatalogCommandApplicationService RuntimeCatalogCommands,
    RuntimeBuildDeletionApplicationService RuntimeBuildDeletionApplication,
    RuntimeBuildApplicationService RuntimeBuildApplication,
    RuntimeBuildJobApplicationService RuntimeBuildJobApplication,
    RuntimeSourceApplicationService RuntimeSourceApplication);

public sealed record MainWindowLoadedServices(
    MainWindowLoadedAppServices App,
    MainWindowLoadedModelServices Models,
    MainWindowLoadedGatewayServices Gateway,
    MainWindowLoadedRuntimeServices Runtime);
