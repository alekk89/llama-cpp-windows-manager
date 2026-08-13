namespace LocalLlmConsole.Services;

public sealed record MainWindowUiState(
    MainWindowViewModel ViewModel,
    RuntimeCatalogSessionState RuntimeCatalogState,
    LaunchSettingsPanelState LaunchSettingsPanel,
    ModelsPageState ModelsPage,
    OverviewPageState OverviewPage,
    RuntimesPageState RuntimesPage,
    LogsPageState LogsPage,
    LifetimePageState LifetimePage,
    SettingsPageState SettingsPage,
    DownloadHistoryPageState DownloadHistoryPageState,
    RuntimeDashboardPageState RuntimeDashboardPage,
    WindowsPageState WindowsPage,
    WslPageState WslPage,
    EnvironmentPageSnapshotCache EnvironmentPageSnapshots);
