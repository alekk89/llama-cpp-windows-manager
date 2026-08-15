using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using LocalLlmConsole.Localization;
using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Theory]
    [InlineData(580, 6, 1)]
    [InlineData(620, 6, 2)]
    [InlineData(1024, 6, 2)]
    [InlineData(1139, 6, 2)]
    [InlineData(1140, 6, 3)]
    [InlineData(1600, 2, 2)]
    [InlineData(1600, 0, 1)]
    public void OverviewMetricLayoutProtectsReadableCardWidths(double width, int visibleCards, int expectedColumns)
        => Assert.Equal(expectedColumns, OverviewResponsiveLayout.MetricColumnCount(width, visibleCards));

    [Fact]
    public void InitialWindowSizingFitsHighDpiWorkAreasWithoutDroppingBelowRequestedMinimumWhenSpaceAllows()
    {
        var constrained = WindowWorkAreaSizingService.Fit(1200, 780, 900, 600, 0, 0, 1056, 672);

        Assert.Equal(1024, constrained.Width);
        Assert.Equal(640, constrained.Height);
        Assert.Equal(900, constrained.MinimumWidth);
        Assert.Equal(600, constrained.MinimumHeight);
        Assert.Equal(16, constrained.Left);
        Assert.Equal(16, constrained.Top);

        var smaller = WindowWorkAreaSizingService.Fit(1200, 780, 900, 600, 0, 0, 820, 560);
        Assert.Equal(788, smaller.Width);
        Assert.Equal(528, smaller.Height);
        Assert.Equal(788, smaller.MinimumWidth);
        Assert.Equal(528, smaller.MinimumHeight);
    }

    [Fact]
    public void UiRowReconciliationPreservesIdentityOrderAndBindingNotifications()
    {
        var first = new UiRow { C1 = "first", Data = new JsonObject { ["Id"] = "1" } };
        var second = new UiRow { C1 = "second", Data = new JsonObject { ["Id"] = "2" } };
        var rows = new System.Collections.ObjectModel.ObservableCollection<UiRow>([first, second]);
        var changes = new List<string?>();
        second.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        var changed = UiRowCollectionUpdater.Reconcile(
            rows,
            [
                new UiRow { C1 = "second updated", Data = new JsonObject { ["Id"] = "2" } },
                new UiRow { C1 = "first", Data = new JsonObject { ["Id"] = "1" } }
            ],
            row => row.Data["Id"]?.ToString() ?? "");

        Assert.True(changed);
        Assert.Same(second, rows[0]);
        Assert.Same(first, rows[1]);
        Assert.Equal("second updated", rows[0].C1);
        Assert.Contains(nameof(UiRow.C1), changes);
        Assert.False(UiRowCollectionUpdater.Reconcile(
            rows,
            [
                new UiRow { C1 = "second updated", Data = new JsonObject { ["Id"] = "2" } },
                new UiRow { C1 = "first", Data = new JsonObject { ["Id"] = "1" } }
            ],
            row => row.Data["Id"]?.ToString() ?? ""));
    }

    [Fact]
    public void CurrentActionIsPinnedBelowHelpInItsOwnSidebarRow()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var helpIndex = xaml.IndexOf("x:Name=\"HelpNavButton\"", StringComparison.Ordinal);
        var currentActionIndex = xaml.IndexOf("x:Name=\"CurrentStatusLabel\"", StringComparison.Ordinal);

        Assert.True(helpIndex >= 0);
        Assert.True(currentActionIndex >= 0);
        Assert.True(currentActionIndex < helpIndex);
        Assert.Contains("<Border Grid.Row=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Grid.Row=\"2\">", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageDoesNotExposeCacheFolder()
    {
        var source = ReadMainWindowSources();

        Assert.DoesNotContain("\"Cache folder\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cacheRoot\"", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowHasVisibleAppStatusLine()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();

        Assert.Contains("x:Name=\"AppStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Status.CurrentActionLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceStatusText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeStatusText", source, StringComparison.Ordinal);
        Assert.Contains("AppStatusText.Text", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.Yield", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreUiSurfacesExposeAutomationNamesHelpAndLiveStatus()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var localization = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Localization.cs"));
        var accessibility = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "UiAccessibility.cs"));
        var sections = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "PageSectionFactory.cs"));

        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(LanguageCombo", localization, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(AppStatusText", localization, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(button", accessibility, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.NameProperty", sections, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpTextProperty", sections, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHeadingLevel", sections, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowCancelsClosingSynchronouslyBeforeAsyncCleanup()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs"));
        var handlerIndex = source.IndexOf("private async void Window_Closing", StringComparison.Ordinal);
        var cancelIndex = source.IndexOf("e.Cancel = true;", handlerIndex, StringComparison.Ordinal);
        var shutdownIndex = source.IndexOf("BeginShutdownAsync", handlerIndex, StringComparison.Ordinal);

        Assert.True(handlerIndex >= 0);
        Assert.True(cancelIndex > handlerIndex);
        Assert.True(shutdownIndex > cancelIndex);
    }


    [Fact]
    public void MainWindowUsesLlamaCppWindowsManagerBrandingAndIcon()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var iconPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Assets", "AppIcon.ico");

        Assert.Contains("Title=\"llama.cpp Windows Manager v2.2.0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"v2.2.0\"", xaml, StringComparison.Ordinal);

        Assert.Contains("AppVersionLabel = \"v2.2.0\"", source, StringComparison.Ordinal);
        Assert.Contains("<AssemblyName>LlamaCppWindowsManager</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Assets\\AppIcon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.True(new FileInfo(iconPath).Length > 1024);
    }



    [Fact]
    public void OverviewLoadedSessionRowsSelectModelStatus()
    {
        var source = ReadMainWindowSources();
        var overviewFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewPageFactory.cs"));
        var loadedSessionSelection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "OverviewLoadedSessionSelectionApplicationService.cs"));

        Assert.Contains("loadedSessionsGrid.SelectionChanged", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("request.Actions.SelectLoadedSessionRowAsync", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("SelectLoadedSessionRowAsync", source, StringComparison.Ordinal);
        Assert.Contains("_overviewPage.SelectedLoadedSessionRow", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.OverviewLoadedSessionSelectionApplication.SelectAsync", source, StringComparison.Ordinal);
        Assert.Contains("OverviewLoadedSessionSelectionActions()", source, StringComparison.Ordinal);
        Assert.Contains("_overviewPage.SelectModelId", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeSessions.SelectModel", source, StringComparison.Ordinal);
        Assert.Contains("Selected session is no longer loaded.", loadedSessionSelection, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected session is no longer loaded.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected loaded model session.", source, StringComparison.Ordinal);
    }


    [Fact]
    public void SelectionReentrancyCoordinatorOwnsSelectionSuppression()
    {
        var coordinator = new SelectionReentrancyCoordinator();
        var source = ReadMainWindowSources();

        using (var modelSelection = coordinator.TryBeginModelGridSelection())
        {
            Assert.NotNull(modelSelection);
            Assert.True(coordinator.IsModelGridSelectionChanging);
            Assert.Null(coordinator.TryBeginModelGridSelection());
        }

        Assert.False(coordinator.IsModelGridSelectionChanging);

        using (var loadedSelection = coordinator.TryBeginLoadedSessionSelection())
        {
            Assert.NotNull(loadedSelection);
            Assert.True(coordinator.IsLoadedSessionSelectionChanging);
            using (coordinator.SuppressLoadedSessionSelection())
            {
                Assert.True(coordinator.IsLoadedSessionSelectionChanging);
                Assert.Null(coordinator.TryBeginLoadedSessionSelection());
            }

            Assert.True(coordinator.IsLoadedSessionSelectionChanging);
        }

        Assert.False(coordinator.IsLoadedSessionSelectionChanging);

        using (coordinator.SuppressLoadedSessionSelection())
        {
            Assert.True(coordinator.IsLoadedSessionSelectionChanging);
            Assert.Null(coordinator.TryBeginLoadedSessionSelection());
        }

        Assert.False(coordinator.IsLoadedSessionSelectionChanging);
        Assert.Contains("_coreServices.Ui.SelectionReentrancy.TryBeginModelGridSelection()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Ui.SelectionReentrancy.TryBeginLoadedSessionSelection()", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.SelectionReentrancy.SuppressLoadedSessionSelection()", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.SelectionReentrancy.IsLoadedSessionSelectionChanging", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectingModelGridRow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectingLoadedSessionRow", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowUsesObservedBackgroundTasks()
    {
        var source = ReadMainWindowSources();

        Assert.DoesNotContain("_ = Refresh", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Monitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = CheckFor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Seed", source, StringComparison.Ordinal);
        Assert.Contains("RunBackground", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.BackgroundTasks.RunAsync(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void LogsPageDeleteRulesStayInWorkflowService()
    {
        var source = ReadMainWindowSources();
        var logsWindow = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Logs.cs"));
        var logWorkflow = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "LogPageWorkflowService.cs"));
        var logApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "LogPageApplicationService.cs"));
        var appLogApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AppLogApplicationService.cs"));
        var logsPageState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Logs", "LogsPageState.cs"));

        Assert.Contains("var logPageApplication = AppServices.LogPageApplication;", source, StringComparison.Ordinal);
        Assert.Contains("logPageApplication!.BuildSelectedDeletionCommand(SelectedLogPaths(), _sessions.Snapshots())", source, StringComparison.Ordinal);
        Assert.Contains("logPageApplication!.BuildSingleDeletionCommand(path, _sessions.Snapshots())", source, StringComparison.Ordinal);
        Assert.Contains("logPageApplication!.BuildAllDeletionCommandAsync(_sessions.Snapshots())", source, StringComparison.Ordinal);
        Assert.Contains("await logPageApplication!.DeleteAsync(commandPlan, LogPageDeleteActions())", source, StringComparison.Ordinal);
        Assert.Contains("private readonly LogsPageState _logsPage;", source, StringComparison.Ordinal);
        Assert.Contains("_logsPage = uiState.LogsPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly LogsPageState _logsPage = new();", source, StringComparison.Ordinal);
        Assert.Contains("_logsPage.Apply(page.Controls);", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class LogsPageState", logsPageState, StringComparison.Ordinal);
        Assert.Contains("public string[] SelectedLogPaths()", logsPageState, StringComparison.Ordinal);
        Assert.Contains("public void RestoreSelection", logsPageState, StringComparison.Ordinal);
        Assert.Contains("public sealed class LogPageApplicationService", logApplication, StringComparison.Ordinal);
        Assert.Contains("_workflow.BuildSelectedDeletionCommand(selectedPaths, sessions)", logApplication, StringComparison.Ordinal);
        Assert.Contains("_workflow.BuildSingleDeletionCommand(path, sessions)", logApplication, StringComparison.Ordinal);
        Assert.Contains("_workflow.BuildAllDeletionCommandAsync(sessions, cancellationToken)", logApplication, StringComparison.Ordinal);
        Assert.Contains("public LogPageOpenApplicationOutcome Open", logApplication, StringComparison.Ordinal);
        Assert.Contains("_workflow.TryValidateForOpen(path, out var error)", logApplication, StringComparison.Ordinal);
        Assert.Contains("public Task<string> BuildPreviewAsync", logApplication, StringComparison.Ordinal);
        Assert.Contains("_workflow.BuildPreviewAsync(new LogPreviewRequest(", logApplication, StringComparison.Ordinal);
        Assert.Contains("public async Task<LogPageDeleteApplicationOutcome> DeleteAsync", logApplication, StringComparison.Ordinal);
        Assert.Contains("!File.Exists(request.Path)", logWorkflow, StringComparison.Ordinal);
        Assert.Contains("BuildSelectedDeletionCommand", logWorkflow, StringComparison.Ordinal);
        Assert.Contains("BuildSingleDeletionCommand", logWorkflow, StringComparison.Ordinal);
        Assert.Contains("BuildAllDeletionCommandAsync", logWorkflow, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.AppLogApplication.WriteExceptionAsync", source, StringComparison.Ordinal);
        Assert.Contains("BoundedLogFile.AppendAsync(path, text, maxLogBytes)", appLogApplication, StringComparison.Ordinal);
        Assert.Contains("LogFileService.RedactSensitiveText(text, apiKey)", appLogApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("_logPageWorkflow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BoundedLogFile.AppendAsync(path, text, MaxLogBytes())", source, StringComparison.Ordinal);
        Assert.DoesNotContain("LogFileService.RedactSensitiveText(text, _settings.ModelApiKey)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_logsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_logsBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStatus(\"Select one or more log files first.\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStatus(\"No selected logs can be deleted.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetStatus(\"Stop the running model before deleting its active runtime log.\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TryValidateLogFileForOpen", source, StringComparison.Ordinal);
        Assert.DoesNotContain("logPageApplication.TryValidateForOpen", source, StringComparison.Ordinal);
        Assert.DoesNotContain("File.Exists", logsWindow, StringComparison.Ordinal);
    }



    [Fact]
    public void LightThemeUsesLayeredSurfacesAndElevation()
    {
        var appXaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml"));
        var source = ReadMainWindowSources();
        var theme = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "ApplicationThemeService.cs"));
        var metricFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "MetricCardFactory.cs"));
        var overviewFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewPageFactory.cs"));

        Assert.Contains("<DropShadowEffect", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"MetricCard\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding Content, RelativeSource={RelativeSource TemplatedParent}}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("TextWrapping=\"Wrap\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DropDownPickerButton\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate TargetType=\"Button\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"MinHeight\" Value=\"29\"/>", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Setter Property=\"Margin\" Value=\"0\"/>", appXaml, StringComparison.Ordinal);
        Assert.Contains("Data=\"M 0 0 L 4 4 L 8 0 Z\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"ContextMenu\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("<Style TargetType=\"MenuItem\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"HasDropShadow\" Value=\"False\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Background\" Value=\"{DynamicResource PanelBackAlt}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("RefreshProgrammaticBrushReferences", theme, StringComparison.Ordinal);
        Assert.Contains("(\"AppBack\", \"#F3F5F8\")", theme, StringComparison.Ordinal);
        Assert.Contains("(\"SidebarBack\", \"#E9EDF2\")", theme, StringComparison.Ordinal);
        Assert.Contains("(\"PanelBorderStrong\", \"#B8C3D0\")", theme, StringComparison.Ordinal);
        Assert.Contains("ControlTemplate TargetType=\"ContextMenu\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("private static string TooltipText(string text) => text;", source, StringComparison.Ordinal);
        Assert.Contains("MetricImportantValuePattern", metricFactory, StringComparison.Ordinal);
        Assert.Contains("SplitMetricLine", metricFactory, StringComparison.Ordinal);
        Assert.Contains("MetricShouldEmphasizeWholeLine", metricFactory, StringComparison.Ordinal);
        Assert.Contains("IsNeutralMetricStatus", metricFactory, StringComparison.Ordinal);
        Assert.Contains("MetricShouldRenderNeutralStatus", metricFactory, StringComparison.Ordinal);
        Assert.Contains("TryAddStatusNameMetricLine", metricFactory, StringComparison.Ordinal);
        Assert.Contains("MetricStatusNameBlock", metricFactory, StringComparison.Ordinal);
        Assert.Contains("MetricCardHeight = 104", metricFactory, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds = true", metricFactory, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.NoWrap", metricFactory, StringComparison.Ordinal);
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", metricFactory, StringComparison.Ordinal);
        Assert.Contains("MetricLabelColumnWidth(label)", metricFactory, StringComparison.Ordinal);
        Assert.Contains("=> string.Equals(label, Loc.T(\"Overview.Metric.ModelStatus\"), StringComparison.Ordinal)", metricFactory, StringComparison.Ordinal);
        Assert.Contains("header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto })", metricFactory, StringComparison.Ordinal);
        Assert.Contains("MetricCardFactory.SetMetricText", source, StringComparison.Ordinal);
        Assert.Contains("gpu = MetricCardFactory.AddMetric(runtimeDashboard, Loc.T(\"Overview.Metric.Hardware\"), 0, 1)", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("slots = MetricCardFactory.AddMetric(runtimeDashboard, Loc.T(\"Overview.Metric.Slots\"), 0, 2)", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("tokens = MetricCardFactory.AddMetricGraph(runtimeDashboard, Loc.T(\"Overview.Metric.Tokens\"), 1, 0", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"Overview.Metric.MtpTokens\")", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"Overview.Metric.KvCache\")", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("public sealed class MetricSparkline", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "MetricSparkline.cs")), StringComparison.Ordinal);
        Assert.DoesNotContain("\"Tokens (Live)\"", overviewFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Tokens (Total)\"", overviewFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("\"Runtime build\", 0, 1", overviewFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("SetLastKnownMetricText(_runtimeDashboardPage.TokensLastKnown", source, StringComparison.Ordinal);
        Assert.Contains("ClearLastKnownMetricText(_runtimeDashboardPage.TokensLastKnown)", source, StringComparison.Ordinal);
        Assert.Contains("SetMetricText(_runtimeDashboardPage.TokensMetric, summary.Tokens)", source, StringComparison.Ordinal);
        Assert.Contains("SetMetricText(_runtimeDashboardPage.MtpTokensMetric, summary.MtpTokens)", source, StringComparison.Ordinal);
        Assert.Contains("SetMetricText(_runtimeDashboardPage.SlotsMetric, summary.Slots)", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeDashboardPage.TokensGraph?.Push(", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeDashboardPage.MtpTokensGraph?.Push(", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeDashboardPage.KvCacheGraph?.Push(", source, StringComparison.Ordinal);
        Assert.Contains("_sessions.SelectedSnapshot()?.LogPath", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeDashboardTotalTokensLastKnown", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(label, \"Overview.Metric.ModelStatus\", StringComparison.Ordinal)", metricFactory, StringComparison.Ordinal);
        Assert.Contains("\"Loaded Model:\"", metricFactory, StringComparison.Ordinal);
        Assert.Contains("\"Loading Model:\"", metricFactory, StringComparison.Ordinal);
        Assert.Contains("\"Loading:\"", metricFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("SetMetricText(_runtimeDashboardPage.RuntimeMetric", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(normalized, \"None\", StringComparison.OrdinalIgnoreCase)", metricFactory, StringComparison.Ordinal);
        Assert.Contains("string.Equals(normalized, \"Stopped\", StringComparison.OrdinalIgnoreCase)", metricFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("text.StartsWith(\"Loading \", StringComparison.OrdinalIgnoreCase)", metricFactory, StringComparison.Ordinal);
        Assert.Contains("MetricValueFont", metricFactory, StringComparison.Ordinal);
        Assert.Contains("Typography.SetNumeralAlignment(valueRun, FontNumeralAlignment.Tabular)", metricFactory, StringComparison.Ordinal);
        Assert.Contains("(\"AppBack\", \"#F3F5F8\")", theme, StringComparison.Ordinal);
        Assert.Contains("(\"PanelBack\", \"#FFFFFF\")", theme, StringComparison.Ordinal);
        Assert.Contains("(\"PanelBorder\", \"#D5DCE5\")", theme, StringComparison.Ordinal);
        Assert.Contains("(\"PanelBorderStrong\", \"#B8C3D0\")", theme, StringComparison.Ordinal);
        Assert.Contains("(\"GridRowAlt\", \"#F6F8FA\")", theme, StringComparison.Ordinal);
        Assert.Contains("(\"Accent\", \"#263545\")", theme, StringComparison.Ordinal);
    }

    [Fact]
    public void DisabledPrimaryButtonsRemainReadableInBothThemes()
    {
        var appXaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml"));
        var themeSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "ApplicationThemeService.cs"));
        var sectionFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "PageSectionFactory.cs"));
        var updatesFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Updates", "UpdatesPageFactory.cs"));
        var windowsFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Environment", "WindowsPageFactory.cs"));
        var wslFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Environment", "WslPageFactory.cs"));

        Assert.Contains("TextElement.Foreground=\"{TemplateBinding Foreground}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"ButtonContent\" Property=\"TextElement.Foreground\" Value=\"{DynamicResource AccentForeground}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"ButtonContent\" Property=\"TextElement.Foreground\" Value=\"{DynamicResource DisabledPrimaryForeground}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DisabledPrimaryBack\" Color=\"#343431\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"DisabledPrimaryForeground\" Color=\"#C7C7C1\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Foreground\" Value=\"{DynamicResource DisabledPrimaryForeground}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("Property=\"Opacity\" Value=\"1\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("(\"DisabledPrimaryBack\", \"#DCE2E8\")", themeSource, StringComparison.Ordinal);
        Assert.Contains("(\"DisabledPrimaryForeground\", \"#526171\")", themeSource, StringComparison.Ordinal);
        Assert.Contains("ContentControl.ContentTemplateProperty", sectionFactory, StringComparison.Ordinal);
        Assert.Contains("TextBlock.ForegroundProperty", sectionFactory, StringComparison.Ordinal);
        Assert.Contains("RelativeSourceMode.FindAncestor, typeof(WpfButton), 1", sectionFactory, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"ButtonTextTemplate\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("ContentTemplate=\"{TemplateBinding ContentTemplate}\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("DisabledControlForeground", appXaml, StringComparison.Ordinal);
        Assert.Contains("AccentPressed", appXaml, StringComparison.Ordinal);
        Assert.Contains("request.Actions.PrimaryActionAsync, VisualRole.Primary", updatesFactory, StringComparison.Ordinal);
        Assert.Contains("VisualRole.SetButtonRole(actionButton, VisualRole.Primary)", windowsFactory, StringComparison.Ordinal);
        Assert.Contains("VisualRole.SetButtonRole(installButton, VisualRole.Primary)", wslFactory, StringComparison.Ordinal);
        Assert.Contains("VisualRole.SetButtonRole(deleteButton, VisualRole.Danger)", wslFactory, StringComparison.Ordinal);
    }


    [Fact]
    public void ActiveNavigationUsesOneWholeButtonHighlightWithoutASecondMarker()
    {
        var appXaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml"));
        var mainWindowXaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));

        Assert.Contains("<Trigger Property=\"Tag\" Value=\"Active\">", appXaml, StringComparison.Ordinal);
        Assert.Contains("TargetName=\"Chrome\" Property=\"Background\" Value=\"{DynamicResource ControlHover}\"", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveMarker", appXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ActiveRail", appXaml, StringComparison.Ordinal);
        Assert.Contains("local:VisualRole.NavGlyph", mainWindowXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"SidebarShell\"", appXaml, StringComparison.Ordinal);
        Assert.Contains("x:Key=\"WorkspaceShell\"", appXaml, StringComparison.Ordinal);
    }


    [Fact]
    public void MetricCardFactoryKeepsMetricParsingRulesOutOfMainWindow()
    {
        var mainWindow = ReadMainWindowSources();

        Assert.Equal(("Gen", "12.3 t/s"), MetricCardFactory.SplitMetricLine("Gen 12.3 t/s"));
        Assert.Equal(("Context", "32,768"), MetricCardFactory.SplitMetricLine("Context 32,768"));
        Assert.Equal(("Port", "8081"), MetricCardFactory.SplitMetricLine("Port: 8081"));
        Assert.True(MetricCardFactory.IsNeutralMetricStatus("No loaded runtime"));
        Assert.True(MetricCardFactory.IsNeutralMetricStatus("Failed to load"));
        Assert.False(MetricCardFactory.IsNeutralMetricStatus("Qwen3 30B"));
        Assert.DoesNotContain("private static readonly Regex MetricImportantValuePattern", mainWindow, StringComparison.Ordinal);
        Assert.DoesNotContain("private static bool MetricShouldRenderNeutralStatus", mainWindow, StringComparison.Ordinal);
        Assert.Contains("MetricCardFactory.SetMetricText", mainWindow, StringComparison.Ordinal);
    }


    [Fact]
    public void OverviewPageFactoryKeepsOverviewLayoutOutOfMainWindow()
    {
        var source = ReadMainWindowSources();
        var overviewFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewPageFactory.cs"));
        var overviewPageState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewPageState.cs"));
        var pageSectionFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "PageSectionFactory.cs"));
        var runtimeDashboardState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "RuntimeDashboardPageState.cs"));

        Assert.Contains("OverviewPageFactory.Create", source, StringComparison.Ordinal);
        Assert.Contains("private readonly OverviewPageState _overviewPage;", source, StringComparison.Ordinal);
        Assert.Contains("_overviewPage = uiState.OverviewPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly OverviewPageState _overviewPage = new();", source, StringComparison.Ordinal);
        Assert.Contains("_overviewPage.Apply(overview);", source, StringComparison.Ordinal);
        Assert.Contains("_runtimeDashboardPage.Apply(overview);", source, StringComparison.Ordinal);
        Assert.Contains("public sealed record OverviewPageActions", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("public sealed record OverviewPageControls", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("public sealed class OverviewPageState", overviewPageState, StringComparison.Ordinal);
        Assert.Contains("public ModelRecord? SelectedModel", overviewPageState, StringComparison.Ordinal);
        Assert.Contains("public UiRow? SelectedLoadedSessionRow", overviewPageState, StringComparison.Ordinal);
        Assert.Contains("public void RestoreLoadedSessionSelection", overviewPageState, StringComparison.Ordinal);
        Assert.Contains("public sealed class RuntimeDashboardPageState", runtimeDashboardState, StringComparison.Ordinal);
        Assert.Contains("public Grid? ModelMetric", runtimeDashboardState, StringComparison.Ordinal);
        Assert.Contains("public DataGrid? RuntimeMetricsGrid", runtimeDashboardState, StringComparison.Ordinal);
        Assert.Contains("public WpfTextBox? RuntimeLogBox", runtimeDashboardState, StringComparison.Ordinal);
        Assert.Contains("Overview.SessionsCol.Model", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Overview.MetricsCol.Metric", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("ConfigureLoadButton(loadButton)", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("button.MinHeight = 30", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(loadButton, 6)", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Width = 240", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Width = 220", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("ConfigureResponsiveModelBar", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("request.Actions.UnloadLoadedSessionRowClick", overviewFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("unloadButton = Button", overviewFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("static readonly (string Header", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"Overview.LoadedSessionsTitle\")", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"Overview.LiveRuntimeLogTitle\")", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("PageSectionFactory.HorizontalGridSplitter(3)", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("public static Grid FramedSection", pageSectionFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("var modelBar = new Grid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GridSection(\"Loaded Model Sessions\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeDashboardModel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeDashboardGenerationRateLastKnown", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeDashboardTokensLastKnown", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_runtimeMetricsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_overviewRuntimeLogBox", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_overviewModelCombo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_overviewLoadButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_overviewUnloadButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_loadedSessionsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_gatewayStatusText", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowKeepsPolishedActionPlacementAndOverviewDiagnostics()
    {
        var source = ReadMainWindowSources();
        var overviewFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewPageFactory.cs"));
        var modelsFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelsPageFactory.cs"));
        var modelsRowActions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelsPageRowActionController.cs"));
        var settingsFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Settings", "SettingsPageFactory.cs"));
        var huggingFaceGridModeFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "HuggingFaceGridModeFactory.cs"));
        var overviewViewModel = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "ViewModels", "OverviewPageViewModel.cs"));
        var modelRuntimeCommands = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "ModelRuntimeCommandDecisionService.cs"));
        var runtimeOverviewStatus = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Runtimes", "RuntimeOverviewStatusService.cs"));
        var normalized = source.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedOverviewFactory = overviewFactory.Replace("\r\n", "\n", StringComparison.Ordinal);
        var normalizedModelsFactory = modelsFactory.Replace("\r\n", "\n", StringComparison.Ordinal);

        Assert.Contains("FolderStripActionsFirst(\n            Loc.T(\"Models.FolderLabel\")", normalizedModelsFactory, StringComparison.Ordinal);
        Assert.Contains("ScanModelsFolderAsync", modelsFactory, StringComparison.Ordinal);
        Assert.Contains("Scanning models...", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Settings.SaveSettingsButton", settingsFactory, StringComparison.Ordinal);
        Assert.Contains("Settings.AutoApplyHint", settingsFactory, StringComparison.Ordinal);
        Assert.Contains("SettingsPageFactory.Create(new SettingsPageRequest(", source, StringComparison.Ordinal);
        Assert.Contains("Select the loading or loaded model to unload it.", modelRuntimeCommands, StringComparison.Ordinal);
        Assert.Contains("Choose the loading or loaded model to unload it.", modelRuntimeCommands, StringComparison.Ordinal);
        Assert.Contains("Stop the currently loading or loaded model", source, StringComparison.Ordinal);
        Assert.Contains("OpenHuggingFaceModelCardRow_Click", source, StringComparison.Ordinal);
        Assert.Contains("_modelCards.OpenFromRow", modelsRowActions, StringComparison.Ordinal);
        Assert.Contains("_modelFolders.Open", modelsRowActions, StringComparison.Ordinal);
        Assert.Contains("PageSectionFactory.AddButtonColumn(request.Grid, Loc.T(\"HfSearch.Col.Card\"), \"C8\", \"B2\", request.Actions.OpenModelCardRow", huggingFaceGridModeFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("Button(\"Model Card\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSelectedHuggingFaceModelCard", source, StringComparison.Ordinal);
        Assert.DoesNotContain("HuggingFaceService.TryCreateModelCardUrl(repo", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Opened Hugging Face model card", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Model folder is unavailable.", source, StringComparison.Ordinal);
        Assert.Contains("(Loc.T(\"HfSearch.Col.Signals\"), \"C6\", 1.4)", huggingFaceGridModeFactory, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"Overview.LiveRuntimeLogTitle\")", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"Overview.RuntimeMetricsTitle\")", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("model = MetricCardFactory.AddMetric(runtimeDashboard, Loc.T(\"Overview.Metric.ModelStatus\"), 0, 0, labelKey:", overviewFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("gatewayStatusText", overviewFactory, StringComparison.OrdinalIgnoreCase);
        var gatewayRuntimeApplication = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "GatewayRuntimeApplicationService.cs"));

        Assert.Contains("actions.StartActivity(request.Model, \"switching to\")", gatewayRuntimeApplication, StringComparison.Ordinal);
        Assert.Contains("Gateway auto-loading", gatewayRuntimeApplication, StringComparison.Ordinal);
        Assert.Contains("Gateway loaded", gatewayRuntimeApplication, StringComparison.Ordinal);
        Assert.Contains("UpdateGatewayStatusText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Ui.GatewayActivity.Build(_settings, _gateway is not null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Ui.GatewayActivityModelName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_lastGatewayError", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Gateway auto-loading", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Gateway loaded {model.Name}", source, StringComparison.Ordinal);
        Assert.Contains("_gatewayServices = services.Gateway", source, StringComparison.Ordinal);
        Assert.Contains("GatewayServices.GatewayRuntimeApplication.EnsureModelLoadedAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelWorkflowServices", source, StringComparison.Ordinal);
        Assert.Contains("_workflow.EnsureLoadedAsync", gatewayRuntimeApplication, StringComparison.Ordinal);
        Assert.True(normalizedOverviewFactory.IndexOf("Loc.T(\"Overview.LoadedSessionsTitle\")", StringComparison.Ordinal) < normalizedOverviewFactory.IndexOf("Loc.T(\"Overview.ModelStatusLabel\")", StringComparison.Ordinal));
        Assert.Contains("(\"Profile\", \"C2\"", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("(Loc.T(\"Overview.SessionsCol.Size\"), \"C3\"", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("SessionStatusLabel", overviewViewModel, StringComparison.Ordinal);
        Assert.Contains("request.Session.RuntimeName", runtimeOverviewStatus, StringComparison.Ordinal);
        Assert.Contains("Unknown runtime", runtimeOverviewStatus, StringComparison.Ordinal);
        Assert.DoesNotContain("active.RuntimeName", source, StringComparison.Ordinal);
        Assert.DoesNotContain("includeProgress: true", source, StringComparison.Ordinal);
        Assert.Contains("var huggingFaceSplitter = PageSectionFactory.HorizontalGridSplitter(2)", modelsFactory, StringComparison.Ordinal);
        Assert.Contains("root.Children.Add(huggingFaceSplitter)", modelsFactory, StringComparison.Ordinal);
        Assert.Contains("BorderThickness = new Thickness(0)", overviewFactory, StringComparison.Ordinal);
    }


    [Fact]
    public void ModelsGridUsesPerRowActionsOnly()
    {
        var source = ReadMainWindowSources();
        var modelsFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelsPageFactory.cs"));
        var modelsPageActions = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelsPageActionController.cs"));
        var modelsPageState = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelsPageState.cs"));
        var launchPanelFactory = ReadLaunchSettingsPanelFactorySources();

        Assert.Contains("nameof(ModelGridRow.Name)", modelsFactory, StringComparison.Ordinal);
        Assert.Contains("nameof(ModelGridRow.Size)", modelsFactory, StringComparison.Ordinal);
        Assert.Contains("Models.SavedVariantsTitle", modelsFactory, StringComparison.Ordinal);
        var modelGroupDialogDirectory = Path.GetDirectoryName(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Models", "ModelGroupDialogFactory.cs"))!;
        var modelGroupDialog = string.Join(
            Environment.NewLine,
            Directory.GetFiles(modelGroupDialogDirectory, "ModelGroupDialogFactory*.cs")
                .OrderBy(path => path, StringComparer.Ordinal)
                .Select(File.ReadAllText));
        Assert.Contains("Loc.T(\"ModelGroups.NewGroup\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"ModelGroups.Edit\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("ShowGroupEditor(", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("Dialog(owner, title, 440, 330)", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("CompactToolbar(Loc.T(\"ModelGroups.Title\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("PageSectionFactory.GridFrame(grid)", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"ModelGroups.DeleteGroup\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"ModelGroups.Column.Policy\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"ModelGroups.Column.IdleMinutes\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"ModelGroups.Column.Priority\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.Contains("Loc.T(\"ModelGroups.EditDescription\")", modelGroupDialog, StringComparison.Ordinal);
        Assert.DoesNotContain("RowDetailsVisibilityMode", modelsFactory, StringComparison.Ordinal);
        Assert.DoesNotContain("Saved launch variant. Same GGUF file", File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "ViewModels", "ModelsPageViewModel.cs")), StringComparison.Ordinal);
        Assert.Contains("private readonly ModelsPageState _modelsPage;", source, StringComparison.Ordinal);
        Assert.Contains("_modelsPage = uiState.ModelsPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly ModelsPageState _modelsPage = new();", source, StringComparison.Ordinal);
        Assert.Contains("_modelsPage.Apply(modelsPage);", source, StringComparison.Ordinal);
        Assert.Contains("public sealed class ModelsPageState", modelsPageState, StringComparison.Ordinal);
        Assert.Contains("public ModelRecord? SelectedModel", modelsPageState, StringComparison.Ordinal);
        Assert.Contains("public void SelectModelAfterRefresh", modelsPageState, StringComparison.Ordinal);
        Assert.Contains("Launch.SaveAsNewButton", launchPanelFactory, StringComparison.Ordinal);
        Assert.Contains("SaveLaunchSettingsAsNewModelAsync", source, StringComparison.Ordinal);
        Assert.Contains("nameof(ModelGridRow.OpenFolderAction)", modelsFactory, StringComparison.Ordinal);
        Assert.Equal(1, modelsFactory.Split("nameof(ModelGridRow.OpenFolderAction)", StringSplitOptions.None).Length - 1);
        Assert.Contains("nameof(ModelGridRow.CanDelete)", modelsFactory, StringComparison.Ordinal);
        Assert.Contains("OpenModelFolderRow_Click", modelsPageActions, StringComparison.Ordinal);
        Assert.Contains("DeleteModelRow_Click", modelsPageActions, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Models.ModelDeletionApplication.DeleteAsync", source, StringComparison.Ordinal);
        Assert.Contains("ModelDeletionActions()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ModelAliasService.IsLaunchAlias(model)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("delete the downloaded model files", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_deleteModelButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteSelectedModelAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_loadModelButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_restartModelButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_unloadModelButton", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UpdateModelActionButtons", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelVariantsGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_modelsFolderText", source, StringComparison.Ordinal);
    }


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
        Assert.Contains("lifetimeMetrics.ListAsync()", lifetime, StringComparison.Ordinal);
        Assert.Contains("lifetimeMetrics.DeleteModelUsageAsync(modelId)", lifetime, StringComparison.Ordinal);
        Assert.Contains("lifetimeMetrics.DeleteAllUsageAsync()", lifetime, StringComparison.Ordinal);
        Assert.Contains("Reset lifetime token metrics for all models?", application, StringComparison.Ordinal);
        Assert.Contains("Only model rows can be reset individually.", application, StringComparison.Ordinal);
        Assert.Contains("_stateStore.AddTokenUsageAsync(delta.ModelId", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_stateStore.ListTokenUsageAsync()", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_stateStore.DeleteTokenUsageAsync(modelId)", metricsApplication, StringComparison.Ordinal);
        Assert.Contains("_stateStore.DeleteAllTokenUsageAsync()", metricsApplication, StringComparison.Ordinal);
        Assert.DoesNotContain("_stateStore.AddTokenUsageAsync", lifetime, StringComparison.Ordinal);
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
        Assert.Contains("_coreServices.App.SettingsPageDefinitions.BuildRows(_settings)", source, StringComparison.Ordinal);
        Assert.Contains("private readonly SettingsPageState _settingsPage;", source, StringComparison.Ordinal);
        Assert.Contains("_settingsPage = uiState.SettingsPage", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private readonly SettingsPageState _settingsPage = new();", source, StringComparison.Ordinal);
        Assert.Contains("_settingsPage.Apply(", source, StringComparison.Ordinal);
        Assert.Contains("Rows.ToDictionary", settingsPersistence, StringComparison.Ordinal);
        Assert.Contains("ScheduleSettingsApply", settingsPersistence, StringComparison.Ordinal);
        Assert.Contains("public sealed class SettingsPageState", settingsPageState, StringComparison.Ordinal);
        Assert.Contains("public string SelectedThemeValue", settingsPageState, StringComparison.Ordinal);
        Assert.DoesNotContain("_themeCombo", source, StringComparison.Ordinal);
        Assert.Contains("CacheMaintenanceService.Size(settings.CacheRoot)", settingsDefinitions, StringComparison.Ordinal);
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
        var rows = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Models", "UiRows.cs"));

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
        var lifetimeFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Lifetime", "LifetimePageFactory.cs"));
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
        Assert.Contains("Loc.T(\"Lifetime.TokenUsageTitle\")", lifetimeFactory, StringComparison.Ordinal);
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
