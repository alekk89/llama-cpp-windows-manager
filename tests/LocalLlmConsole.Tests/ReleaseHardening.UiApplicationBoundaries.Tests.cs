using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using LocalLlmConsole.Localization;
using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void FolderSettingsWorkflowStaysOutOfMainWindow()
    {
        var folderSettings = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.FolderSettings.cs"));
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "FolderSettingsApplicationService.cs"));
        var dialogs = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "FileSystemDialogService.cs"));

        Assert.Contains("_coreServices.App.FolderSettingsApplication.ChooseModelsFolderAsync", folderSettings, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.FolderSettingsApplication.ChooseRuntimeFolderAsync", folderSettings, StringComparison.Ordinal);
        Assert.Contains("FolderSettingsActions()", folderSettings, StringComparison.Ordinal);
        Assert.Contains("initial => _coreServices.App.FileSystemDialogs.PickFolder(initial)", folderSettings, StringComparison.Ordinal);
        Assert.Contains("Forms.FolderBrowserDialog", dialogs, StringComparison.Ordinal);
        Assert.Contains("Models folder set to", application, StringComparison.Ordinal);
        Assert.Contains("Runtimes folder set to", application, StringComparison.Ordinal);
        Assert.DoesNotContain("Path.GetFullPath(folder)", folderSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("FolderBrowserDialog", folderSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Exists(initial)", folderSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Changing models folder...\"", folderSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Changing runtimes folder...\"", folderSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Models folder set to", folderSettings, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Runtimes folder set to", folderSettings, StringComparison.Ordinal);
    }


    [Fact]
    public void ToolSetupCommandPolicyStaysOutOfMainWindow()
    {
        var windows = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Windows.cs"));
        var wsl = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.WslActions.cs"));
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Environment", "ToolSetupApplicationService.cs"));

        Assert.Contains("_coreServices.Environment.WindowsToolSetupApplication.Run", windows, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Environment.WslToolSetupApplication.Run", wsl, StringComparison.Ordinal);
        Assert.Contains("Install or select an Ubuntu distro first.", application, StringComparison.Ordinal);
        Assert.DoesNotContain("_windowsToolSetupWorkflow.Plan", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("_windowsToolSetupWorkflow.Execute", windows, StringComparison.Ordinal);
        Assert.DoesNotContain("_wslToolSetupWorkflow.RequiresUbuntuDistro", wsl, StringComparison.Ordinal);
        Assert.DoesNotContain("_wslToolSetupWorkflow.Plan", wsl, StringComparison.Ordinal);
        Assert.DoesNotContain("_wslToolSetupWorkflow.Execute", wsl, StringComparison.Ordinal);
        Assert.DoesNotContain("Install or select an Ubuntu distro first.", wsl, StringComparison.Ordinal);
    }


    [Fact]
    public void LifetimeMetricResetPolicyStaysOutOfMainWindow()
    {
        var lifetime = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RefreshAndLifetime.cs"));
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "LifetimeMetricResetApplicationService.cs"));
        var metricsApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "LifetimeMetricsApplicationService.cs"));

        Assert.Contains("_coreServices.App.LifetimeMetricResetApplication.ResetAsync", lifetime, StringComparison.Ordinal);
        Assert.Contains("LifetimeMetricResetActions()", lifetime, StringComparison.Ordinal);
        Assert.Contains("AppServices.LifetimeMetricsApplication", lifetime, StringComparison.Ordinal);
        Assert.Contains("electricityTariff: ElectricityTariffPolicy.FromSettings(_settings)", lifetime, StringComparison.Ordinal);
        Assert.Contains("lifetimeMetrics.DeleteModelUsageAsync(modelId)", lifetime, StringComparison.Ordinal);
        Assert.Contains("lifetimeMetrics.DeleteAllUsageAsync()", lifetime, StringComparison.Ordinal);
        Assert.Contains("Reset usage metrics for all models?", application, StringComparison.Ordinal);
        Assert.Contains("Only model rows can be reset individually.", application, StringComparison.Ordinal);
        Assert.Contains("_stateStore.RecordTokenUsageAsync(delta)", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_stateStore.ListTokenUsageAsync()", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_stateStore.ListTokenUsageBucketsAsync", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_stateStore.DeleteTokenUsageAsync(modelId)", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_stateStore.DeleteAllTokenUsageAsync()", metricsApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("_stateStore.RecordTokenUsageAsync", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("_stateStore.ListTokenUsageAsync()", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("_stateStore.DeleteTokenUsageAsync", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("_stateStore.DeleteAllTokenUsageAsync", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("row.Data[\"Kind\"]", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("Reset lifetime token metrics for all models?", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("Only model rows can be reset individually.", lifetime, StringComparison.Ordinal);
    }


    [Fact]
    public void ModelCatalogRefreshCompositionStaysOutOfMainWindow()
    {
        var lifetime = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RefreshAndLifetime.cs"));
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Models", "ModelCatalogRefreshApplicationService.cs"));

        Assert.Contains("ModelServices.ModelCatalogRefreshApplication", lifetime, StringComparison.Ordinal);
        Assert.Contains("modelRefresh.RefreshAsync(ModelCatalogRefreshActions())", lifetime, StringComparison.Ordinal);
        Assert.Contains("result.NamedLaunchProfiles", lifetime, StringComparison.Ordinal);
        Assert.Contains("_catalog.CleanupModelRecordsAsync()", application, StringComparison.Ordinal);
        Assert.Contains("_stateStore.ListModelsAsync()", application, StringComparison.Ordinal);
        Assert.DoesNotContain("CleanupModelRecordsAsync", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("ListModelsAsync()", lifetime, StringComparison.Ordinal);
        Assert.DoesNotContain("new Dictionary<string, ModelLaunchSettings>", lifetime, StringComparison.Ordinal);
    }


    [Fact]
    public void HuggingFaceSearchKeepsDownloadActionVisibleAndSwitchesToHistory()
    {
        var source = ReadMainWindowSources();
        var downloadHistorySource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.DownloadHistory.cs"));
        var downloadHistoryWorkflow = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "HuggingFace", "DownloadHistoryWorkflowService.cs"));
        var downloadHistoryApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "HuggingFace", "DownloadHistoryApplicationService.cs"));
        var searchApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "HuggingFace", "HuggingFaceSearchApplicationService.cs"));
        var downloadApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "HuggingFace", "HuggingFaceDownloadApplicationService.cs"));
        var gridModeFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "HuggingFaceGridModeFactory.cs"));

        Assert.Contains("_coreServices.HuggingFaceServices.HuggingFaceSearchApplication.SearchAsync", source, StringComparison.Ordinal);
        Assert.Contains("HuggingFaceSearchActions(", source, StringComparison.Ordinal);
        Assert.Contains("actions.ConfigureSearchGrid()", searchApplication, StringComparison.Ordinal);
        Assert.Contains("actions.ApplySearchResults(results, installed, settings.ModelsRoot)", searchApplication, StringComparison.Ordinal);
        Assert.Contains("_coreServices.HuggingFaceServices.HuggingFaceDownloadApplication.StartAsync", source, StringComparison.Ordinal);
        Assert.Contains("HuggingFaceDownloadActions(", source, StringComparison.Ordinal);
        Assert.Contains("await actions.ShowDownloadHistoryAsync(job.Id)", downloadApplication, StringComparison.Ordinal);
        Assert.Contains("actions.StartMonitor(job.Id)", downloadApplication, StringComparison.Ordinal);
        Assert.Contains("Download started: {file.Name} ({job.Id})", downloadApplication, StringComparison.Ordinal);
        Assert.Contains("SelectDownloadHistoryJob", source, StringComparison.Ordinal);
        Assert.Contains("_modelsPage.UseHuggingFaceSearchGrid()", source, StringComparison.Ordinal);
        Assert.Contains("_modelsPage.UseDownloadHistoryGrid()", source, StringComparison.Ordinal);
        Assert.Contains("HuggingFaceGridModeFactory.ConfigureSearch(HuggingFaceGridModeRequest(grid))", source, StringComparison.Ordinal);
        Assert.Contains("HuggingFaceGridModeFactory.ConfigureDownloadHistory(HuggingFaceGridModeRequest(grid))", source, StringComparison.Ordinal);
        Assert.Contains("_downloadHistoryPageState.ShowSearch()", source, StringComparison.Ordinal);
        Assert.Contains("_downloadHistoryPageState.ShowHistory()", source, StringComparison.Ordinal);
        Assert.Contains("_downloadHistoryPageState.TryBeginTimerRefresh", source, StringComparison.Ordinal);
        Assert.Contains("_downloadHistoryPageState.CompleteTimerRefresh", source, StringComparison.Ordinal);
        Assert.Contains("public async Task<DownloadHistoryApplicationOutcome> ShowAsync", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("public async Task<DownloadHistoryTimerRefreshOutcome> RefreshTimerAsync", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("actions.ConfigureHistoryGrid()", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("actions.TryBeginRefresh()", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("actions.CompleteRefresh()", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("_downloadHistoryPageState.IsShowingHistory", downloadHistorySource, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.DownloadHistoryRefreshTimer.Start(", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.DownloadHistoryRefreshTimer.Stop()", source, StringComparison.Ordinal);
        Assert.Contains("DownloadHistoryTimerRefreshAsync", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.DownloadCompletionApplication.MonitorAsync(", source, StringComparison.Ordinal);
        Assert.Contains("new DownloadCompletionApplicationActions(", source, StringComparison.Ordinal);
        Assert.Contains("RunDownloadCompletionOnUiThreadAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshCompletedDownloadAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_hfShowingDownloadHistory", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_downloadHistoryRefreshInFlight", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_downloadHistoryTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DownloadHistoryTimer_Tick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_viewModel.HuggingFace.ReplaceSearchResults(await huggingFace", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStatus($\"Download started:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_hfQueryBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_hfGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_downloadHistoryGrid", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[1].Width = new DataGridLength(1.85, DataGridLengthUnitType.Star)", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[5].Width = new DataGridLength(1.05, DataGridLengthUnitType.Star)", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[6].MinWidth = 96", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[6].Width = new DataGridLength(104)", source, StringComparison.Ordinal);
        Assert.Contains("grid.Columns[7].Width = new DataGridLength(74)", source, StringComparison.Ordinal);
        Assert.Contains("PageSectionFactory.AddButtonColumn(request.Grid, Loc.T(\"HfSearch.Col.Actions\"), \"C7\", \"B1\", request.Actions.DownloadSearchRow", gridModeFactory, StringComparison.Ordinal);
        Assert.Contains("PageSectionFactory.AddButtonColumn(request.Grid, Loc.T(\"Common.DeleteButton\"), \"C10\", \"B4\", request.Actions.DeleteDownloadRow", gridModeFactory, StringComparison.Ordinal);
        Assert.Contains("var downloadHistory = AppServices.DownloadHistoryApplication;", source, StringComparison.Ordinal);
        Assert.Contains("await downloadHistory!.DeleteAsync(job, _settings, DownloadHistoryDeleteActions())", source, StringComparison.Ordinal);
        Assert.Contains("await downloadHistory!.ResumeAsync(job, _settings, DownloadHistoryCommandActions())", source, StringComparison.Ordinal);
        Assert.Contains("await downloadHistory!.PauseAsync(job, DownloadHistoryCommandActions())", source, StringComparison.Ordinal);
        Assert.Contains("await downloadHistory!.StopAsync(job, DownloadHistoryCommandActions())", source, StringComparison.Ordinal);
        Assert.Contains("await downloadHistory!.ShowAsync(", source, StringComparison.Ordinal);
        Assert.Contains("await downloadHistory.RefreshTimerAsync(", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class DownloadHistoryApplicationService", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("var deletePlan = _workflow.BuildDeletePlan(job)", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("await _workflow.ResumeAsync(job, settings)", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("await _workflow.PauseAsync(job)", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.Contains("await _workflow.StopAsync(job)", downloadHistoryApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("_downloadHistoryWorkflow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppServices.HuggingFace!.ResumeDownloadAsync(job, _settings)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppServices.HuggingFace!.PauseDownloadAsync(job)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppServices.HuggingFace!.StopDownloadAsync(job)", source, StringComparison.Ordinal);
        Assert.Contains("DeletePartialFile", downloadHistoryWorkflow, StringComparison.Ordinal);
        Assert.Contains("Completed model files are kept.", downloadHistoryWorkflow, StringComparison.Ordinal);
        Assert.Contains("if (grid.Columns.Count < 10) return;", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowExposesAppUpdatesAndCacheClearing()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var themedMessageBox = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "ThemedMessageBox.cs"));
        var settingsDefinitions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "SettingsPageDefinitionService.cs"));
        var settingsPageState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Settings", "SettingsPageState.cs"));
        var settingsPersistence = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.SettingsPersistence.cs"));
        var updatesPageFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Updates", "UpdatesPageFactory.cs"));

        Assert.Contains("x:Name=\"UpdatesNavButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HelpNavButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WindowsNavButton\"", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"ToolsNavLabel\"", xaml, StringComparison.Ordinal);
        Assert.True(xaml.IndexOf("x:Name=\"AppStatusText\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"UpdatesNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"LogsNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"ToolsNavLabel\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"ToolsNavLabel\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"WindowsNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"WindowsNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"WslLinuxNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"LogsNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"UpdatesNavButton\"", StringComparison.Ordinal));
        Assert.True(xaml.IndexOf("x:Name=\"UpdatesNavButton\"", StringComparison.Ordinal) < xaml.IndexOf("x:Name=\"HelpNavButton\"", StringComparison.Ordinal));
        Assert.Contains("CheckForAppUpdatesOnStartupAsync", source, StringComparison.Ordinal);
        Assert.Contains("InstallAppUpdateAsync", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.SettingsPageDefinitions.BuildRows(_settings, knownCacheSize)", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SettingsPageState _settingsPage;", source, StringComparison.Ordinal);
        Assert.Contains("_settingsPage = uiState.SettingsPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly SettingsPageState _settingsPage = new();", source, StringComparison.Ordinal);
        Assert.Contains("_settingsPage.Apply(", source, StringComparison.Ordinal);
        Assert.Contains("Rows.ToDictionary", settingsPersistence, StringComparison.Ordinal);
        Assert.Contains("ScheduleSettingsApply", settingsPersistence, StringComparison.Ordinal);
        Assert.Contains("public sealed class SettingsPageState", settingsPageState, StringComparison.Ordinal);
        Assert.Contains("public string SelectedThemeValue", settingsPageState, StringComparison.Ordinal);
        Assert.DoesNotContain("_themeCombo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CacheMaintenanceService.Size(", settingsDefinitions, StringComparison.Ordinal);
        Assert.Contains("CacheMaintenanceService.SizeAsync(cacheRoot)", source, StringComparison.Ordinal);
        Assert.Contains("sender is EditableSettingRow { Type: \"readonly\" }", settingsPageState, StringComparison.Ordinal);
        Assert.Contains("ClearCacheAsync", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.CacheClearApplication.ClearAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CacheClearPlanStatus.", source, StringComparison.Ordinal);
        Assert.Contains("<RepositoryUrl>https://github.com/alekk89/llama-cpp-windows-manager</RepositoryUrl>", project, StringComparison.Ordinal);

        Assert.Contains("UpdatesPageFactory.Create(new UpdatesPageRequest(", source, StringComparison.Ordinal);
        Assert.True(
            updatesPageFactory.IndexOf("actions.Children.Add(Button(request.ViewModel.ActionText", StringComparison.Ordinal)
            < updatesPageFactory.IndexOf("Loc.T(\"Updates.StatusSectionTitle\")", StringComparison.Ordinal));
        Assert.DoesNotContain("FramedSection(\"Update Status\"", source, StringComparison.Ordinal);
        Assert.Contains("MaxHeight = DialogMaxHeight(owner)", themedMessageBox, StringComparison.Ordinal);
        Assert.Contains("DialogMessageMaxHeight", themedMessageBox, StringComparison.Ordinal);
        Assert.Contains("VerticalScrollBarVisibility = ScrollBarVisibility.Auto", themedMessageBox, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowDialogCallsGoThroughDialogService()
    {
        var source = ReadMainWindowSources();
        var dialogs = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "DialogService.cs"));
        var factory = ReadAppServiceFactorySources();

        Assert.Contains("public sealed class DialogService", dialogs, StringComparison.Ordinal);
        Assert.Contains("ThemedMessageBox.Show", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemedMessageBox.Show", dialogs, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.Dialogs.Confirm", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.Dialogs.Notify", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ThemedMessageBox.Show", source, StringComparison.Ordinal);
    }


    [Fact]
    public void AppStartupSingleInstanceNoticeUsesServices()
    {
        var app = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml.cs"));
        var singleInstance = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "SingleInstanceApplicationService.cs"));

        Assert.Contains("private readonly SingleInstanceApplicationService _singleInstance = new(SingleInstanceApplicationService.AcquireMutexLease);", app, StringComparison.Ordinal);
        Assert.Contains("private readonly DialogService _dialogs = new(ThemedMessageBox.Show);", app, StringComparison.Ordinal);
        Assert.Contains("_singleInstance.TryAcquire(SingleInstanceMutexName)", app, StringComparison.Ordinal);
        Assert.Contains("_dialogs.Notify(null, \"llama.cpp Windows Manager is already running.\"", app, StringComparison.Ordinal);
        Assert.Contains("_singleInstance.Dispose();", app, StringComparison.Ordinal);
        Assert.DoesNotContain("new Mutex(", app, StringComparison.Ordinal);
        Assert.Contains("public sealed class SingleInstanceApplicationService", singleInstance, StringComparison.Ordinal);
        Assert.Contains("AcquireMutexLease", singleInstance, StringComparison.Ordinal);
    }


    [Fact]
    public void SettingsThemePreviewDoesNotRebuildSettingsPage()
    {
        var settings = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Settings.cs"));
        var handlerStart = settings.IndexOf("private void PreviewSettingsTheme()", StringComparison.Ordinal);
        var handlerEnd = settings.IndexOf("private async Task RunSettingsRowActionAsync", handlerStart, StringComparison.Ordinal);

        Assert.True(handlerStart >= 0);
        Assert.True(handlerEnd > handlerStart);
        var handler = settings[handlerStart..handlerEnd];
        Assert.Contains("AppPreferenceService.ThemeMode(_settingsPage.SelectedThemeValue)", handler, StringComparison.Ordinal);
        Assert.Contains("ApplicationThemeService.Apply(mode);", handler, StringComparison.Ordinal);
        Assert.Contains("Status.ThemePreviewApplied", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("_themeCombo", handler, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowSettings()", handler, StringComparison.Ordinal);
    }


    [Fact]
    public void SettingsApiKeyCanBeShownAndCopied()
    {
        var settings = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Settings.cs"));
        var settingsActions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Settings", "SettingsPageActionController.cs"));
        var settingsFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Settings", "SettingsPageFactory.cs"));
        var settingsGridColumns = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Settings", "SettingsGridColumnFactory.cs"));
        var settingsDefinitions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "SettingsPageDefinitionService.cs"));
        var settingsRowActions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "SettingsRowActionApplicationService.cs"));
        var clipboard = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "ClipboardService.cs"));
        var factory = ReadAppServiceFactorySources();
        var rows = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.Core", "Models", "UiRows.cs"));

        Assert.Contains("Loc.T(\"Setting.ApiKey\"), \"modelApiKey\", settings.ModelApiKey", settingsDefinitions, StringComparison.Ordinal);
        Assert.Contains("Tooltip.Setting.ApiKey", settingsDefinitions, StringComparison.Ordinal);
        Assert.Contains("Tooltip.Setting.ApiKey", settingsDefinitions, StringComparison.Ordinal);
        Assert.DoesNotContain("SettingsGridColumnFactory.ActionsColumn", settingsFactory, StringComparison.Ordinal);
        Assert.Contains("SettingsGridColumnFactory.ValueColumn", settingsFactory, StringComparison.Ordinal);
        Assert.Contains("DockPanel.DockProperty, Dock.Right", settingsGridColumns, StringComparison.Ordinal);
        Assert.Contains("Value = \"cache\"", settingsGridColumns, StringComparison.Ordinal);
        Assert.Contains("VisualRole.Danger", settingsGridColumns, StringComparison.Ordinal);
        Assert.DoesNotContain("FrameworkElementFactory", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Header = \"Secret\"", settings, StringComparison.Ordinal);
        Assert.Contains("RevealSecretRow_Click", settingsActions, StringComparison.Ordinal);
        Assert.Contains("CopySecretRow_Click", settingsActions, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.SettingsRowActions.RunActionAsync", settings, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.SettingsRowActions.ToggleSecret", settings, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.SettingsRowActions.CopySecret", settings, StringComparison.Ordinal);
        Assert.Contains("new(_coreServices.App.Clipboard.SetText, SetStatus)", settings, StringComparison.Ordinal);
        Assert.Contains("System.Windows.Clipboard.SetText", factory, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Clipboard.SetText", clipboard, StringComparison.Ordinal);
        Assert.DoesNotContain("ApiSecurity.GenerateHexToken(32)", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("row.Type != \"folder\"", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Windows.Clipboard.SetText", settings, StringComparison.Ordinal);
        Assert.DoesNotContain("Clipboard.SetText(value)", settings, StringComparison.Ordinal);
        Assert.Contains("nameof(EditableSettingRow.RevealAction)", settingsGridColumns, StringComparison.Ordinal);
        Assert.Contains("nameof(EditableSettingRow.CopyAction)", settingsGridColumns, StringComparison.Ordinal);
        Assert.Contains("nameof(EditableSettingRow.Action)", settingsGridColumns, StringComparison.Ordinal);
        Assert.Contains("public static DataGridTemplateColumn ValueColumn(", settingsGridColumns, StringComparison.Ordinal);
        Assert.Contains("API key copied to clipboard.", settingsRowActions, StringComparison.Ordinal);
        Assert.DoesNotContain("API key copied to clipboard.", settings, StringComparison.Ordinal);
        Assert.Contains("IsSecretVisible", rows, StringComparison.Ordinal);
        Assert.Contains("RevealAction", rows, StringComparison.Ordinal);
        Assert.Contains("CopyAction", rows, StringComparison.Ordinal);
        Assert.Contains("Type == \"secret\" ? IsSecretVisible", rows, StringComparison.Ordinal);
    }



}
