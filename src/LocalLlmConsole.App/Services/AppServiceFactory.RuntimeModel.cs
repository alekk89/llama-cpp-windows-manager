namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    public ModelRuntimeLaunchApplicationService CreateModelRuntimeLaunchApplicationService(
        ModelRuntimeLaunchPreparationService preparation,
        RuntimeSessionCommandService commands,
        ModelRuntimeStartFollowupService followup,
        ModelRuntimeStartFollowupApplicationService followupApplication)
        => new(preparation, commands, followup, followupApplication);

    public ModelRuntimeStatusTracker CreateModelRuntimeStatusTracker()
        => new();

    public IUiTimerFactory CreateUiTimerFactory()
        => new DispatcherUiTimerFactory();

    public UiAsyncRefreshTimerController CreateUiAsyncRefreshTimerController(IUiTimerFactory timerFactory)
        => new(timerFactory);

    public GatewayActivityStatusTracker CreateGatewayActivityStatusTracker()
        => new();

    public GatewayActivityStatusController CreateGatewayActivityStatusController(
        GatewayActivityStatusTracker tracker,
        IUiTimerFactory timerFactory)
        => new(tracker, timerFactory);

    public SelectedModelCapabilityController CreateSelectedModelCapabilityController()
        => new();

    public AdvancedSectionStateController CreateAdvancedSectionStateController()
        => new();

    public LaunchSettingsEditorSession CreateLaunchSettingsEditorSession()
        => new();

    public SelectionReentrancyCoordinator CreateSelectionReentrancyCoordinator()
        => new();

    public GpuSummaryCache CreateGpuSummaryCache()
        => new();

    public RuntimeGpuSummaryApplicationService CreateRuntimeGpuSummaryApplicationService(
        GpuStatusProbeService gpuStatus,
        GpuSummaryCache cache)
        => new(gpuStatus, cache, HostExecutableResolver.WslExe);

    public UiBusyStateController CreateUiBusyStateController()
        => new();

    public TrayWindowStateController CreateTrayWindowStateController()
        => new();

    public RuntimeReadinessMonitorRegistry CreateRuntimeReadinessMonitorRegistry()
        => new();

    public DebouncedAsyncAction CreateLaunchSettingsRefreshAction()
        => new(TimeSpan.FromMilliseconds(120));

    public DebouncedAsyncAction CreateSettingsAutoApplyAction()
        => new(TimeSpan.FromMilliseconds(300));

    public ModelRuntimeStatusController CreateModelRuntimeStatusController(
        ModelRuntimeStatusTracker tracker,
        IUiTimerFactory timerFactory)
        => new(tracker, timerFactory);

    public ModelRuntimeStatusRenderService CreateModelRuntimeStatusRenderService()
        => new();

    public RuntimeLaunchAdmissionService CreateRuntimeLaunchAdmissionService()
        => new(new VramAdmissionService());

    public ModelRuntimeCommandDecisionService CreateModelRuntimeCommandDecisionService()
        => new();

    public OverviewModelGroupLoadPlanningService CreateOverviewModelGroupLoadPlanningService()
        => new();

    public OverviewModelGroupLoadApplicationService CreateOverviewModelGroupLoadApplicationService()
        => new();

    public LaunchRuntimeSelectionService CreateLaunchRuntimeSelectionService()
        => new();

    public ModelRuntimeLoadApplicationService CreateModelRuntimeLoadApplicationService(
        ModelRuntimeCommandDecisionService commands,
        LaunchRuntimeSelectionService runtimeSelection)
        => new(commands, runtimeSelection);

    public ModelRuntimeUnloadApplicationService CreateModelRuntimeUnloadApplicationService(
        ModelRuntimeCommandDecisionService commands)
        => new(commands);

    public ModelFolderApplicationService CreateModelFolderApplicationService()
        => new();

    public ModelImportApplicationService CreateModelImportApplicationService()
        => new();

    public ModelDeletionApplicationService CreateModelDeletionApplicationService()
        => new();

    public ModelGatewayHostFactoryService CreateModelGatewayHostFactoryService()
        => new();

    public ModelGatewayLifecycleApplicationService CreateModelGatewayLifecycleApplicationService()
        => new();

    public ModelGatewayRouteCatalogApplicationService CreateModelGatewayRouteCatalogApplicationService(
        StateStore stateStore)
        => new(stateStore);

    public LaunchSettingsControlStateService CreateLaunchSettingsControlStateService()
        => new();

    public ModelRuntimeLaunchPreparationService CreateModelRuntimeLaunchPreparationService(
        RuntimeSessionCoordinator runtimeSessions,
        RuntimeLaunchPrerequisiteService runtimeLaunchPrerequisites,
        RuntimeLaunchAdmissionService runtimeLaunchAdmission,
        GpuStatusProbeService gpuStatus)
        => new(runtimeSessions, runtimeLaunchPrerequisites, runtimeLaunchAdmission, gpuStatus);

    public LaunchSettingsRenderApplicationService CreateLaunchSettingsRenderApplicationService()
        => new();

    public ModelLaunchHeadSelectionApplicationService CreateModelLaunchHeadSelectionApplicationService()
        => new();

    public ModelLaunchSettingsSaveApplicationService CreateModelLaunchSettingsSaveApplicationService()
        => new();

    public ModelLaunchVariantSaveApplicationService CreateModelLaunchVariantSaveApplicationService()
        => new();

    public GatewayModelLoadWorkflowService CreateGatewayModelLoadWorkflowService(
        StateStore stateStore,
        ModelLaunchProfileService launchProfiles,
        RuntimeSessionCoordinator runtimeSessions)
        => new(stateStore, launchProfiles, runtimeSessions);

    public GatewayRuntimeApplicationService CreateGatewayRuntimeApplicationService(
        GatewayModelLoadWorkflowService workflow)
        => new(workflow);
}
