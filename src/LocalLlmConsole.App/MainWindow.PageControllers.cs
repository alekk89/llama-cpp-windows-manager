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
                async () => await ChooseModelsFolderAsync(scanAfter: true),
                () => OpenFolder(_settings.ModelsRoot),
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
                () => OpenFolder(Path.Combine(_workspaceRoot, "logs")),
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
                RunEventAsync));

    private SettingsPageActionController CreateSettingsPageActionController()
        => new(
            new SettingsPageActionControllerActions(
                SaveSettingsAsync,
                PreviewSettingsTheme,
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
                RuntimeBuildPresetFromRowButton,
                RuntimePackagePresetFromRowButton,
                JobFromRowButton,
                AddCustomRuntimeRepositoryFromRowAsync,
                DownloadRuntimeSourceAsync,
                InstallRuntimePackageAsync,
                CheckRuntimePackageUpdateAsync,
                DeleteRuntimePackageBuildsAsync,
                CheckRuntimePresetUpdateAsync,
                DeleteAllRuntimePresetBuildsAsync,
                BuildRuntimeSourceAsync,
                DeleteRuntimeSourceAsync,
                DeleteRuntimeBuildAsync,
                CancelRuntimeBuildJobAsync,
                RetryRuntimeBuildJobAsync,
                ClearRuntimeBuildJobAsync,
                OpenLogPath,
                RunEventAsync));

    private RuntimesPageActionController CreateRuntimesPageActionController(RuntimesPageRowActionController runtimeRows)
        => new(
            new RuntimesPageActionControllerActions(
                async () => await ChooseRuntimeFolderAsync(scanAfter: true),
                async () => await RunEventAsync(ChangeRuntimeCudaPackagePreferenceAsync),
                ToggleAdvancedRuntimes,
                RuntimeGrid_PreviewMouseLeftButtonDown,
                runtimeRows,
                SetRuntimeGridColumnSizing,
                SetRuntimeBuildGridColumnSizing,
                SetRuntimeJobsGridColumnSizing));

    private OverviewPageActionController CreateOverviewPageActionController()
        => new(
            new OverviewPageActionControllerActions(
                SelectOverviewModelSessionAsync,
                SelectOverviewLaunchProfileAsync,
                UpdateOverviewModelActions,
                LoadOverviewSelectedModelAsync,
                SelectLoadedSessionRowAsync,
                LoadedSessionIdFromRowButton,
                UnloadLoadedSessionAsync,
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
