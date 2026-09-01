namespace LocalLlmConsole;

public sealed record MainWindowPageControllers(
    ModelsPageActionController Models,
    ModelsPageRowActionController ModelRows,
    DownloadHistoryRowActionController DownloadHistoryRows,
    RuntimesPageActionController Runtimes,
    RuntimesPageRowActionController RuntimeRows,
    WindowsPageActionController Windows,
    WslPageActionController Wsl,
    OverviewPageActionController Overview,
    LogsPageActionController Logs,
    LifetimePageActionController Lifetime,
    SettingsPageActionController Settings);

public partial class MainWindow
{
    private MainWindowPageControllers CreatePageControllers()
    {
        var modelRows = CreateModelsPageRowActionController();
        var runtimeRows = CreateRuntimesPageRowActionController();

        return new MainWindowPageControllers(
            CreateModelsPageActionController(modelRows),
            modelRows,
            CreateDownloadHistoryRowActionController(),
            CreateRuntimesPageActionController(runtimeRows),
            runtimeRows,
            CreateWindowsPageActionController(),
            CreateWslPageActionController(),
            CreateOverviewPageActionController(),
            CreateLogsPageActionController(),
            CreateLifetimePageActionController(),
            CreateSettingsPageActionController());
    }

    private ModelsPageRowActionController CreateModelsPageRowActionController()
        => new(
            _coreServices.Models.ModelFolderApplication,
            _coreServices.HuggingFaceServices.HuggingFaceModelCards,
            new ModelsPageRowActionControllerActions(
                ModelFromRowButton,
                ModelRowFromButton,
                ModelFolderActions,
                DeleteModelRowAsync,
                StartHuggingFaceDownloadAsync,
                HuggingFaceModelCardActions,
                RunEventAsync));

    private ModelsPageActionController CreateModelsPageActionController(ModelsPageRowActionController modelRows)
        => new(
            new ModelsPageActionControllerActions(
                ScanModelsFolderAsync,
                ImportModelFileAsync,
                async () => await ChooseModelsFolderAsync(scanAfter: true),
                () => _coreServices.App.ShellIntegration.OpenFolder(_settings.ModelsRoot),
                ManageModelGroupsAsync,
                AssignLaunchProfileGroupAsync,
                RemoveLaunchProfileGroupAsync,
                async model => { await AppServices.StateStore.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.Model, model.Id); await RefreshModelsAsync(); },
                ToggleTrayProfileFavoriteAsync,
                async (_, profile) => { await AppServices.StartupLaunchProfiles.ToggleLoadOnStartupAsync(profile.Id); await RefreshModelsAsync(); },
                LoadLaunchProfileAsync,
                BeginNewLaunchProfile,
                SelectModelGridRow,
                modelRows,
                SearchHuggingFaceAsync,
                async () => await ShowDownloadHistoryAsync(),
                SetModelGridColumnSizing));

    private LogsPageActionController CreateLogsPageActionController()
        => new(
            new LogsPageActionControllerActions(
                RefreshLogsAsync,
                OpenSelectedLogFile,
                () => _coreServices.App.ShellIntegration.OpenFolder(Path.Combine(_workspaceRoot, "logs")),
                CreateDiagnosticsBundleAsync,
                DeleteSelectedLogAsync,
                DeleteAllLogsAsync,
                OpenLogPath,
                DeleteLogPathAsync,
                LogPathFromRow,
                LoadSelectedLog,
                RunEventAsync));

    private LifetimePageActionController CreateLifetimePageActionController()
        => new(
            new LifetimePageActionControllerActions(
                ResetLifetimeMetricAsync,
                ResetVisibleLifetimeMetricAsync,
                LifetimeRangeChangedAsync,
                LifetimeFiltersChangedAsync,
                LifetimeFiltersChangedAsync,
                ClearLifetimeDateSelectionAsync,
                () => _lifetimePage.IsApplying,
                RunEventAsync));

    private SettingsPageActionController CreateSettingsPageActionController()
        => new(
            new SettingsPageActionControllerActions(
                PreviewSettingsTheme,
                ScheduleSettingsApply,
                SettingRowFromSender,
                RunSettingsRowActionAsync,
                ToggleSettingsSecret,
                CopySettingsSecret,
                RunEventAsync));

    private DownloadHistoryRowActionController CreateDownloadHistoryRowActionController()
        => new(
            new DownloadHistoryRowActionControllerActions(
                JobFromRowButton,
                ResumeDownloadAsync,
                PauseDownloadAsync,
                StopDownloadAsync,
                DeleteDownloadAsync,
                RunEventAsync));

    private RuntimesPageRowActionController CreateRuntimesPageRowActionController()
        => new(
            new RuntimesPageRowActionControllerActions(
                RuntimeFromRowButton,
                RuntimeSourceFromRowButton,
                RuntimePackagePresetFromRowButton,
                RunRuntimeSourceRowActionAsync,
                InstallRuntimePackageAsync,
                CheckRuntimePackageUpdateAsync,
                DeleteRuntimeDownloadRowAsync,
                DeleteRuntimeSourceAsync,
                DeleteRuntimeBuildAsync,
                VerifyRuntimeInstallationAsync,
                RunEventAsync));

    private RuntimesPageActionController CreateRuntimesPageActionController(RuntimesPageRowActionController runtimeRows)
        => new(
            new RuntimesPageActionControllerActions(
                async () => await ChooseRuntimeFolderAsync(scanAfter: true),
                async () => await RunEventAsync(ChangeRuntimeCudaPackagePreferenceAsync),
                async runtime => { await AppServices.StateStore.ToggleSelectorFavoriteAsync(SelectorFavoriteKind.Runtime, runtime.Id); await RefreshRuntimesAsync(); },
                runtimeRows,
                SetRuntimeGridColumnSizing,
                SetRuntimeBuildGridColumnSizing));

    private OverviewPageActionController CreateOverviewPageActionController()
        => new(
            new OverviewPageActionControllerActions(
                SelectOverviewModelSessionAsync,
                SelectOverviewLaunchProfileAsync,
                UpdateOverviewModelActions,
                LoadOverviewSelectedModelAsync,
                () => _coreServices.Ui.SelectionReentrancy.IsLoadedSessionSelectionChanging,
                SelectLoadedSessionRowAsync,
                InspectSelectedOverviewEndpointAsync,
                EndpointRowFromLink,
                InspectOverviewEndpointRowAsync,
                LoadedSessionIdFromRowButton,
                _overviewSelection.UnloadSessionAsync,
                PersistOverviewDashboardLayoutAsync,
                RunEventAsync));

    private WslPageActionController CreateWslPageActionController()
        => new(
            new WslPageActionControllerActions(
                RefreshWslLinuxAsync,
                InstallWslAsync,
                CheckWslUpdatesAsync,
                DeleteWslAsync,
                InstallWslUbuntuAsync,
                CheckUbuntuUpdatesAsync,
                DeleteUbuntuAsync,
                InstallUbuntuBuildToolsAsync,
                DeleteUbuntuBuildToolsAsync,
                InstallUbuntuCudaToolkitAsync,
                DeleteUbuntuCudaToolkitAsync,
                InstallUbuntuVulkanToolsAsync,
                DeleteUbuntuVulkanToolsAsync,
                InstallUbuntuSyclRuntimeAsync,
                DeleteUbuntuSyclRuntimeAsync,
                InstallUbuntuSyclOneApiAsync,
                DeleteUbuntuSyclOneApiAsync,
                SelectWslDistroAsync,
                RunEventAsync));

    private WindowsPageActionController CreateWindowsPageActionController()
        => new(
            new WindowsPageActionControllerActions(
                RefreshWindowsAsync,
                InstallWindowsCpuToolsAsync,
                InstallWindowsCudaToolkitAsync,
                InstallWindowsVulkanToolsAsync,
                InstallWindowsSyclToolsAsync));
}
