using System.Windows;
namespace LocalLlmConsole;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Loc.LoadLanguage("en");
        AppVersionText.Text = AppVersionLabel;
        ApplyNavigationToggleState(collapsed: false);
        StateChanged += Window_StateChanged;
        SourceInitialized += (_, _) => ConstrainInitialWindowToWorkArea();
        _workspaceRoot = WorkspaceRootResolver.Resolve();
        ApplyLocalizedXamlStrings();
        _serviceFactory = new AppServiceFactory(_workspaceRoot);
        _infrastructureServices = _serviceFactory.CreateMainWindowInfrastructureServices();
        _runtimeLaunchOptionDiscovery = _serviceFactory.CreateRuntimeLaunchOptionDiscoveryService(_infrastructureServices.ProcessRunner);
        _sessions = _infrastructureServices.Sessions;
        _settings = AppSettings.CreateDefault(_workspaceRoot);
        _coreServices = _serviceFactory.CreateMainWindowCoreServices(_infrastructureServices.CoreServiceRequest());
        var uiState = _coreServices.Ui.UiState;
        _viewModel = uiState.ViewModel;
        _runtimeCatalogState = uiState.RuntimeCatalogState;
        _launchSettingsPanel = uiState.LaunchSettingsPanel;
        _modelsPage = uiState.ModelsPage;
        _overviewPage = uiState.OverviewPage;
        _runtimesPage = uiState.RuntimesPage;
        _logsPage = uiState.LogsPage;
        _lifetimePage = uiState.LifetimePage;
        _settingsPage = uiState.SettingsPage;
        _downloadHistoryPageState = uiState.DownloadHistoryPageState;
        _runtimeDashboardPage = uiState.RuntimeDashboardPage;
        _windowsPage = uiState.WindowsPage;
        _wslPage = uiState.WslPage;
        _environmentPageSnapshots = uiState.EnvironmentPageSnapshots;
        _launchSettingsController = new LaunchSettingsPageController(
            _workspaceRoot,
            _launchSettingsPanel,
            _coreServices.Ui,
            _coreServices.Models,
            _coreServices.Runtime,
            new LaunchSettingsPageControllerActions(
                Settings: () => _settings,
                SetSettings: settings => _settings = settings,
                SelectedModel: SelectedModel,
                SelectedProfileId: SelectedModelLaunchProfileId,
                SelectedRuntimeId: SelectedLaunchRuntimeId, SelectedRuntime: () => _launchSettingsPanel.RuntimeCombo?.SelectedItem as RuntimeChoice,
                ModelServices: () => ModelServices,
                RunBusyAsync: RunAsync,
                RunBackground: RunBackground,
                RefreshRuntimeSelectorAsync: runtimeId => RefreshRuntimeSelectorAsync(runtimeId),
                ApplyModelCapabilitiesAsync: ApplyModelCapabilitiesAsync,
                RefreshModelsAsync: RefreshModelsAsync,
                SelectProfileAfterRefresh: SelectLaunchProfileAfterRefresh,
                SelectModelAfterRefresh: SelectModelAfterRefresh,
                RefreshOverviewModelsAsync: RefreshOverviewModelSelectorAsync,
                PersistSettingsAsync: PersistSettingsAsync,
                UpdateControlVisibility: UpdateLaunchControlVisibility,
                UpdateRuntimeCommandPreview: UpdateRuntimeCommandPreview,
                UpdateContextSizeSuggestion: UpdateContextSizeSuggestion,
                NormalizeContextSize: NormalizeContextSizeBox,
                CancelRuntimeOptionDiscovery: CancelRuntimeLaunchOptionDiscovery,
                PickOpenFile: request => _coreServices.App.FileSystemDialogs.PickOpenFile(request, this),
                OpenBenchmarkPlan: OpenBenchmarkPlan,
                ShowModels: ShowModels,
                OpenLog: OpenLogPath,
                SetStatus: SetStatus));
        _overviewSelection = new OverviewSelectionController(
            _viewModel,
            _overviewPage,
            _sessions,
            _coreServices.Runtime,
            _coreServices.Models,
            _coreServices.Ui.SelectionReentrancy,
            new OverviewSelectionControllerActions(
                () => AppServices,
                () => ModelServices,
                () => _settings,
                settings => _activeRuntimeSettings = settings,
                ModelRuntimeUnloadActions,
                SaveActiveRuntimeSessionsAsync,
                RefreshRuntimeMetricsAsync,
                SetStatus,
                this,
                _coreServices.App.Clipboard.SetText));
        _pageControllers = CreatePageControllers();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RunAsync(Loc.T("Status.StartingApp"), async () =>
        {
            await _coreServices.App.StartupApplication.StartAsync(
                new AppStartupApplicationRequest(
                    _workspaceRoot,
                    _serviceFactory.DatabasePath,
                    _serviceFactory.CreateStateStore,
                    stateStore => _serviceFactory.CreateMainWindowLoadedServices(
                        _infrastructureServices.LoadedServiceRequest(stateStore, _coreServices)),
                    CreateLocalControlService),
                new AppStartupApplicationActions(
                    stateStore => _stateStore = stateStore,
                        settings =>
                        {
                            _settings = settings;
                            ApplicationThemeService.Apply(settings.ThemeMode);
                            ApplicationUiScaleService.Apply(settings.UiScalePercent); ApplicationFontScaleService.Apply(settings.FontScalePercent);
                            Loc.LoadLanguage(settings.UiCulture);
                            ApplyLocalizedXamlStrings();
                            PopulateLanguageSelector();
                            ApplyTrayIconVisibilityPreference();
                        },
                    ApplyLoadedServices,
                    service => _service = service,
                    SetStatus));
            await AppServices.UiLayouts.AttachShellAsync(this, PageHost, () => _viewModel.CurrentPage);
            RunBackground(SeedSuggestedLaunchProfilesInBackgroundAsync, "Launch profile seeding follow-up failed");
            ShowOverview(refresh: false);
            await RefreshAllAsync();
            await RecoverActiveRuntimeSessionAsync();
            await AppServices.StartupLaunchProfiles.LoadConfiguredAsync(new(
                (model, profile, token) => EnsureGatewayModelLoadedAsync(new(model, profile), ModelGatewaySwapPolicy.KeepLoaded, token),
                (model, profile) => _sessions.SessionForProfile(model.Id, profile.Id) is { IsRunning: true }, SetStatus));
            await StartModelGatewaySafelyAsync();
            StartGpuEnergyTrackingTimer();
            RunBackground(AutoSelectDetectedWslDistroAsync, "WSL distro auto-select failed");
        });
        await ShowCompletedAppUpdateNoticeAsync();
        RunBackground(CheckForAppUpdatesOnStartupAsync, "App update check failed");
    }

    private void ApplyLoadedServices(MainWindowLoadedServices services)
    {
        _appServices = services.App;
        _modelServices = services.Models;
        _gatewayServices = services.Gateway;
        _runtimeServices = services.Runtime;
        _controlRuntimeOperations = new ControlRuntimeOperationApplicationService(
            new ControlRuntimeOperationDependencies(
                services.App.StateStore,
                _coreServices.Runtime.RuntimeCatalogData,
                services.Runtime.CustomRuntimeRepositories,
                services.Runtime.RuntimeBuildDeletionApplication,
                services.Runtime.RuntimePackageApplication,
                services.Runtime.RuntimeSourceApplication,
                services.Runtime.RuntimeBuildApplication,
                services.Runtime.RuntimeBuildJobApplication,
                _runtimeCatalogState),
            new ControlRuntimeOperationActions(
                () => _settings,
                MaxLogBytes,
                ControlRunBusyAsync,
                RefreshRuntimesAsync,
                SetStatus,
                message => Dispatcher.InvokeAsync(() => SetStatus(message)).Task));
    }

    private async Task SeedSuggestedLaunchProfilesInBackgroundAsync()
    {
        var huggingFace = _appServices?.HuggingFace;
        var result = await _coreServices.App.StartupBackgroundApplication.SeedSuggestedLaunchProfilesAsync(
            new AppStartupSuggestedLaunchProfileSeedRequest(
                _settings,
                huggingFace is null ? null : huggingFace.SeedSuggestedLaunchProfilesAsync));
        if (!result.ShouldRefreshLaunchSettings)
            return;

        await Dispatcher.InvokeAsync(async () =>
            {
                SetStatus(result.StatusMessage);
                await RenderSelectedModelLaunchSettingsAsync();
            });
    }

    private async void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        var controlShutdownConfirmed = Interlocked.Exchange(ref _controlShutdownConfirmed, 0) == 1;
        // Cancel before awaiting so WPF does not continue closing during cleanup.
        e.Cancel = true;
        try
        {
            var result = await _coreServices.App.ShutdownApplication.BeginShutdownAsync(
                new AppShutdownApplicationRequest(
                    _sessions.Snapshots().Count(session => session.IsRunning),
                    _appServices?.HuggingFace.ActiveDownloadCount ?? 0,
                    controlShutdownConfirmed),
                new AppShutdownApplicationActions(
                    confirmation => Task.FromResult(_coreServices.App.Dialogs.Confirm(
                        this,
                        confirmation.Message,
                        confirmation.Title,
                        MessageBoxImage.Warning)),
                    () => IsEnabled = false,
                    SetStatus,
                    ShutdownAsync));
            e.Cancel = result.CancelClosingEvent;
            if (result.RequestClose)
                WindowCloseScheduler.Schedule(Dispatcher, Close);
        }
        catch (Exception ex)
        {
            IsEnabled = true;
            SetStatus($"Shutdown failed: {ex.Message}");
            await WriteAppLogAsync(ex);
            _coreServices.App.Dialogs.Notify(this, ex.Message, "Shutdown failed", MessageBoxImage.Error);
        }
    }

    private async Task ShutdownAsync()
    {
        if (_appServices is not null) await _appServices.UiLayouts.SaveShellAsync();
        var cleanup = await _coreServices.App.ShutdownCleanupApplication.CleanupAsync(new AppShutdownCleanupActions(
            StopDownloadHistoryRefreshTimer: _coreServices.Ui.DownloadHistoryRefreshTimer.Stop,
            StopRuntimeDashboardRefreshTimer: _coreServices.Ui.RuntimeDashboardRefreshTimer.Stop,
            StopGpuEnergyTrackingTimer: _coreServices.Ui.GpuEnergyTrackingTimer.Stop,
            CancelPendingUiWork: CancelPendingUiWork,
            StopRuntimeReadinessMonitor: StopRuntimeReadinessMonitor,
            DisposeTrayIcon: DisposeTrayIcon,
            PauseActiveDownloadsAsync: async () =>
            {
                if (_appServices?.HuggingFace is not null)
                    await _appServices.HuggingFace.PauseActiveDownloadsAsync(TimeSpan.FromSeconds(10));
            },
            DisposeBenchmarkServiceAsync: async () =>
            {
                if (_appServices?.Benchmarks.IsValueCreated == true)
                    await _appServices.Benchmarks.Value.DisposeAsync();
            },
            KillTrackedProcesses: _infrastructureServices.ProcessRunner.KillTrackedProcesses,
            CleanupActiveWslBuildsAsync: CleanupActiveWslBuildsAsync,
            DisposeGatewayAsync: StopModelGatewayAsync,
            DisposeLocalServiceAsync: async () =>
            {
                if (_service is not null)
                {
                    await _service.DisposeAsync();
                    _service = null;
                    _controlApi = null;
                }
            },
            DrainBackgroundTasksAsync: _coreServices.App.BackgroundTasks.DrainAsync,
            StopRuntimeSessionsAsync: _coreServices.Runtime.RuntimeSessions.StopAllAsync,
            DisposeSessions: _sessions.Dispose,
            DisposeHuggingFaceService: () => _appServices?.HuggingFace.Dispose(),
            DisposeAppUpdateService: _infrastructureServices.AppUpdates.Dispose,
            DisposeRuntimePackageClient: _infrastructureServices.RuntimePackageClient.Dispose,
            DisposeMetricsClient: _infrastructureServices.MetricsClient.Dispose,
            DisposeRuntimeProbeClient: _infrastructureServices.RuntimeProbeClient.Dispose,
            ClearActiveRuntimeSettings: () => _activeRuntimeSettings = null,
            ClearActiveRuntimeSession: ClearActiveRuntimeSession,
            DisposeStateStoreAsync: async () =>
            {
                if (_stateStore is not null)
                {
                    await _stateStore.DisposeAsync();
                    _stateStore = null;
                }
            }));

        foreach (var failure in cleanup.Failures)
        {
            var error = new InvalidOperationException(
                $"Shutdown stage '{failure.Stage}' failed: {failure.Exception.Message}",
                failure.Exception);
            try { await WriteAppLogAsync(error); }
            catch (Exception logError) { Trace.TraceWarning($"Could not record shutdown cleanup failure: {logError.Message}"); }
        }
        if (cleanup.CompletedWithWarnings)
            Trace.TraceWarning($"Shutdown completed with {cleanup.Failures.Count} cleanup warning(s).");
    }

}
