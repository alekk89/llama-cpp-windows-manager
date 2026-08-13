namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    public MainWindowCoreServices CreateMainWindowCoreServices(MainWindowCoreServiceRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.AppUpdates);
        ArgumentNullException.ThrowIfNull(request.Sessions);
        ArgumentNullException.ThrowIfNull(request.ProcessRunner);
        ArgumentNullException.ThrowIfNull(request.WindowsEnvironment);
        ArgumentNullException.ThrowIfNull(request.WslEnvironment);
        ArgumentNullException.ThrowIfNull(request.RuntimeProbeClient);
        ArgumentNullException.ThrowIfNull(request.MetricsClient);

        var uiState = CreateMainWindowUiState();
        var stateStoreInitialization = CreateStateStoreInitializationService();
        var backgroundTasks = CreateBackgroundTaskApplicationService();
        var foregroundTasks = CreateForegroundTaskApplicationService();
        var shellIntegration = CreateShellIntegrationService();
        var gpuStatus = CreateGpuStatusProbeService(request.ProcessRunner);
        var fileSystemDialogs = CreateFileSystemDialogService();
        var clipboard = CreateClipboardService();
        var dialogs = CreateDialogService();
        var downloadCompletionApplication = CreateDownloadCompletionApplicationService();
        var uiTimerFactory = CreateUiTimerFactory();
        var downloadHistoryRefreshTimer = CreateUiAsyncRefreshTimerController(uiTimerFactory);
        var runtimeDashboardRefreshTimer = CreateUiAsyncRefreshTimerController(uiTimerFactory);
        var gatewayActivity = CreateGatewayActivityStatusController(
            CreateGatewayActivityStatusTracker(),
            uiTimerFactory);
        var selectedCapabilities = CreateSelectedModelCapabilityController();
        var advancedSections = CreateAdvancedSectionStateController();
        var launchSettingsEditor = CreateLaunchSettingsEditorSession();
        var selectionReentrancy = CreateSelectionReentrancyCoordinator();
        var gpuSummaryCache = CreateGpuSummaryCache();
        var runtimeGpuSummaryApplication = CreateRuntimeGpuSummaryApplicationService(gpuStatus, gpuSummaryCache);
        var uiBusyState = CreateUiBusyStateController();
        var trayWindowState = CreateTrayWindowStateController();
        var runtimeReadinessMonitors = CreateRuntimeReadinessMonitorRegistry();
        var launchSettingsRefresh = CreateLaunchSettingsRefreshAction();
        var settingsUpdates = CreateAppSettingsUpdateService();
        var appUpdateWorkflow = CreateAppUpdateWorkflowService(request.AppUpdates);
        var appUpdateApplication = CreateAppUpdateApplicationService();
        var cacheClearApplication = CreateCacheClearApplicationService();
        var huggingFaceModelCards = CreateHuggingFaceModelCardApplicationService();
        var huggingFaceSearchApplication = CreateHuggingFaceSearchApplicationService();
        var huggingFaceDownloadApplication = CreateHuggingFaceDownloadApplicationService();
        var settingsRowActions = CreateSettingsRowActionApplicationService();
        var folderSettingsApplication = CreateFolderSettingsApplicationService();
        var appLogApplication = CreateAppLogApplicationService();
        var lifetimeMetricResetApplication = CreateLifetimeMetricResetApplicationService();
        var shutdownDecisions = CreateAppShutdownDecisionService();
        var shutdownState = CreateAppShutdownStateController();
        var shutdownApplication = CreateAppShutdownApplicationService(shutdownDecisions, shutdownState);
        var shutdownCleanupApplication = CreateAppShutdownCleanupApplicationService();
        var settingsPageDefinitions = CreateSettingsPageDefinitionService();
        var helpSections = CreateHelpSectionService();
        var helpNavigation = CreateHelpNavigationApplicationService();
        var runtimeCatalogData = CreateRuntimeCatalogDataService();
        var activeSessions = CreateActiveRuntimeSessionStore();
        var runtimeBuildMarkers = CreateRuntimeBuildMarkerService(request.ProcessRunner);
        var runtimeBuildCancellations = CreateRuntimeBuildCancellationRegistry();
        var localAppStartup = CreateLocalAppServiceStartupService();
        var startupRegistration = CreateWindowsStartupRegistrationService();
        var startupApplication = CreateAppStartupApplicationService(
            stateStoreInitialization,
            localAppStartup,
            runtimeBuildMarkers,
            startupRegistration);
        var startupBackgroundApplication = CreateAppStartupBackgroundApplicationService();
        var runtimeSessions = CreateRuntimeSessionCoordinator(request.Sessions);
        var runtimeSessionPersistence = CreateRuntimeSessionPersistenceService(activeSessions, request.Sessions);
        var modelCapabilities = CreateModelCapabilityCacheService();
        var runtimeSources = CreateRuntimeSourceRepositoryService(request.ProcessRunner);
        var runtimeSessionRecovery = CreateRuntimeSessionRecoveryService(request.Sessions);
        var runtimeSessionRecoveryApplication = CreateRuntimeSessionRecoveryApplicationService(
            request.Sessions,
            runtimeSessionPersistence,
            runtimeSessionRecovery);
        var runtimeSessionReconciler = CreateRuntimeSessionReconciler();
        var runtimeSessionReconciliationApplication = CreateRuntimeSessionReconciliationApplicationService(
            request.Sessions,
            runtimeSessionPersistence,
            runtimeSessionReconciler);
        var runtimeReadinessWorkflow = CreateRuntimeReadinessWorkflowService();
        var runtimeReadinessCompletion = CreateRuntimeReadinessCompletionService();
        var runtimeReadinessMonitorWorkflow = CreateRuntimeReadinessMonitorWorkflowService(
            runtimeReadinessWorkflow,
            runtimeReadinessCompletion);
        var runtimeReadinessCompletionApplication = CreateRuntimeReadinessCompletionApplicationService();
        var runtimeReadinessMonitorApplication = CreateRuntimeReadinessMonitorApplicationService(
            runtimeReadinessMonitorWorkflow,
            runtimeReadinessCompletionApplication);
        var runtimeSessionActions = CreateRuntimeSessionActionDecisionService();
        var runtimeSessionCommands = CreateRuntimeSessionCommandService(runtimeSessions, runtimeSessionActions);
        var runtimeSessionFollowupApplication = CreateRuntimeSessionFollowupApplicationService();
        var runtimeSessionApplication = CreateRuntimeSessionApplicationService(
            runtimeSessionCommands,
            runtimeSessionFollowupApplication);
        var modelRuntimeStartFollowup = CreateModelRuntimeStartFollowupService();
        var modelRuntimeStartFollowupApplication = CreateModelRuntimeStartFollowupApplicationService();
        var runtimeEndpointProbe = CreateRuntimeEndpointProbeService(request.RuntimeProbeClient);
        var runtimeMetricPoller = CreateRuntimeMetricPollerService(request.MetricsClient);
        var runtimeTelemetryApplication = CreateRuntimeTelemetryApplicationService(runtimeMetricPoller);
        var runtimeDashboardSelection = CreateRuntimeDashboardSelectionService();
        var runtimeDashboardRenderDecisions = CreateRuntimeDashboardRenderDecisionService();
        var runtimeMetricRowsRender = CreateRuntimeMetricRowsRenderService();
        var runtimeDashboardMetricsApplication = CreateRuntimeDashboardMetricsApplicationService(
            runtimeTelemetryApplication,
            runtimeDashboardRenderDecisions,
            runtimeMetricRowsRender);
        var runtimeDashboardRefreshApplication = CreateRuntimeDashboardRefreshApplicationService(
            runtimeTelemetryApplication,
            runtimeDashboardSelection,
            runtimeDashboardMetricsApplication);
        var runtimeLogTail = CreateRuntimeLogTailService();
        var runtimeOverviewStatus = CreateRuntimeOverviewStatusService();
        var overviewModelSelectionApplication = CreateOverviewModelSelectionApplicationService();
        var overviewLoadedSessionSelectionApplication = CreateOverviewLoadedSessionSelectionApplicationService();
        var modelRuntimeStatus = CreateModelRuntimeStatusController(
            CreateModelRuntimeStatusTracker(),
            uiTimerFactory);
        var modelRuntimeStatusRender = CreateModelRuntimeStatusRenderService();
        var runtimeLaunchAdmission = CreateRuntimeLaunchAdmissionService();
        var modelRuntimeCommands = CreateModelRuntimeCommandDecisionService();
        var launchRuntimeSelection = CreateLaunchRuntimeSelectionService();
        var modelRuntimeLoadApplication = CreateModelRuntimeLoadApplicationService(
            modelRuntimeCommands,
            launchRuntimeSelection);
        var modelRuntimeUnloadApplication = CreateModelRuntimeUnloadApplicationService(modelRuntimeCommands);
        var modelFolderApplication = CreateModelFolderApplicationService();
        var modelDeletionApplication = CreateModelDeletionApplicationService();
        var modelGatewayHostFactory = CreateModelGatewayHostFactoryService();
        var modelGatewayLifecycleApplication = CreateModelGatewayLifecycleApplicationService();
        var launchSettingsControlStates = CreateLaunchSettingsControlStateService();
        var runtimeToolPrerequisites = CreateRuntimeToolPrerequisiteService(
            request.WslEnvironment,
            request.WindowsEnvironment,
            request.ProcessRunner);
        var runtimeBuildPrerequisites = CreateRuntimeBuildPrerequisiteService(runtimeToolPrerequisites);
        var runtimeLaunchPrerequisites = CreateRuntimeLaunchPrerequisiteService(runtimeToolPrerequisites);
        var visibleCommandLauncher = CreateVisibleCommandLaunchService();
        var windowsToolSetupWorkflow = CreateWindowsToolSetupWorkflowService(visibleCommandLauncher, request.WindowsEnvironment);
        var windowsToolSetupApplication = CreateWindowsToolSetupApplicationService(windowsToolSetupWorkflow);
        var wslToolSetupWorkflow = CreateWslToolSetupWorkflowService(visibleCommandLauncher);
        var wslToolSetupApplication = CreateWslToolSetupApplicationService(wslToolSetupWorkflow);
        var wslDistroSelectionApplication = CreateWslDistroSelectionApplicationService();
        var wslPageWorkflow = CreateWslPageWorkflowService(request.WslEnvironment, request.ProcessRunner);
        var modelRuntimeLaunchPreparation = CreateModelRuntimeLaunchPreparationService(
            runtimeSessions,
            runtimeLaunchPrerequisites,
            runtimeLaunchAdmission,
            gpuStatus);
        var modelRuntimeLaunchApplication = CreateModelRuntimeLaunchApplicationService(
            modelRuntimeLaunchPreparation,
            runtimeSessionCommands,
            modelRuntimeStartFollowup,
            modelRuntimeStartFollowupApplication);
        var launchSettingsRenderApplication = CreateLaunchSettingsRenderApplicationService();
        var modelLaunchHeadSelectionApplication = CreateModelLaunchHeadSelectionApplicationService();
        var modelLaunchSettingsSaveApplication = CreateModelLaunchSettingsSaveApplicationService();
        var modelLaunchVariantSaveApplication = CreateModelLaunchVariantSaveApplicationService();

        return new MainWindowCoreServices(
            new MainWindowCoreUiServices(
                uiState,
                downloadHistoryRefreshTimer,
                runtimeDashboardRefreshTimer,
                gatewayActivity,
                selectedCapabilities,
                advancedSections,
                launchSettingsEditor,
                selectionReentrancy,
                gpuSummaryCache,
                runtimeGpuSummaryApplication,
                uiBusyState,
                trayWindowState,
                runtimeReadinessMonitors,
                launchSettingsRefresh),
            new MainWindowCoreAppServices(
                stateStoreInitialization,
                backgroundTasks,
                foregroundTasks,
                shellIntegration,
                gpuStatus,
                fileSystemDialogs,
                clipboard,
                dialogs,
                downloadCompletionApplication,
                startupApplication,
                startupBackgroundApplication,
                settingsUpdates,
                appUpdateWorkflow,
                appUpdateApplication,
                cacheClearApplication,
                settingsRowActions,
                folderSettingsApplication,
                appLogApplication,
                lifetimeMetricResetApplication,
                shutdownDecisions,
                shutdownState,
                shutdownApplication,
                shutdownCleanupApplication,
                settingsPageDefinitions,
                helpSections,
                helpNavigation,
                localAppStartup),
            new MainWindowCoreHuggingFaceServices(
                huggingFaceModelCards,
                huggingFaceSearchApplication,
                huggingFaceDownloadApplication),
            new MainWindowCoreRuntimeServices(
                runtimeCatalogData,
                activeSessions,
                runtimeBuildMarkers,
                runtimeBuildCancellations,
                runtimeSessions,
                runtimeSessionPersistence,
                runtimeSources,
                runtimeSessionRecovery,
                runtimeSessionRecoveryApplication,
                runtimeSessionReconciliationApplication,
                runtimeReadinessWorkflow,
                runtimeReadinessCompletion,
                runtimeReadinessMonitorWorkflow,
                runtimeReadinessCompletionApplication,
                runtimeReadinessMonitorApplication,
                runtimeSessionApplication,
                runtimeEndpointProbe,
                runtimeTelemetryApplication,
                runtimeDashboardSelection,
                runtimeDashboardMetricsApplication,
                runtimeDashboardRefreshApplication,
                runtimeLogTail,
                runtimeOverviewStatus,
                overviewModelSelectionApplication,
                overviewLoadedSessionSelectionApplication,
                runtimeLaunchAdmission,
                runtimeToolPrerequisites,
                runtimeBuildPrerequisites,
                runtimeLaunchPrerequisites),
            new MainWindowCoreModelServices(
                modelCapabilities,
                modelRuntimeStatus,
                modelRuntimeStatusRender,
                modelRuntimeCommands,
                modelRuntimeLoadApplication,
                modelRuntimeUnloadApplication,
                launchRuntimeSelection,
                modelFolderApplication,
                modelDeletionApplication,
                modelGatewayHostFactory,
                modelGatewayLifecycleApplication,
                launchSettingsControlStates,
                modelRuntimeLaunchApplication,
                launchSettingsRenderApplication,
                modelLaunchHeadSelectionApplication,
                modelLaunchSettingsSaveApplication,
                modelLaunchVariantSaveApplication),
            new MainWindowCoreEnvironmentServices(
                windowsToolSetupWorkflow,
                windowsToolSetupApplication,
                wslToolSetupWorkflow,
                wslToolSetupApplication,
                wslDistroSelectionApplication,
                wslPageWorkflow));
    }
}
