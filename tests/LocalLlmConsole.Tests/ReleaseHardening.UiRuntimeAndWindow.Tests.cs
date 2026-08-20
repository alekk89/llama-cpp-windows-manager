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
    public void MainWindowKeepsLogDeletionActionsAndRemovesRuntimeJobsFromRuntimesPage()
    {
        var source = ReadMainWindowSources();
        var themedMessageBox = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "ThemedMessageBox.cs"));
        var runtimeDeletionPlanner = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeDeletionPlanner.cs"));
        var runtimeBuildDeletionApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeBuildDeletionApplicationService.cs"));
        var runtimeJobControls = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeBuildJobControlService.cs"));
        var settingsGridColumns = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Settings", "SettingsGridColumnFactory.cs"));
        var pageSectionFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "PageSectionFactory.cs"));
        var lifetimeFactory = string.Concat(
            File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Lifetime", "LifetimePageFactory.cs")),
            File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Lifetime", "LifetimePageFactory.Calendar.cs")));
        var lifetimePageState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Lifetime", "LifetimePageState.cs"));
        var modelsFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelsPageFactory.cs"));
        var runtimesFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Runtimes", "RuntimesPageFactory.cs"));
        var runtimesPageState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Runtimes", "RuntimesPageState.cs"));
        var logsFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Logs", "LogsPageFactory.cs"));
        var logsActions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Logs", "LogsPageActionController.cs"));
        var logsPartial = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Logs.cs"));
        var downloadHistoryPartial = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.DownloadHistory.cs"));
        var runtimesRowActions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Runtimes", "RuntimesPageRowActionController.cs"));
        var logWorkflow = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "LogPageWorkflowService.cs"));
        var advancedSections = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AdvancedSectionStateController.cs"));
        Assert.Contains("Logs.DeleteSelectedButton", logsFactory, StringComparison.Ordinal);
        Assert.Contains("Logs.DeleteAllButton", logsFactory, StringComparison.Ordinal);
        Assert.Contains("DeleteLogRow_Click", logsActions, StringComparison.Ordinal);
        Assert.Contains("DataGridSelectionMode.Extended", logsFactory, StringComparison.Ordinal);
        Assert.Contains("LogsPageFactory.Create(new LogsPageRequest(", source, StringComparison.Ordinal);
        Assert.Contains("SelectedLogPaths", source, StringComparison.Ordinal);
        Assert.Contains("LifetimePageFactory.Create(new LifetimePageRequest(", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class LifetimePageState", lifetimePageState, StringComparison.Ordinal);
        Assert.DoesNotContain("Items.Refresh()", lifetimePageState, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"Lifetime.DailyHistoryTitle\")", lifetimeFactory, StringComparison.Ordinal);
        Assert.Contains("LifetimeRangeSelector", lifetimeFactory, StringComparison.Ordinal);
        Assert.Contains("LifetimeUsageCalendar", lifetimeFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("LifetimeUsageChart", lifetimeFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("PageSectionFactory.GridFor(MetricColumns)", lifetimeFactory, StringComparison.Ordinal);
        Assert.Contains("PageSectionFactory.GridFor(", lifetimeFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("_lifetimeMetricsGrid", source, StringComparison.Ordinal);
        Assert.Contains("IsActiveRuntimeLog", logWorkflow, StringComparison.Ordinal);
        Assert.Contains("BuildSelectedDeletionCommand", logWorkflow, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtimes.RuntimeJobsDesc", runtimesFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeJobsGrid", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("OpenRuntimeJobLogRow_Click", runtimesRowActions, StringComparison.Ordinal);
        Assert.Contains("OpenLogPath(job.LogPath)", runtimesRowActions, StringComparison.Ordinal);
        Assert.Contains("Status.LogsNotReady", logsPartial, StringComparison.Ordinal);
        Assert.DoesNotContain("Status.LogsNotReady", downloadHistoryPartial, StringComparison.Ordinal);
        Assert.DoesNotContain("logPageApplication.Open(job.LogPath", downloadHistoryPartial, StringComparison.Ordinal);
        Assert.DoesNotContain("Common.LogButton", runtimesFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowRuntimes", advancedSections, StringComparison.Ordinal);
        Assert.DoesNotContain("AdvancedSections.ShowRuntimes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ToggleAdvancedRuntimes", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_showAdvancedRuntimes", source, StringComparison.Ordinal);
        Assert.Contains("private readonly RuntimesPageState _runtimesPage;", source, StringComparison.Ordinal);
        Assert.Contains("_runtimesPage = uiState.RuntimesPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly RuntimesPageState _runtimesPage = new();", source, StringComparison.Ordinal);
        Assert.Contains("_runtimesPage.Apply(runtimesPage);", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class RuntimesPageState", runtimesPageState, StringComparison.Ordinal);
        Assert.Contains("public RuntimeRecord? SelectedRuntime", runtimesPageState, StringComparison.Ordinal);
        Assert.Contains("public string SelectedCudaPackagePreference", runtimesPageState, StringComparison.Ordinal);
        Assert.DoesNotContain("RestoreRuntimeJobSelection", runtimesPageState, StringComparison.Ordinal);
        Assert.Contains("public void RefreshRuntimePackageGrid()", runtimesPageState, StringComparison.Ordinal);
        Assert.Contains("public void RefreshRuntimeDownloadsGrid()", runtimesPageState, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimePackageGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeBuildGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeJobsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("source downloads, builds, and runtime jobs", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeCudaPreferenceCombo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimesFolderText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShowAdvancedRuntimes", runtimesFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeAdvancedToggleButton", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("Runtimes.CudaDownloadsLabel", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("LaunchCombo(AppPreferenceService.CudaPackagePreferenceOptions())", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("ChangeRuntimeCudaPackagePreferenceAsync", source, StringComparison.Ordinal);
        Assert.Contains("nameof(RuntimePackagePresetRow.BuildSourceAction)", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("request.Actions.RuntimeSourceRowClick, .75", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("\"RuntimeDownload\"", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("\"InstalledRuntime\"", runtimesFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime Job Log Tail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LoadSelectedRuntimeJobLog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeJobLogBox", source, StringComparison.Ordinal);
        Assert.Contains("ClearRuntimeJobRow_Click", runtimesRowActions, StringComparison.Ordinal);
        Assert.Contains("DeleteJobAsync(job.Id)", runtimeJobControls, StringComparison.Ordinal);
        Assert.Contains("DeleteRuntimeAsync(runtime, _settings, RuntimeBuildDeletionActions())", source, StringComparison.Ordinal);
        Assert.Contains("RuntimeBuildDeletionActions()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PlanRuntimeDeletionAsync(runtime", source, StringComparison.Ordinal);
        Assert.Contains("PlanRuntimeDeletionAsync(runtime", runtimeBuildDeletionApplication, StringComparison.Ordinal);
        Assert.Contains("Register another runtime before deleting this one", runtimeDeletionPlanner, StringComparison.Ordinal);
        Assert.Contains("Saved model launch settings that use this runtime will be moved", runtimeBuildDeletionApplication, StringComparison.Ordinal);
        Assert.Contains("nameof(RuntimeCatalogRow.DeleteToolTip)", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("ButtonToolTip", source, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticButtonToolTips", source, StringComparison.Ordinal);
        Assert.Contains("ToolTipService.ShowOnDisabledProperty", pageSectionFactory, StringComparison.Ordinal);
        Assert.Contains("nameof(ModelGridRow.DeleteToolTip)", modelsFactory, StringComparison.Ordinal);
        Assert.Contains("nameof(RuntimePackagePresetRow.BuildSourceToolTip)", runtimesFactory, StringComparison.Ordinal);
        Assert.Contains("nameof(EditableSettingRow.ActionToolTip)", settingsGridColumns, StringComparison.Ordinal);
        Assert.Contains("tooltipBinding: \"T1\"", lifetimeFactory, StringComparison.Ordinal);
        Assert.Contains("DialogButtonToolTip", themedMessageBox, StringComparison.Ordinal);
        Assert.Contains("LogFileService.TryValidateWorkspaceLogFile(_workspaceRoot, job.LogPath", runtimeJobControls, StringComparison.Ordinal);
        Assert.Contains("LogFileService.RedactSensitiveText(tail", logWorkflow, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeCatalogCommandsStayOutOfMainWindow()
    {
        var source = ReadMainWindowSources();
        var runtimeCatalog = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeCatalog.cs"));
        var application = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeCatalogCommandApplicationService.cs"));

        Assert.Contains("var runtimeCatalogCommands = RuntimeServices.RuntimeCatalogCommands;", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("runtimeCatalogCommands.ChangeCudaPackagePreferenceAsync", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("runtimeCatalogCommands.AddCustomRepositoryAsync", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("RuntimeCatalogPreferenceActions()", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("RuntimeCatalogCustomRepositoryActions", runtimeCatalog, StringComparison.Ordinal);
        Assert.Contains("AppPreferenceService.CudaPackagePreference(selectedPreference)", application, StringComparison.Ordinal);
        Assert.Contains("_customRepositories.AddAsync(runtimeRoot, draft", application, StringComparison.Ordinal);
        Assert.DoesNotContain("AppPreferenceService.CudaPackagePreference(_runtimesPage.SelectedCudaPackagePreference)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CUDA downloads set to", source, StringComparison.Ordinal);
        Assert.DoesNotContain("customRuntimeRepositories.AddAsync", runtimeCatalog, StringComparison.Ordinal);
    }


    [Fact]
    public void MinimizeBehaviorUsesExplicitTrayAndTaskbarModes()
    {
        Loc.LoadLanguage("en");
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var source = ReadMainWindowSources();
        var controller = new TrayWindowStateController();

        Assert.Equal("taskbarOnly", settings.MinimizeBehavior);
        Assert.Equal(
            [Loc.T("Pref.TaskbarOnly"), Loc.T("Pref.TrayOnly"), Loc.T("Pref.TrayAndTaskbar")],
            AppPreferenceService.MinimizeBehaviorOptions());
        Assert.Equal("trayAndTaskbar", AppPreferenceService.MinimizeBehavior("Tray + taskbar"));
        Assert.Equal("both", AppPreferenceService.ModelAccessMode("network access"));
        Assert.Equal("gateway", AppPreferenceService.ModelAccessMode("Gateway LAN only"));
        Assert.True(AppPreferenceService.GatewayAllowsLanAccess("Gateway LAN only"));
        Assert.False(AppPreferenceService.DirectModelsAllowLanAccess("Gateway LAN only"));
        Assert.Equal("127.0.0.1", AppPreferenceService.RuntimeHostForAccessMode("Gateway LAN only"));
        Assert.Equal("0.0.0.0", AppPreferenceService.RuntimeHostForAccessMode("Direct models LAN only"));
        Assert.Equal("latest", settings.CudaPackagePreference);
        Assert.Equal(
            [Loc.T("Pref.Latest"), Loc.T("Pref.Compatibility")],
            AppPreferenceService.CudaPackagePreferenceOptions());
        Assert.Equal("latest", AppPreferenceService.CudaPackagePreference("Latest"));
        Assert.Equal("compatibility", AppPreferenceService.CudaPackagePreference("CUDA 12 compatibility"));
        Assert.True(AppPreferenceService.YesNoValue("on", fallback: false));
        Assert.True(AppPreferenceService.TryIntValue("42", out var parsed));
        Assert.Equal(42, parsed);
        Assert.False(AppPreferenceService.TryIntValue("bad", out _));
        Assert.Equal(10, AppPreferenceService.ClampedIntValue("99", fallback: 7, min: 1, max: 10));
        Assert.Equal(TrayMinimizeAction.TaskbarOnly, controller.BuildMinimizePlan("taskbarOnly").Action);
        Assert.Equal(TrayMinimizeAction.TrayOnly, controller.BuildMinimizePlan("trayOnly").Action);
        var trayAndTaskbar = controller.BuildMinimizePlan("trayAndTaskbar");
        Assert.Equal(TrayMinimizeAction.TrayAndTaskbar, trayAndTaskbar.Action);
        Assert.Contains("taskbar and tray", trayAndTaskbar.StatusMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(TrayMinimizeAction.TrayOnly, controller.WindowStateChangedAction(System.Windows.WindowState.Minimized, "trayOnly"));
        Assert.Equal(TrayMinimizeAction.TaskbarOnly, controller.WindowStateChangedAction(System.Windows.WindowState.Normal, "trayOnly"));

        var minimize = controller.BeginHideToTray(System.Windows.WindowState.Maximized);
        Assert.True(minimize.ShouldApply);
        Assert.True(minimize.ShouldShowHint);
        Assert.True(controller.IsMinimizingToTray);
        Assert.True(controller.HasShownTrayHint);
        Assert.Equal(System.Windows.WindowState.Maximized, controller.RestoreState);
        controller.CompleteHideToTray();
        Assert.False(controller.IsMinimizingToTray);
        Assert.Equal(System.Windows.WindowState.Maximized, controller.BuildRestorePlan().RestoreState);
        var secondMinimize = controller.BeginHideToTray(System.Windows.WindowState.Minimized);
        Assert.True(secondMinimize.ShouldApply);
        Assert.False(secondMinimize.ShouldShowHint);
        Assert.Equal(System.Windows.WindowState.Maximized, secondMinimize.RestoreState);
        controller.CompleteHideToTray();

        Assert.Contains("_coreServices.Ui.TrayWindowState.BuildMinimizePlan(_settings.MinimizeBehavior)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.TrayWindowState.WindowStateChangedAction(WindowState, _settings.MinimizeBehavior)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.TrayWindowState.BeginHideToTray(WindowState)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.TrayWindowState.BuildRestorePlan()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldHideToTrayOnMinimize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ShouldShowTrayWithTaskbarOnMinimize", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_windowStateBeforeTray", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_minimizingToTray", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_shownTrayHint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Tray when running", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowConstrainsMaximizedWindowToWorkingArea()
    {
        var source = ReadMainWindowSources();

        Assert.Contains("ApplyWindowWorkAreaBounds", source, StringComparison.Ordinal);
        Assert.Contains("Forms.Screen.FromHandle", source, StringComparison.Ordinal);
        Assert.Contains("TransformFromDevice", source, StringComparison.Ordinal);
    }



    [Fact]
    public void MainWindowViewModelTracksPageStatusAndBusyState()
    {
        Loc.LoadLanguage("en");
        var vm = new MainWindowViewModel();
        var changes = new List<string?>();
        vm.PropertyChanged += (_, e) => changes.Add(e.PropertyName);
        var source = ReadMainWindowSources();
        var controller = new UiBusyStateController();
        var pageEnabled = true;
        bool? waitCursor = null;

        Assert.Equal("Overview", vm.CurrentPage);
        Assert.Equal(Loc.T("Status.Starting"), vm.StatusText);
        Assert.True(vm.TryBeginBusy(out var busyMessage));
        Assert.Equal("", busyMessage);
        Assert.False(vm.TryBeginBusy(out busyMessage));
        Assert.Equal(Loc.T("Status.PleaseWaitFor", vm.StatusText), busyMessage);
        Assert.True(vm.EndBusy());
        Assert.False(vm.EndBusy());

        vm.CurrentPage = "Models";
        vm.SetStatus("");

        Assert.Equal("Models", vm.CurrentPage);
        Assert.Equal(Loc.T("Status.Ready"), vm.DisplayStatusText);
        Assert.Contains(nameof(MainWindowViewModel.CurrentPage), changes);
        Assert.Contains(nameof(MainWindowViewModel.StatusText), changes);
        Assert.Contains(nameof(MainWindowViewModel.DisplayStatusText), changes);
        Assert.Contains(nameof(MainWindowViewModel.IsBusy), changes);

        controller.Begin(
            pageEnabled,
            enabled => pageEnabled = enabled,
            enabled => waitCursor = enabled);

        Assert.True(controller.HasActiveBusyState);
        Assert.False(pageEnabled);
        Assert.True(waitCursor);

        controller.Begin(
            pageIsEnabled: true,
            enabled => pageEnabled = enabled,
            enabled => waitCursor = enabled);

        Assert.False(pageEnabled);
        Assert.True(waitCursor);
        Assert.True(controller.End(enabled => pageEnabled = enabled, enabled => waitCursor = enabled));
        Assert.True(pageEnabled);
        Assert.False(waitCursor);
        Assert.False(controller.HasActiveBusyState);
        Assert.False(controller.End(enabled => pageEnabled = enabled, enabled => waitCursor = enabled));
        Assert.Contains("_coreServices.Ui.UiBusyState.Begin(PageHost.IsEnabled, SetPageHostEnabled, SetWaitCursor)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.UiBusyState.End(SetPageHostEnabled, SetWaitCursor)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_pageHostEnabledBeforeBusy", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LongRunningModelAndRuntimeOperationsKeepThePageInteractive()
    {
        var execution = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Execution.cs"));
        var uiState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.UiState.cs"));
        var modelRuntime = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.ModelRuntime.cs"));
        var runtimeBuilds = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeBuildJobs.cs"));
        var runtimeSources = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeSourceDownloads.cs"));
        var runtimePackages = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimePackages.cs"));
        var sourceApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeSourceApplicationService.cs"));

        Assert.Contains("RunResponsiveAsync(string message, Func<Task> action)", execution, StringComparison.Ordinal);
        Assert.Contains("ResponsiveTaskActions()", execution, StringComparison.Ordinal);
        Assert.Contains("TryBeginResponsiveActivity", execution, StringComparison.Ordinal);
        Assert.Contains("EndResponsiveActivity", execution, StringComparison.Ordinal);
        Assert.Contains("private bool TryBeginResponsiveActivity", uiState, StringComparison.Ordinal);
        Assert.DoesNotContain("SetPageHostEnabled(false)", uiState, StringComparison.Ordinal);
        Assert.Contains("RunResponsiveAsync,", modelRuntime, StringComparison.Ordinal);
        Assert.Contains("private RuntimeBuildApplicationActions RuntimeBuildApplicationActions()", runtimeBuilds, StringComparison.Ordinal);
        Assert.Contains("RunResponsiveAsync,", runtimeBuilds, StringComparison.Ordinal);
        Assert.Contains("RunResponsiveAsync,", runtimeSources, StringComparison.Ordinal);
        Assert.Contains("RuntimePackageActions(responsive: true)", runtimePackages, StringComparison.Ordinal);
        Assert.Contains("DeleteBuildsAsync(preset, _settings, _runtimeCatalogState, RuntimePackageActions())", runtimePackages, StringComparison.Ordinal);
        Assert.Contains("_catalogData.LoadSourcesAsync(settings.RuntimeRoot)", sourceApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("_catalogData.Sources(settings.RuntimeRoot)", sourceApplication, StringComparison.Ordinal);
    }

}
