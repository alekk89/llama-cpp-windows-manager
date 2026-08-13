namespace LocalLlmConsole.Services;

public sealed partial class AppServiceFactory
{
    public StateStore CreateStateStore()
        => new(DatabasePath);

    public AppUpdateService CreateAppUpdateService()
        => new(CreateAppUpdateHttpClient(), StartProcess);

    public HttpClient CreateAppUpdateHttpClient()
        => new();

    private static void StartProcess(ProcessStartInfo processStartInfo)
        => Process.Start(processStartInfo);

    public LoadedModelSessionManager CreateLoadedModelSessionManager()
        => CreateLoadedModelSessionManager(CreateProcessRunner());

    public LoadedModelSessionManager CreateLoadedModelSessionManager(IProcessRunner processRunner)
        => new(() => CreateLlamaProcessSupervisor(
            CreateWslRuntimeStopService(processRunner),
            CreateNativeRuntimeStopService()));

    public LlamaProcessSupervisor CreateLlamaProcessSupervisor(
        WslRuntimeStopService wslRuntimeStop,
        NativeRuntimeStopService nativeRuntimeStop)
        => new(wslRuntimeStop, nativeRuntimeStop);

    public WslRuntimeStopService CreateWslRuntimeStopService(IProcessRunner processRunner)
        => new(processRunner);

    public NativeRuntimeStopService CreateNativeRuntimeStopService()
        => new();

    public TrackedProcessRunner CreateProcessRunner()
        => new();

    public WindowsEnvironmentService CreateWindowsEnvironmentService()
        => new();

    public WslEnvironmentService CreateWslEnvironmentService()
        => new();

    public HttpClient CreateRuntimeProbeClient()
        => new() { Timeout = TimeSpan.FromSeconds(1.5) };

    public HttpClient CreateRuntimeMetricsClient()
        => new() { Timeout = TimeSpan.FromSeconds(5) };

    public HttpClient CreateRuntimePackageClient()
        => new() { Timeout = TimeSpan.FromMinutes(60) };

    public StateStoreInitializationService CreateStateStoreInitializationService()
        => new();

    public MainWindowUiState CreateMainWindowUiState()
        => new(
            new MainWindowViewModel(),
            new RuntimeCatalogSessionState(),
            new LaunchSettingsPanelState(),
            new ModelsPageState(),
            new OverviewPageState(),
            new RuntimesPageState(),
            new LogsPageState(),
            new LifetimePageState(),
            new SettingsPageState(),
            new DownloadHistoryPageState(),
            new RuntimeDashboardPageState(),
            new WindowsPageState(),
            new WslPageState(),
            new EnvironmentPageSnapshotCache());

    public BackgroundTaskApplicationService CreateBackgroundTaskApplicationService()
        => new();

    public ForegroundTaskApplicationService CreateForegroundTaskApplicationService()
        => new();

    public ShellIntegrationService CreateShellIntegrationService()
        => new(StartProcess);

    public GpuStatusProbeService CreateGpuStatusProbeService(IProcessRunner processRunner)
        => new(processRunner);

    public FileSystemDialogService CreateFileSystemDialogService()
        => new(FileSystemDialogService.ShowFolderDialog, FileSystemDialogService.ShowOpenFileDialog);

    public ClipboardService CreateClipboardService()
        => new(SetClipboardText);

    private static void SetClipboardText(string text)
        => System.Windows.Clipboard.SetText(text);

    public DialogService CreateDialogService()
        => new(ThemedMessageBox.Show);

    public DownloadCompletionApplicationService CreateDownloadCompletionApplicationService()
        => new();

    public AppStartupApplicationService CreateAppStartupApplicationService(
        StateStoreInitializationService stateStoreInitialization,
        LocalAppServiceStartupService localAppStartup,
        RuntimeBuildMarkerService runtimeBuildMarkers,
        WindowsStartupRegistrationService startupRegistration)
        => new(stateStoreInitialization, localAppStartup, runtimeBuildMarkers, startupRegistration);

    public WindowsStartupRegistrationService CreateWindowsStartupRegistrationService()
        => new();

    public AppStartupBackgroundApplicationService CreateAppStartupBackgroundApplicationService()
        => new();

    public AppSettingsUpdateService CreateAppSettingsUpdateService()
        => new();

    public AppUpdateWorkflowService CreateAppUpdateWorkflowService(AppUpdateService updates)
        => new(updates, _workspaceRoot);

    public AppUpdateApplicationService CreateAppUpdateApplicationService()
        => new();

    public CacheClearApplicationService CreateCacheClearApplicationService()
        => new();

    public HuggingFaceModelCardApplicationService CreateHuggingFaceModelCardApplicationService()
        => new();

    public HuggingFaceSearchApplicationService CreateHuggingFaceSearchApplicationService()
        => new();

    public HuggingFaceDownloadApplicationService CreateHuggingFaceDownloadApplicationService()
        => new();

    public SettingsRowActionApplicationService CreateSettingsRowActionApplicationService()
        => new();

    public FolderSettingsApplicationService CreateFolderSettingsApplicationService()
        => new();

    public AppLogApplicationService CreateAppLogApplicationService()
        => new(_workspaceRoot);

    public LifetimeMetricResetApplicationService CreateLifetimeMetricResetApplicationService()
        => new();

    public AppShutdownDecisionService CreateAppShutdownDecisionService()
        => new();

    public AppShutdownStateController CreateAppShutdownStateController()
        => new();

    public AppShutdownApplicationService CreateAppShutdownApplicationService(
        AppShutdownDecisionService decisions,
        AppShutdownStateController state)
        => new(decisions, state);

    public AppShutdownCleanupApplicationService CreateAppShutdownCleanupApplicationService()
        => new();

    public AppSettingsWorkflowService CreateAppSettingsWorkflowService(
        StateStore stateStore,
        AppSettingsUpdateService updates)
        => new(stateStore, updates, _workspaceRoot);

    public AppSettingsApplicationService CreateAppSettingsApplicationService(
        AppSettingsWorkflowService settingsWorkflow,
        WindowsStartupRegistrationService startupRegistration)
        => new(settingsWorkflow, startupRegistration);

    public SettingsPageDefinitionService CreateSettingsPageDefinitionService()
        => new();

    public HelpSectionService CreateHelpSectionService()
        => new();

    public HelpNavigationApplicationService CreateHelpNavigationApplicationService()
        => new();

    public CacheClearWorkflowService CreateCacheClearWorkflowService(StateStore stateStore)
        => new(_workspaceRoot, stateStore);

    public LogPageWorkflowService CreateLogPageWorkflowService(StateStore stateStore)
        => new(_workspaceRoot, stateStore);

    public LogPageApplicationService CreateLogPageApplicationService(LogPageWorkflowService workflow)
        => new(workflow);

    public LifetimeMetricsApplicationService CreateLifetimeMetricsApplicationService(StateStore stateStore)
        => new(stateStore);

    public ModelLookupApplicationService CreateModelLookupApplicationService(StateStore stateStore)
        => new(stateStore);

    public ActiveRuntimeSessionStore CreateActiveRuntimeSessionStore()
        => new(_workspaceRoot);
}
