using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private const string AppDisplayName = "llama.cpp Windows Manager";
    private const string AppVersionLabel = "v2.4.0";

    private readonly string _workspaceRoot;
    private readonly AppServiceFactory _serviceFactory;
    private readonly MainWindowInfrastructureServices _infrastructureServices;
    private readonly MainWindowCoreServices _coreServices;
    private readonly RuntimeLaunchOptionDiscoveryService _runtimeLaunchOptionDiscovery;
    private CancellationTokenSource? _runtimeLaunchOptionDiscoveryCancellation;
    private StateStore? _stateStore;
    private ILocalAppServiceHost? _service;
    private LocalControlApi? _controlApi;
    private IModelGatewayHost? _gateway;
    private MainWindowLoadedAppServices? _appServices;
    private MainWindowLoadedModelServices? _modelServices;
    private MainWindowLoadedGatewayServices? _gatewayServices;
    private MainWindowLoadedRuntimeServices? _runtimeServices;
    private ControlRuntimeOperationApplicationService? _controlRuntimeOperations;
    private readonly LoadedModelSessionManager _sessions;
    private readonly MainWindowViewModel _viewModel;
    private AppSettings _settings;
    private AppSettings? _activeRuntimeSettings;
    private LlamaProcessSupervisor _llama => _sessions.ActiveSupervisor;
    private readonly RuntimeCatalogSessionState _runtimeCatalogState;
    private readonly LaunchSettingsPanelState _launchSettingsPanel;
    private readonly LaunchSettingsPageController _launchSettingsController;
    private readonly ModelsPageState _modelsPage;
    private readonly OverviewPageState _overviewPage;
    private readonly OverviewSelectionController _overviewSelection;
    private readonly RuntimesPageState _runtimesPage;
    private readonly LogsPageState _logsPage;
    private readonly LifetimePageState _lifetimePage;
    private readonly SemaphoreSlim _lifetimeMetricsRefreshGate = new(1, 1);
    private long _lastLifetimeReportDataVersion = -1;
    private DateTimeOffset _nextLifetimeReportRefreshAt = DateTimeOffset.MinValue;
    private readonly MinimizedUiRefreshPolicy _minimizedUiRefreshPolicy = new();
    private readonly SettingsPageState _settingsPage;
    private Task<long>? _settingsCacheSizeRefreshTask;
    private string _settingsCacheSizeRoot = "";
    private long? _settingsCacheSizeBytes;
    private int _settingsPageVersion;
    private readonly DownloadHistoryPageState _downloadHistoryPageState;
    private readonly RuntimeDashboardPageState _runtimeDashboardPage;
    private readonly WindowsPageState _windowsPage;
    private readonly WslPageState _wslPage;
    private readonly MainWindowPageControllers _pageControllers;
    private readonly EnvironmentPageSnapshotCache _environmentPageSnapshots;
    private Forms.NotifyIcon? _trayIcon;
    private int _controlShutdownConfirmed;

    private MainWindowLoadedAppServices AppServices
        => _appServices ?? throw new InvalidOperationException("Loaded app services are not initialized.");

    private MainWindowLoadedModelServices ModelServices
        => _modelServices ?? throw new InvalidOperationException("Loaded model services are not initialized.");

    private MainWindowLoadedGatewayServices GatewayServices
        => _gatewayServices ?? throw new InvalidOperationException("Loaded gateway services are not initialized.");

    private MainWindowLoadedRuntimeServices RuntimeServices
        => _runtimeServices ?? throw new InvalidOperationException("Loaded runtime services are not initialized.");
}
