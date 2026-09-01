namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    public LocalAppServiceStartupService CreateLocalAppServiceStartupService()
        => new();

    public RuntimeSessionCoordinator CreateRuntimeSessionCoordinator(LoadedModelSessionManager sessions)
        => new(sessions, LogRoot);

    public RuntimeSessionPersistenceService CreateRuntimeSessionPersistenceService(ActiveRuntimeSessionStore store, LoadedModelSessionManager sessions)
        => new(store, sessions);

    public RuntimeSessionRecoveryService CreateRuntimeSessionRecoveryService(LoadedModelSessionManager sessions)
        => new(sessions);

    public RuntimeSessionRecoveryApplicationService CreateRuntimeSessionRecoveryApplicationService(
        LoadedModelSessionManager sessions,
        RuntimeSessionPersistenceService persistence,
        RuntimeSessionRecoveryService recovery)
        => new(sessions, persistence, recovery);

    public RuntimeSessionReconciler CreateRuntimeSessionReconciler()
        => new();

    public RuntimeSessionReconciliationApplicationService CreateRuntimeSessionReconciliationApplicationService(
        LoadedModelSessionManager sessions,
        RuntimeSessionPersistenceService persistence,
        RuntimeSessionReconciler reconciler)
        => new(sessions, persistence, reconciler);

    public RuntimeReadinessWorkflowService CreateRuntimeReadinessWorkflowService()
        => new();

    public RuntimeReadinessCompletionService CreateRuntimeReadinessCompletionService()
        => new();

    public RuntimeReadinessMonitorWorkflowService CreateRuntimeReadinessMonitorWorkflowService(
        RuntimeReadinessWorkflowService readiness,
        RuntimeReadinessCompletionService completion)
        => new(readiness, completion);

    public RuntimeReadinessMonitorApplicationService CreateRuntimeReadinessMonitorApplicationService(
        RuntimeReadinessMonitorWorkflowService workflow)
        => new(workflow);

    public RuntimeSessionActionDecisionService CreateRuntimeSessionActionDecisionService()
        => new();

    public RuntimeSessionCommandService CreateRuntimeSessionCommandService(
        RuntimeSessionCoordinator runtimeSessions,
        RuntimeSessionActionDecisionService runtimeSessionActions)
        => new(runtimeSessions, runtimeSessionActions);

    public RuntimeSessionApplicationService CreateRuntimeSessionApplicationService(
        RuntimeSessionCommandService commands)
        => new(commands);

    public ModelRuntimeStartFollowupService CreateModelRuntimeStartFollowupService()
        => new();

    public ModelRuntimeStartFollowupApplicationService CreateModelRuntimeStartFollowupApplicationService()
        => new();

    public RuntimeEndpointProbeService CreateRuntimeEndpointProbeService(HttpClient client)
        => new(client);

    public EndpointInspectionService CreateEndpointInspectionService(HttpClient client)
        => new(client);

    public RuntimeMetricPollerService CreateRuntimeMetricPollerService(HttpClient client)
        => new(client);

    public RuntimeDashboardRefreshCoordinator CreateRuntimeDashboardRefreshCoordinator()
        => new();

    public RuntimeMetricSummaryTracker CreateRuntimeMetricSummaryTracker()
        => new();

    public RuntimeLifetimeCounterTracker CreateRuntimeLifetimeCounterTracker()
        => new();

    public RuntimeIdleUnloadPolicyService CreateRuntimeIdleUnloadPolicyService()
        => new();

    public RuntimeTelemetryApplicationService CreateRuntimeTelemetryApplicationService(RuntimeMetricPollerService poller)
        => new(
            poller,
            CreateRuntimeDashboardRefreshCoordinator(),
            CreateRuntimeMetricSummaryTracker(),
            CreateRuntimeLifetimeCounterTracker(),
            CreateRuntimeIdleUnloadPolicyService());

    public RuntimeDashboardSelectionService CreateRuntimeDashboardSelectionService()
        => new();

    public RuntimeDashboardRenderDecisionService CreateRuntimeDashboardRenderDecisionService()
        => new();

    public RuntimeMetricRowsRenderService CreateRuntimeMetricRowsRenderService()
        => new();

    public RuntimeDashboardMetricsApplicationService CreateRuntimeDashboardMetricsApplicationService(
        RuntimeTelemetryApplicationService telemetry,
        RuntimeDashboardRenderDecisionService renderDecisions,
        RuntimeMetricRowsRenderService rowsRender)
        => new(telemetry, renderDecisions, rowsRender);

    public RuntimeDashboardRefreshApplicationService CreateRuntimeDashboardRefreshApplicationService(
        RuntimeTelemetryApplicationService telemetry,
        RuntimeDashboardSelectionService selection,
        RuntimeDashboardMetricsApplicationService metricsApplication)
        => new(telemetry, selection, metricsApplication);

    public RuntimeLogTailService CreateRuntimeLogTailService()
        => new();

    public RuntimeOverviewStatusService CreateRuntimeOverviewStatusService()
        => new();

    public OverviewModelSelectionApplicationService CreateOverviewModelSelectionApplicationService()
        => new();

    public OverviewLoadedSessionSelectionApplicationService CreateOverviewLoadedSessionSelectionApplicationService()
        => new();

    public RuntimeToolPrerequisiteService CreateRuntimeToolPrerequisiteService(
        WslEnvironmentService wslEnvironment,
        WindowsEnvironmentService windowsEnvironment,
        IProcessRunner processRunner)
        => new(wslEnvironment, windowsEnvironment, processRunner);

    public VisibleCommandLaunchService CreateVisibleCommandLaunchService()
        => new();

    public WindowsToolSetupWorkflowService CreateWindowsToolSetupWorkflowService(
        VisibleCommandLaunchService commandLauncher,
        WindowsEnvironmentService windowsEnvironment)
        => new(commandLauncher, windowsEnvironment);

    public WindowsToolSetupApplicationService CreateWindowsToolSetupApplicationService(
        WindowsToolSetupWorkflowService workflow)
        => new(workflow);

    public WslToolSetupWorkflowService CreateWslToolSetupWorkflowService(
        VisibleCommandLaunchService commandLauncher)
        => new(commandLauncher);

    public WslToolSetupApplicationService CreateWslToolSetupApplicationService(
        WslToolSetupWorkflowService workflow)
        => new(workflow);

    public WslDistroSelectionApplicationService CreateWslDistroSelectionApplicationService()
        => new();

    public WslPageWorkflowService CreateWslPageWorkflowService(
        WslEnvironmentService wslEnvironment,
        IProcessRunner processRunner)
        => new(wslEnvironment, processRunner);

    public RuntimeBuildPrerequisiteService CreateRuntimeBuildPrerequisiteService(RuntimeToolPrerequisiteService runtimeTools)
        => new(runtimeTools);

    public RuntimeLaunchPrerequisiteService CreateRuntimeLaunchPrerequisiteService(
        RuntimeToolPrerequisiteService runtimeTools)
        => new(runtimeTools);

    public ProfileFitCapabilityService CreateProfileFitCapabilityService(IProcessRunner processRunner)
        => new(processRunner);

    public ProfileFitService CreateProfileFitService(
        IProcessRunner processRunner,
        ProfileFitCapabilityService capabilities)
        => new(processRunner, capabilities);

    public RuntimeDeletionPlanner CreateRuntimeDeletionPlanner(
        StateStore stateStore,
        ModelLaunchProfileService launchProfiles,
        LoadedModelSessionManager sessions)
        => new(stateStore, launchProfiles, sessions);

    public RuntimeDeletionExecutorService CreateRuntimeDeletionExecutorService(StateStore stateStore)
        => new(stateStore);
}
