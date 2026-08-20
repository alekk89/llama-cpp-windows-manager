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
        var appXaml = ReadApplicationResourceSources();
        var source = ReadMainWindowSources();
        var theme = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "ApplicationThemeService.cs"));
        var metricFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "MetricCardFactory.cs"));
        var metricRenderer = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "MetricCardRenderer.cs"));
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
        Assert.Contains("MetricImportantValuePattern", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("SplitMetricLine", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("MetricShouldEmphasizeWholeLine", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("IsNeutralMetricStatus", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("MetricShouldRenderNeutralStatus", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("TryAddStatusNameMetricLine", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("MetricStatusNameBlock", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("MetricCardHeight = 104", metricFactory, StringComparison.Ordinal);
        Assert.Contains("ClipToBounds = true", metricFactory, StringComparison.Ordinal);
        Assert.Contains("TextWrapping = TextWrapping.NoWrap", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("TextTrimming = TextTrimming.CharacterEllipsis", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("MetricLabelColumnWidth(label)", metricFactory, StringComparison.Ordinal);
        Assert.Contains("=> string.Equals(label, Loc.T(\"Overview.Metric.ModelStatus\"), StringComparison.Ordinal)", metricRenderer, StringComparison.Ordinal);
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
        Assert.Contains("string.Equals(label, \"Overview.Metric.ModelStatus\", StringComparison.Ordinal)", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("\"Loaded Model:\"", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("\"Loading Model:\"", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("\"Loading:\"", metricRenderer, StringComparison.Ordinal);
        Assert.DoesNotContain("SetMetricText(_runtimeDashboardPage.RuntimeMetric", source, StringComparison.Ordinal);
        Assert.Contains("string.Equals(normalized, \"None\", StringComparison.OrdinalIgnoreCase)", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("string.Equals(normalized, \"Stopped\", StringComparison.OrdinalIgnoreCase)", metricRenderer, StringComparison.Ordinal);
        Assert.DoesNotContain("text.StartsWith(\"Loading \", StringComparison.OrdinalIgnoreCase)", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("MetricValueFont", metricRenderer, StringComparison.Ordinal);
        Assert.Contains("Typography.SetNumeralAlignment(valueRun, FontNumeralAlignment.Tabular)", metricRenderer, StringComparison.Ordinal);
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
        var appXaml = ReadApplicationResourceSources();
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
        var appXaml = ReadApplicationResourceSources();
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
        var responsiveCoordinator = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewPageResponsiveCoordinator.cs"));
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
        Assert.Contains("OverviewPageResponsiveCoordinator.ConfigureLoadButton(loadButton)", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("button.MinHeight = 30", responsiveCoordinator, StringComparison.Ordinal);
        Assert.Contains("Grid.SetColumn(loadButton, 6)", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Width = 240", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("Width = 220", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("OverviewPageResponsiveCoordinator.ConfigureModelBar", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("public static void ConfigureModelBar", responsiveCoordinator, StringComparison.Ordinal);
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
        var modelRuntimeCommands = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.Core", "Services", "Runtimes", "ModelRuntimeCommandDecisionService.cs"));
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


}
