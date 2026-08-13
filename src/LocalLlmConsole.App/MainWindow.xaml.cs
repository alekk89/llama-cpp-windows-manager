using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppVersionText.Text = AppVersionLabel;
        ApplyStaticButtonToolTips();
        StateChanged += Window_StateChanged;
        _workspaceRoot = WorkspaceRootResolver.Resolve();
        Loc.LoadLanguage("en");
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
        _pageControllers = CreatePageControllers();
        InitializeTrayIcon();
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
                            ApplyTheme(settings.ThemeMode);
                            Loc.LoadLanguage(settings.UiCulture);
                            ApplyLocalizedXamlStrings();
                            PopulateLanguageSelector();
                        },
                    ApplyLoadedServices,
                    service => _service = service,
                    SetStatus));
            RunBackground(SeedSuggestedLaunchProfilesInBackgroundAsync, "Launch profile seeding follow-up failed");
            ShowOverview();
            await RefreshAllAsync();
            await RecoverActiveRuntimeSessionAsync();
            await StartModelGatewaySafelyAsync();
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
        // Cancel synchronously before the first await. Otherwise WPF can continue
        // closing the window while cleanup is still running, and the follow-up
        // Close() below then throws because the window is already closing.
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
                Close();
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
        => await _coreServices.App.ShutdownCleanupApplication.CleanupAsync(new AppShutdownCleanupActions(
            _coreServices.Ui.DownloadHistoryRefreshTimer.Stop,
            _coreServices.Ui.RuntimeDashboardRefreshTimer.Stop,
            CancelLaunchSettingsRefresh,
            StopRuntimeReadinessMonitor,
            DisposeTrayIcon,
            async () =>
            {
                if (_appServices?.HuggingFace is not null)
                    await _appServices.HuggingFace.PauseActiveDownloadsAsync(TimeSpan.FromSeconds(10));
            },
            _infrastructureServices.ProcessRunner.KillTrackedProcesses,
            CleanupActiveWslBuildsAsync,
            StopModelGatewayAsync,
            _coreServices.Runtime.RuntimeSessions.StopAllAsync,
            _sessions.Dispose,
            _infrastructureServices.RuntimePackageClient.Dispose,
            _infrastructureServices.MetricsClient.Dispose,
            _infrastructureServices.RuntimeProbeClient.Dispose,
            () => _activeRuntimeSettings = null,
            ClearActiveRuntimeSession,
            async () =>
            {
                if (_service is not null)
                {
                    await _service.DisposeAsync();
                    _service = null;
                    _controlApi = null;
                }
            },
            async () =>
            {
                if (_stateStore is not null)
                {
                    await _stateStore.DisposeAsync();
                    _stateStore = null;
                }
            }));

}
