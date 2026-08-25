using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed partial class WpfUiSmokeTests
{
    [Fact]
    public async Task CoreWpfSurfacesComposeResizeFilterAndExposeSessionUnloadOnStaThread()
    {
        await RunStaAsync(() =>
        {
            LocalLlmConsole.Localization.Loc.LoadLanguage("en");
            var app = new LocalLlmConsole.App();
            app.InitializeComponent();

            var window = new LocalLlmConsole.MainWindow
            {
                Width = 1024,
                Height = 680
            };
            try
            {
                var windowContent = Assert.IsAssignableFrom<FrameworkElement>(window.Content);
                windowContent.Measure(new Size(1024, 680));
                windowContent.Arrange(new Rect(0, 0, 1024, 680));
                windowContent.UpdateLayout();

                var appStatusText = Assert.IsType<TextBlock>(window.FindName("AppStatusText"));
                var helpButton = Assert.IsType<Button>(window.FindName("HelpNavButton"));
                var appVersionText = Assert.IsType<TextBlock>(window.FindName("AppVersionText"));
                var navigationToggle = Assert.IsType<Button>(window.FindName("NavigationToggleButton"));
                var minimizeButton = Assert.IsType<Button>(window.FindName("MinimizeButton"));
                var maximizeButton = Assert.IsType<Button>(window.FindName("MaximizeButton"));
                var closeButton = Assert.IsType<Button>(window.FindName("CloseButton"));
                var languageCombo = Assert.IsType<ComboBox>(window.FindName("LanguageCombo"));
                var sidebarNavigation = Assert.IsType<Border>(window.FindName("SidebarNavigation"));
                var sidebarColumn = Assert.IsType<ColumnDefinition>(window.FindName("SidebarColumn"));
                var statusCard = Assert.IsType<Border>(Assert.IsType<StackPanel>(appStatusText.Parent).Parent);
                var helpY = helpButton.TranslatePoint(new Point(0, 0), windowContent).Y;
                var statusY = statusCard.TranslatePoint(new Point(0, 0), windowContent).Y;
                Assert.True(statusY > helpY + helpButton.ActualHeight, $"Help bottom {helpY + helpButton.ActualHeight}, status top {statusY}.");
                Assert.True(statusY + statusCard.ActualHeight <= 680, $"Status bottom {statusY + statusCard.ActualHeight}.");
                Assert.Equal("v2.4.0", appVersionText.Text);
                Assert.Equal(28, navigationToggle.Width);
                var navigationToggleGlyph = Assert.IsType<TextBlock>(navigationToggle.Content);
                Assert.Equal("\uE700", navigationToggleGlyph.Text);
                Assert.Equal(13, navigationToggleGlyph.FontSize);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(minimizeButton)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(maximizeButton)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(closeButton)));
                Assert.Equal("Language", AutomationProperties.GetName(languageCombo));
                Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(appStatusText));
                Assert.Equal(244, sidebarColumn.Width.Value);
                Assert.Equal(Visibility.Visible, sidebarNavigation.Visibility);
                navigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(0, sidebarColumn.Width.Value);
                Assert.Equal(Visibility.Collapsed, sidebarNavigation.Visibility);
                Assert.Equal("Expand navigation menu.", navigationToggle.ToolTip);
                navigationToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(244, sidebarColumn.Width.Value);
                Assert.Equal(Visibility.Visible, sidebarNavigation.Visibility);
                Assert.All(
                    new[] { "OverviewNavButton", "ModelsNavButton", "RuntimesNavButton", "HelpNavButton" }
                        .Select(name => Assert.IsType<Button>(window.FindName(name))),
                    button => Assert.True(button.MinHeight >= 40));

                var applyLocalizedStrings = typeof(LocalLlmConsole.MainWindow).GetMethod(
                    "ApplyLocalizedXamlStrings",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(applyLocalizedStrings);
                LocalLlmConsole.Localization.Loc.LoadLanguage("ar");
                applyLocalizedStrings.Invoke(window, null);
                Assert.Equal(FlowDirection.RightToLeft, window.FlowDirection);
                var copiedEndpointValue = "";
                const string endpointApiKey = "endpoint-report-test-api-key-1234567890";
                var endpointDialog = LocalLlmConsole.EndpointInspectionDialogFactory.Create(
                    window,
                    new EndpointInspectionReport(
                        EndpointInspectionKind.Gateway,
                        "Gateway",
                        "http://127.0.0.1:8082/v1",
                        "Ready",
                        DateTimeOffset.UtcNow,
                        [new EndpointInspectionModel("route-id", "Route", "manager", "default", null, 32768, null, 7_000_000_000, 4_000_000_000)],
                        null,
                        [],
                        [],
                        "Keep loaded",
                        "Loopback",
                        []),
                    endpointApiKey,
                    value => copiedEndpointValue = value);
                var endpointDialogContent = Assert.IsAssignableFrom<FrameworkElement>(endpointDialog.Content);
                endpointDialogContent.Measure(new Size(760, 560));
                endpointDialogContent.Arrange(new Rect(0, 0, 760, 560));
                endpointDialogContent.UpdateLayout();
                var endpointDialogRoot = Assert.IsAssignableFrom<DependencyObject>(endpointDialogContent);
                Assert.Equal(FlowDirection.RightToLeft, endpointDialog.FlowDirection);
                Assert.Contains("فحص", endpointDialog.Title, StringComparison.Ordinal);
                Assert.Contains(
                    VisualDescendants<TextBlock>(endpointDialogRoot),
                    text => text.Text.Contains("تقرير", StringComparison.Ordinal));
                var selectableEndpoint = Assert.Single(
                    VisualDescendants<TextBox>(endpointDialogRoot),
                    textBox => textBox.Text == "http://127.0.0.1:8082/v1");
                Assert.True(selectableEndpoint.IsReadOnly);
                Assert.False(selectableEndpoint.IsReadOnlyCaretVisible);
                Assert.Null(selectableEndpoint.FocusVisualStyle);
                Assert.Equal(new Thickness(0), selectableEndpoint.BorderThickness);
                Assert.Equal(System.Windows.Media.Brushes.Transparent, selectableEndpoint.Background);
                Assert.Equal(0, selectableEndpoint.MinHeight);
                var endpointTable = Assert.Single(VisualDescendants<DataGrid>(endpointDialogRoot));
                Assert.Equal(DataGridSelectionMode.Extended, endpointTable.SelectionMode);
                Assert.Equal(DataGridSelectionUnit.CellOrRowHeader, endpointTable.SelectionUnit);
                Assert.Equal(DataGridClipboardCopyMode.IncludeHeader, endpointTable.ClipboardCopyMode);
                var copyButtons = VisualDescendants<Button>(endpointDialogRoot)
                    .Where(button => AutomationProperties.GetAutomationId(button).StartsWith("EndpointCopy", StringComparison.Ordinal))
                    .ToDictionary(AutomationProperties.GetAutomationId, StringComparer.Ordinal);
                copyButtons["EndpointCopyEndpointButton"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("http://127.0.0.1:8082/v1", copiedEndpointValue);
                copyButtons["EndpointCopyReportButton"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains("http://127.0.0.1:8082/v1", copiedEndpointValue, StringComparison.Ordinal);
                Assert.Contains("route-id", copiedEndpointValue, StringComparison.Ordinal);
                Assert.DoesNotContain(endpointApiKey, copiedEndpointValue, StringComparison.Ordinal);
                copyButtons["EndpointCopyApiKeyButton"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(endpointApiKey, copiedEndpointValue);
                endpointDialog.Close();
                LocalLlmConsole.Localization.Loc.LoadLanguage("en");
                applyLocalizedStrings.Invoke(window, null);
                Assert.Equal(FlowDirection.LeftToRight, window.FlowDirection);

                Assert.Null(window.FindName("ControlApiNavButton"));

                var runtimeDiscoveryCancellationField = typeof(LocalLlmConsole.MainWindow).GetField(
                    "_runtimeLaunchOptionDiscoveryCancellation",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                var cancelRuntimeDiscovery = typeof(LocalLlmConsole.MainWindow).GetMethod(
                    "CancelRuntimeLaunchOptionDiscovery",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(runtimeDiscoveryCancellationField);
                Assert.NotNull(cancelRuntimeDiscovery);
                runtimeDiscoveryCancellationField.SetValue(window, new CancellationTokenSource());
                cancelRuntimeDiscovery.Invoke(window, null);
                cancelRuntimeDiscovery.Invoke(window, null);
                Assert.Null(runtimeDiscoveryCancellationField.GetValue(window));

                var settings = AssertLaunchSettingsSurface();
                var viewModel = new MainWindowViewModel();
                viewModel.Overview.ReplaceSessions([RunningSession(settings)]);
                viewModel.Overview.ReplaceLaunchProfiles([
                    new NamedModelLaunchProfile("default:model-1", "model-1", "Default", ModelLaunchSettings.FromAppSettings(settings), DateTimeOffset.UtcNow, true)
                ]);
                var overview = LocalLlmConsole.OverviewPageFactory.Create(new LocalLlmConsole.OverviewPageRequest(
                    viewModel,
                    new LocalLlmConsole.OverviewPageActions(
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        (_, _) => { },
                        (_, _) => { },
                        _ => Task.CompletedTask,
                        action => action(),
                        action => action()),
                    _ => { },
                    OverviewDashboardLayoutPolicy.Default));
                overview.LaunchProfileCombo.SelectedIndex = 0;
                overview.Root.Measure(new Size(900, 680));
                overview.Root.Arrange(new Rect(0, 0, 900, 680));
                overview.Root.UpdateLayout();
                Assert.Equal(0, Grid.GetRow(overview.LoadButton));
                Assert.Equal(1, Grid.GetRowSpan(overview.LoadButton));
                Assert.Equal(240, overview.ModelCombo.Width);
                Assert.Equal(220, overview.LaunchProfileCombo.Width);
                Assert.Equal(Grid.GetRow(overview.ModelCombo), Grid.GetRow(overview.LaunchProfileCombo));
                Assert.Equal(Grid.GetRow(overview.ModelCombo), Grid.GetRow(overview.LoadButton));
                Assert.True(Grid.GetColumn(overview.LoadButton) > Grid.GetColumn(overview.LaunchProfileCombo));
                Assert.InRange(overview.LoadButton.ActualHeight, 28, 36);
                var overviewStateForLoad = new LocalLlmConsole.OverviewPageState();
                overviewStateForLoad.Apply(overview);
                overviewStateForLoad.SetModelActionsEnabled(hasSelection: true, hasProfileSelection: true, selectedProfileLoaded: true, selectedModelMissing: false);
                Assert.Equal(Visibility.Visible, overview.LoadButton.Visibility);
                Assert.Equal("Loaded", overview.LoadButton.Content);
                Assert.False(overview.LoadButton.IsEnabled);
                overviewStateForLoad.SetModelActionsEnabled(hasSelection: true, hasProfileSelection: true, selectedProfileLoaded: false, selectedModelMissing: false);
                Assert.Equal(Visibility.Visible, overview.LoadButton.Visibility);
                Assert.Equal("Load", overview.LoadButton.Content);
                Assert.True(overview.LoadButton.IsEnabled);
                overviewStateForLoad.SetModelActionsEnabled(hasSelection: true, hasProfileSelection: true, selectedProfileLoaded: false, selectedModelMissing: true);
                Assert.False(overview.LoadButton.IsEnabled);
                Assert.Equal("The model file is missing. Restore it or remove the catalog entry before loading.", overview.LoadButton.ToolTip);
                Assert.True(ToolTipService.GetShowOnDisabled(overview.LoadButton));
                var launchProfileText = VisualDescendants<TextBlock>(overview.LaunchProfileCombo)
                    .Select(text => text.Text)
                    .ToArray();
                Assert.Contains("Default", launchProfileText);
                Assert.DoesNotContain(launchProfileText, text => text.Contains(nameof(OverviewLaunchProfileChoice), StringComparison.Ordinal));
                Assert.Equal(8, overview.LoadedSessionsGrid.Columns.Count);
                Assert.Equal("Default", viewModel.Overview.SessionRows[0].C2);
                Assert.Equal("Unload", viewModel.Overview.SessionRows[0].C8);
                Assert.True(viewModel.Overview.SessionRows[0].B1);
                Assert.True(viewModel.Overview.SessionRows[0].B2);
                Assert.Equal("http://127.0.0.1:8081/v1", viewModel.Overview.SessionRows[0].T1);
                Assert.Empty(viewModel.Overview.SessionRows[0].T2);
                Assert.IsType<DataGridTemplateColumn>(overview.LoadedSessionsGrid.Columns[4]);
                Assert.Contains("Double-click", overview.LoadedSessionsGrid.ToolTip?.ToString(), StringComparison.Ordinal);
                Assert.False(overview.RuntimeLogBox.IsUndoEnabled);
                var unloadAction = Assert.Single(
                    VisualDescendants<Button>(overview.LoadedSessionsGrid),
                    button => Equals(button.Content, "Unload"));
                Assert.Equal("Unload", AutomationProperties.GetName(unloadAction));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(unloadAction)));

                var runtimeDashboardState = new LocalLlmConsole.RuntimeDashboardPageState();
                runtimeDashboardState.Apply(overview);
                var logTextChanges = 0;
                overview.RuntimeLogBox.TextChanged += (_, _) => logTextChanges++;
                var firstLog = string.Join(Environment.NewLine, Enumerable.Range(0, 200).Select(index => $"line {index}"));
                runtimeDashboardState.SetRuntimeLogText(firstLog, followTail: true);
                overview.Root.UpdateLayout();
                var logScrollViewer = Assert.Single(VisualDescendants<ScrollViewer>(overview.RuntimeLogBox));
                Assert.Equal(logScrollViewer.ScrollableHeight, logScrollViewer.VerticalOffset, precision: 1);

                runtimeDashboardState.SetRuntimeLogText(firstLog, followTail: true);
                Assert.Equal(1, logTextChanges);

                logScrollViewer.ScrollToVerticalOffset(0);
                var secondLog = firstLog + Environment.NewLine + "new tail line";
                runtimeDashboardState.SetRuntimeLogText(secondLog, followTail: true);
                Assert.Equal(0, logScrollViewer.VerticalOffset, precision: 1);
                Assert.True(logScrollViewer.ScrollableHeight > 0);

                logScrollViewer.ScrollToEnd();
                runtimeDashboardState.SetRuntimeLogText(secondLog + Environment.NewLine + "another tail line", followTail: true);
                Assert.Equal(logScrollViewer.ScrollableHeight, logScrollViewer.VerticalOffset, precision: 1);

                var alternateSettings = ModelLaunchSettings.FromAppSettings(settings with { Port = 8099 });
                var alternateProfile = new NamedModelLaunchProfile(
                    "profile:model-1:alternate",
                    "model-1",
                    "Alternate",
                    alternateSettings,
                    DateTimeOffset.UtcNow);
                var ungroupedProfile = alternateProfile with
                {
                    Id = "profile:model-1:ungrouped",
                    Name = "Ungrouped",
                    Settings = alternateSettings with { Port = 8100 }
                };
                var modelRow = new ModelGridRow { Name = "Qwen", Quant = "Q4_K_M", Size = "4 GiB", Model = RunningModel() };
                var profileRow = new ModelGridRow
                {
                    Name = alternateProfile.Name,
                    Quant = "Profile",
                    Size = "4 GiB",
                    Model = modelRow.Model,
                    LaunchProfile = alternateProfile
                };
                var modelGrid = new DataGrid { ItemsSource = new[] { modelRow } };
                var profileGrid = new DataGrid { ItemsSource = new[] { profileRow } };
                var modelsState = new LocalLlmConsole.ModelsPageState();
                modelsState.Apply(new LocalLlmConsole.ModelsPageControls(
                    new Grid(),
                    new TextBlock(),
                    modelGrid,
                    profileGrid,
                    new Grid(),
                    new GridSplitter(),
                    new TextBox(),
                    new DataGrid()));
                modelGrid.SelectedItem = modelRow;
                profileGrid.SelectedItem = profileRow;

                Assert.Equal(modelRow.Model.Id, modelsState.SelectedModel?.Id);
                Assert.Equal(alternateProfile.Id, modelsState.SelectedLaunchProfileId);
                Assert.Equal(8099, modelsState.SelectedLaunchProfile?.Settings.Port);

                viewModel.Models.ReplaceModels(
                    [modelRow.Model],
                    _ => false,
                    [ungroupedProfile, alternateProfile],
                    new Dictionary<string, string> { [modelRow.Model.Id] = "4 GiB" },
                    new Dictionary<string, ModelGroupRecord>
                    {
                        [alternateProfile.Id] = new(
                            "group:interactive", "Interactive", ModelGroupRetentionMode.Pinned, 30,
                            ModelGroupEvictionPriority.High, DateTimeOffset.UtcNow)
                    });
                var modelPage = LocalLlmConsole.ModelsPageFactory.Create(new LocalLlmConsole.ModelsPageRequest(
                    viewModel,
                    settings.ModelsRoot,
                    new Grid(),
                    new LocalLlmConsole.ModelsPageActions(
                        () => Task.CompletedTask, () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        () => { },
                        () => Task.CompletedTask,
                        (_, _) => Task.CompletedTask,
                        (_, _) => Task.CompletedTask,
                        (_, _) => Task.CompletedTask,
                        () => { },
                        (_, _) => { },
                        (_, _) => { },
                        (_, _) => { },
                        () => Task.CompletedTask,
                        () => Task.CompletedTask,
                        _ => { })));
                modelPage.Root.Measure(new Size(1024, 680));
                modelPage.Root.Arrange(new Rect(0, 0, 1024, 680));
                modelPage.Root.UpdateLayout();
                var liveModelsState = new LocalLlmConsole.ModelsPageState();
                liveModelsState.Apply(modelPage);
                liveModelsState.ApplyUiPreferences(settings);
                Assert.False(settings.ShowModelsHuggingFace);
                Assert.Equal(Visibility.Collapsed, modelPage.HuggingFaceSection.Visibility);
                Assert.Equal(Visibility.Collapsed, modelPage.HuggingFaceSplitter.Visibility);
                Assert.Equal(0, modelPage.Root.RowDefinitions[3].Height.Value);
                liveModelsState.ApplyUiPreferences(settings with { ShowModelsHuggingFace = true });
                Assert.Equal(Visibility.Visible, modelPage.HuggingFaceSection.Visibility);
                Assert.Equal(Visibility.Visible, modelPage.HuggingFaceSplitter.Visibility);
                Assert.Equal(230, modelPage.Root.RowDefinitions[3].Height.Value);
                Assert.Equal("Add", viewModel.Models.VariantRows[0].GroupAction);
                Assert.True(viewModel.Models.VariantRows[0].CanAssignGroup);
                Assert.Equal("Interactive", viewModel.Models.VariantRows[1].Group);
                Assert.Equal("", viewModel.Models.VariantRows[1].GroupAction);
                Assert.Equal("Click Interactive to change or remove this group assignment.", viewModel.Models.VariantRows[1].GroupToolTip);
                Assert.False(viewModel.Models.VariantRows[1].CanAssignGroup);
                Assert.DoesNotContain(modelPage.ModelsGrid.Columns, column => Equals(column.Header, "Group"));
                Assert.Contains(modelPage.ModelVariantsGrid.Columns, column => Equals(column.Header, "Group"));
                Assert.Contains(modelPage.ModelsGrid.Columns, column => Equals(column.Header, "Open Folder"));
                Assert.DoesNotContain(modelPage.ModelVariantsGrid.Columns, column => Equals(column.Header, "Open Folder"));
                AssertContextMenu(modelPage.ModelsGrid, viewModel.Models.Rows[0], "Open Folder", "Save New Profile", "Delete");
                AssertContextMenu(modelPage.ModelVariantsGrid, viewModel.Models.VariantRows[0], "Load", "Assign to group…", "Remove from group", "Remove");
                Assert.Contains(VisualDescendants<Button>(modelPage.Root), button => Equals(button.Content, "Groups…"));
                var inlineGroupButtons = VisualDescendants<Button>(modelPage.ModelVariantsGrid)
                    .Where(button => Equals(button.Content, "Add"))
                    .ToArray();
                Assert.Single(inlineGroupButtons);
                Assert.Equal(Visibility.Visible, inlineGroupButtons[0].Visibility);
                AssertGridActionButtonMatches(inlineGroupButtons[0], modelPage.ModelVariantsGrid, "Remove");
                var addGroupButtonWidth = inlineGroupButtons[0].ActualWidth;
                modelPage.ModelVariantsGrid.ScrollIntoView(viewModel.Models.VariantRows[1]);
                modelPage.ModelVariantsGrid.UpdateLayout();
                var variantButtons = VisualDescendants<Button>(modelPage.ModelVariantsGrid).ToArray();
                var groupNameButtons = variantButtons
                    .Where(button => button.Visibility == Visibility.Visible
                                     && LocalLlmConsole.VisualRole.GetButtonRole(button) == LocalLlmConsole.VisualRole.Quiet)
                    .ToArray();
                Assert.True(groupNameButtons.Length == 1, string.Join(" | ", variantButtons.Select(button =>
                    $"{button.Content ?? "<null>"}:{button.Visibility}:{LocalLlmConsole.VisualRole.GetButtonRole(button)}:{(button.DataContext as ModelGridRow)?.Group}")));
                var groupNameButton = groupNameButtons[0];
                Assert.Equal(addGroupButtonWidth, groupNameButton.ActualWidth, precision: 1);
                Assert.Equal(LocalLlmConsole.VisualRole.Quiet, LocalLlmConsole.VisualRole.GetButtonRole(groupNameButton));
                Assert.Equal("Interactive", Assert.IsType<ModelGridRow>(groupNameButton.DataContext).Group);
                Assert.Contains(VisualDescendants<TextBlock>(groupNameButton), text => text.Text == "Interactive");
                groupNameButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(
                    ["Change group…", "Remove from group"],
                    groupNameButton.ContextMenu!.Items.Cast<MenuItem>().Select(item => item.Header).ToArray());
                groupNameButton.ContextMenu.IsOpen = false;

                var dashboard = overview.DashboardController;
                dashboard.ApplyHardwareSummary(
                    "CPU: AMD Ryzen 9 7950X\nTelemetry: 18.5% load | 16C/32T | 57.2 °C thermal\nRAM: 12.0/32.0 GiB | 37.5%\nGPU 0: AMD Radeon RX 7900 XTX | 53.4% | 8.0/24.0 GiB");
                dashboard.ApplyMetricSummary(new RuntimeMetricSummaryPresentation(
                    "Gen 35 t/s\nPrompt 17.5 t/s",
                    "Generated 4.5 t/s\nAccepted 3.5 t/s",
                    "Active 1/1 | Queued 0\nBusy/decode 1.0",
                    "Used 8,192 t | 50%\nCapacity 16,384 t | unified",
                    null,
                    new RuntimeMetricGraphSample("model|runtime|8081", 35, 17.5, 4.5, 3.5, 50),
                    [new PrometheusSample("llama_active_slots", "state=busy", 1, "1", "gauge", "Active slots")],
                    new RuntimeMetricAtomicSnapshot(
                        35, 17.5, 30, 15, 1200, 600, 4.5, 3.5, 4, 3, 69, 47,
                        1, 1, 0, 1, 8192, 16384, 50, "Unified")));

                var metricCards = dashboard.Cards.Select(card => card.Root).ToArray();
                Assert.Equal(3, metricCards.Length);
                Assert.All(dashboard.Cards, card => Assert.True(card.Root.Height >= card.Layout.Bounds!.Height));
                var metricDashboard = dashboard.DashboardGrid;
                var metricCanvas = dashboard.DashboardCanvas;
                Assert.Empty(metricDashboard.ColumnDefinitions);
                Assert.Equal(3, metricCanvas.Children.Count);
                var cpuCard = dashboard.Cards.Single(card => card.MetricIds.Contains(OverviewDashboardMetricIds.Cpu));
                var cpuText = VisualDescendants<TextBlock>(cpuCard.Root).Select(block => block.Text).ToArray();
                Assert.Contains("18.5", cpuText);
                Assert.Contains("%", cpuText);
                Assert.Contains("AMD Ryzen 9 7950X · 16C/32T", cpuText);
                Assert.DoesNotContain(LocalLlmConsole.Localization.Loc.T("Dashboard.CustomCardTitle"), cpuText);
                Assert.DoesNotContain(VisualDescendants<Button>(cpuCard.Root), button => button.IsVisible);
                Assert.Equal(new CornerRadius(6), cpuCard.Root.CornerRadius);
                Assert.Equal(new Thickness(11, 9, 11, 9), cpuCard.Root.Padding);
                var cpuValue = VisualDescendants<TextBlock>(cpuCard.Root).Single(block => block.Text == "18.5");
                Assert.Contains("Cascadia Mono", cpuValue.FontFamily.Source, StringComparison.Ordinal);
                Assert.Equal(FontNumeralAlignment.Tabular, Typography.GetNumeralAlignment(cpuValue));
                AssertUnavailableMetricPolish();
                Assert.All(VisualDescendants<GridSplitter>(overview.Root), splitter => Assert.True(splitter.ShowsPreview));
                overview.Root.UpdateLayout();
                var graphCards = dashboard.Cards.Where(card => card.Graph is not null).ToArray();
                Assert.Equal(2, graphCards.Length);
                Assert.All(graphCards, card => Assert.Equal(2, card.Graph!.SampleCount));
                AssertDashboardCardsSeparated(dashboard);
                AssertDashboardGraphsAreNamed(graphCards);
                dashboard.ApplyLayout(dashboard.Layout);
                graphCards = dashboard.Cards.Where(card => card.Graph is not null).ToArray();
                Assert.All(graphCards, card => Assert.Equal(1, card.Graph!.SampleCount));

                AssertHardwareChartHistoryAndOptionalSensors(dashboard);
                AssertHiddenDashboardCardsDoNotReserveSpace();

                AssertDashboardPolish(overview);
                var tokensCard = dashboard.Cards.Single(card =>
                    card.MetricIds.Contains(OverviewDashboardMetricIds.AverageGenerationRate));
                var tokensMenu = OpenContextMenu(tokensCard.Root);
                var chartMenu = tokensMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.Chart")));
                var promptChartItem = chartMenu.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "Average prompt rate"));
                Assert.True(promptChartItem.StaysOpenOnClick);
                promptChartItem.IsChecked = true;
                promptChartItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.True(tokensMenu.IsOpen);
                tokensMenu.IsOpen = false;
                tokensCard = dashboard.Cards.Single(card =>
                    card.MetricIds.Contains(OverviewDashboardMetricIds.AverageGenerationRate));
                Assert.Single(tokensCard.Graphs);
                Assert.Contains(OverviewDashboardMetricIds.AveragePromptRate, tokensCard.Graphs.Keys);

                tokensMenu = OpenContextMenu(tokensCard.Root);
                var removeMetricMenu = tokensMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.RemoveMetric")));
                AssertDashboardSubmenuTemplate(removeMetricMenu);
                var promptRemoveItem = removeMetricMenu.Items.OfType<MenuItem>()
                    .Single(item => Equals(item.Header, "Average prompt rate"));
                Assert.True(promptRemoveItem.StaysOpenOnClick);
                promptRemoveItem.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                Assert.False(promptRemoveItem.IsEnabled);
                Assert.True(tokensMenu.IsOpen);
                tokensMenu.IsOpen = false;
                tokensCard = dashboard.Cards.Single(card =>
                    card.MetricIds.Contains(OverviewDashboardMetricIds.AverageGenerationRate));
                Assert.DoesNotContain(OverviewDashboardMetricIds.AveragePromptRate, tokensCard.MetricIds);
                Assert.Empty(tokensCard.Graphs);
                graphCards = dashboard.Cards.Where(card => card.Graph is not null).ToArray();

                var modelBar = Assert.IsType<Grid>(overview.ModelCombo.Parent);
                overview.Root.Measure(new Size(700, 680));
                overview.Root.Arrange(new Rect(0, 0, 700, 680));
                overview.Root.UpdateLayout();
                Assert.Empty(metricDashboard.ColumnDefinitions);
                Assert.Equal(1, Grid.GetRow(overview.LaunchProfileCombo));
                Assert.Equal(2, Grid.GetRowSpan(overview.LoadButton));
                Assert.True(modelBar.ActualWidth < 760);

                overview.Root.InvalidateMeasure();
                overview.Root.Measure(new Size(1024, 680));
                overview.Root.Arrange(new Rect(0, 0, 1024, 680));
                overview.Root.UpdateLayout();
                Assert.Empty(metricDashboard.ColumnDefinitions);
                Assert.Equal(0, Grid.GetRow(overview.LaunchProfileCombo));
                Assert.Equal(1, Grid.GetRowSpan(overview.LoadButton));
                var gpuCard = dashboard.Cards.Single(card => card.MetricIds.Contains(OverviewDashboardMetricIds.Gpu(0)));
                Assert.Equal(0, Canvas.GetTop(gpuCard.Root));
                Assert.Equal(Math.Max(metricDashboard.ActualWidth, dashboard.Layout.LockedSurfaceWidth!) * gpuCard.Layout.Bounds!.X / 12, Canvas.GetLeft(gpuCard.Root), precision: 1);
                overview.Root.InvalidateMeasure();
                overview.Root.Measure(new Size(1180, 680));
                overview.Root.Arrange(new Rect(0, 0, 1180, 680));
                overview.Root.UpdateLayout();
                Assert.Empty(metricDashboard.ColumnDefinitions);
                Assert.Equal(Math.Max(metricDashboard.ActualWidth, dashboard.Layout.LockedSurfaceWidth!) * gpuCard.Layout.Bounds!.X / 12, Canvas.GetLeft(gpuCard.Root), precision: 1);
                Assert.All(graphCards, card => Assert.Equal(30, card.Graph!.ActualHeight));

                var overviewState = new LocalLlmConsole.OverviewPageState();
                overviewState.Apply(overview);
                AssertOverviewSurfaceRetention(overviewState, overview, dashboard, settings);
                var hiddenMetricsLayout = OverviewDashboardLayoutPolicy.ApplyLegacyVisibilityChanges(
                    dashboard.Layout,
                    OverviewDashboardLayoutPolicy.LegacyVisibility(dashboard.Layout),
                    new OverviewDashboardLegacyVisibility(true, false, true, true, false, true));
                overviewState.ApplyUiPreferences(settings with
                {
                    OverviewDashboardLayout = hiddenMetricsLayout,
                    ShowOverviewLiveRuntimeLog = false,
                    ShowOverviewAllMetrics = false
                });
                overview.Root.UpdateLayout();
                Assert.DoesNotContain(dashboard.Cards, card => card.MetricIds.Any(id =>
                    id == OverviewDashboardMetricIds.Cpu || id == OverviewDashboardMetricIds.Ram
                    || OverviewDashboardMetricIds.IsGpuMetric(id)));
                Assert.DoesNotContain(dashboard.Cards, card => card.MetricIds.Any(id =>
                    id.StartsWith("overview.runtime.mtp.", StringComparison.Ordinal)));
                Assert.Equal(Visibility.Collapsed, overview.RuntimeLogSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.MetricsSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.RuntimeSectionsSplitter.Visibility);
                Assert.Equal(0, overview.Root.RowDefinitions[2].Height.Value);
                Assert.Equal(0, overview.Root.RowDefinitions[3].Height.Value);
                Assert.Equal(0, overview.Root.RowDefinitions[4].Height.Value);
                Assert.Empty(metricDashboard.ColumnDefinitions);
                Assert.Empty(metricDashboard.RowDefinitions);
                Assert.Equal(3, metricCanvas.Children.Count);

                EnsureDashboardCardSizesUnlocked(overview, dashboard);
                var originalMetricCount = dashboard.Layout.Cards[0].MetricIds.Count;
                var customLayout = OverviewDashboardLayoutPolicy.AddMetrics(
                    dashboard.Layout,
                    dashboard.Layout.Cards[0].Id,
                    [OverviewDashboardMetricIds.Prometheus("llama_active_slots", "state=busy")]);
                customLayout = OverviewDashboardLayoutPolicy.ResizeCard(
                    customLayout,
                    customLayout.Cards[0].Id,
                    2,
                    OverviewDashboardCardHeight.Tall);
                dashboard.ApplyLayout(customLayout);
                Assert.Equal(originalMetricCount + 1, dashboard.Cards[0].MetricIds.Count);
                Assert.True(dashboard.Cards[0].Root.Height >= 176);
                Assert.Equal(8, dashboard.Cards[0].Layout.Bounds!.Width);
                var configuredCardWidth = metricDashboard.ActualWidth * 8 / 12 - OverviewDashboardLayoutPolicy.CardGap;
                Assert.InRange(dashboard.Cards[0].Root.Width, dashboard.Cards[0].MinimumWidth, configuredCardWidth);

                Assert.True(dashboard.IsEditing);
                Assert.DoesNotContain(VisualDescendants<Button>(overview.Root),
                    button => Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Dashboard.Customize")));
                Assert.Contains(VisualDescendants<Button>(overview.Root),
                    button => Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Dashboard.AddCard")));
                Assert.All(dashboard.Cards, card =>
                {
                    var width = card.Root.ActualWidth > 0 ? card.Root.ActualWidth : card.Root.Width;
                    var height = card.Root.ActualHeight > 0 ? card.Root.ActualHeight : card.Root.Height;
                    Assert.Equal(OverviewDashboardResizeEdge.Left,
                        card.ResizeEdgeAt(new Point(1, height / 2)));
                    Assert.Equal(OverviewDashboardResizeEdge.Right,
                        card.ResizeEdgeAt(new Point(width - 1, height / 2)));
                    Assert.Equal(OverviewDashboardResizeEdge.Top,
                        card.ResizeEdgeAt(new Point(width / 2, 1)));
                    Assert.Equal(OverviewDashboardResizeEdge.Bottom,
                        card.ResizeEdgeAt(new Point(width / 2, height - 1)));
                    Assert.Equal(OverviewDashboardResizeEdge.Left | OverviewDashboardResizeEdge.Top,
                        card.ResizeEdgeAt(new Point(1, 1)));
                    Assert.Equal(OverviewDashboardResizeEdge.Right | OverviewDashboardResizeEdge.Top,
                        card.ResizeEdgeAt(new Point(width - 1, 1)));
                    Assert.Equal(OverviewDashboardResizeEdge.Left | OverviewDashboardResizeEdge.Bottom,
                        card.ResizeEdgeAt(new Point(1, height - 1)));
                    Assert.Equal(OverviewDashboardResizeEdge.Right | OverviewDashboardResizeEdge.Bottom,
                        card.ResizeEdgeAt(new Point(width - 1, height - 1)));
                    card.UpdatePointer(new Point(width - 1, height - 1));
                    Assert.Equal(Cursors.SizeNWSE, card.Root.Cursor);
                    card.ResetPointer();
                    Assert.Equal(Cursors.SizeAll, card.Root.Cursor);
                });

                var firstCardMenu = dashboard.Cards[0].Root.ContextMenu!;
                Assert.DoesNotContain(firstCardMenu.Items.OfType<MenuItem>(), item =>
                    Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.Customize"))
                    || Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.MoveEarlier"))
                    || Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.MoveLater"))
                    || Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.Size")));
                Assert.Single(firstCardMenu.Items.OfType<Separator>());

                var sessionRows = new[]
                {
                    new UiRow { C1 = "First", Data = new System.Text.Json.Nodes.JsonObject { ["SessionId"] = "session-1" } },
                    new UiRow { C1 = "Metrics source", Data = new System.Text.Json.Nodes.JsonObject { ["SessionId"] = "session-2" } }
                };
                overview.LoadedSessionsGrid.ItemsSource = sessionRows;
                overviewState.RestoreLoadedSessionSelection("session-2", sessionRows);
                Assert.Same(sessionRows[1], overview.LoadedSessionsGrid.SelectedItem);
                overviewState.RestoreLoadedSessionSelection("", sessionRows);
                Assert.Null(overview.LoadedSessionsGrid.SelectedItem);

                overviewState.ApplyUiPreferences(settings with { OverviewDashboardLayout = OverviewDashboardLayoutPolicy.Default });
                Assert.Equal(Visibility.Visible, overview.RuntimeLogSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.MetricsSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.RuntimeSectionsSplitter.Visibility);

                overview.Root.Measure(new Size(580, 900));
                overview.Root.Arrange(new Rect(0, 0, 580, 900));
                overview.Root.UpdateLayout();
                Assert.Empty(metricDashboard.ColumnDefinitions);
                Assert.All(dashboard.Cards, card => Assert.True(
                    card.Root.ActualHeight >= card.Layout.Bounds!.Height));
                Assert.All(dashboard.Cards, card => Assert.True(
                    card.Root.ActualWidth >= card.MinimumWidth));
                AssertDashboardCardsSeparated(dashboard);
                AssertHiddenDashboardOverflow(overview);
                AssertLifetimeUsageSurface();

                var persistedSettings = settings with { ModelApiKey = "persisted-key" };
                var settingDefinitions = new SettingsPageDefinitionService().BuildRows(persistedSettings);
                var settingsViewModel = new SettingsPageViewModel();
                settingsViewModel.ReplaceRows(settingDefinitions);
                var runtimesViewModel = new MainWindowViewModel();
                runtimesViewModel.Runtimes.ReplaceRows([
                    new RuntimeCatalogRow { Name = "CUDA", Backend = "CUDA Windows", State = "Built", Location = "cuda", Details = "", Vendor = RuntimeInventoryFilterService.Nvidia, Platform = RuntimeInventoryFilterService.Windows },
                    new RuntimeCatalogRow { Name = "Vulkan", Backend = "Vulkan WSL", State = "Built", Location = "vulkan", Details = "", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux }
                ]);
                runtimesViewModel.RuntimePackages.ReplaceRows([
                    new RuntimePackagePresetRow { Label = "CUDA", Vendor = RuntimeInventoryFilterService.Nvidia, Platform = RuntimeInventoryFilterService.Windows, BuildSourceAction = "Check", CanBuildSource = true, CheckAction = "Check", CanCheck = true },
                    new RuntimePackagePresetRow { Label = "Vulkan", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux, BuildSourceAction = "Download", CanBuildSource = true },
                    new RuntimePackagePresetRow { Label = "Add custom source repository", Vendor = RuntimeInventoryFilterService.All, Platform = RuntimeInventoryFilterService.All, BuildSourceAction = "Add", CanBuildSource = true }
                ]);
                var noOp = new RoutedEventHandler((_, _) => { });
                var runtimesControls = LocalLlmConsole.RuntimesPageFactory.Create(new LocalLlmConsole.RuntimesPageRequest(
                    runtimesViewModel,
                    settings.RuntimeRoot,
                    settings.CudaPackagePreference,
                    new LocalLlmConsole.RuntimesPageActions(
                        ChooseRuntimeFolderAsync: () => Task.CompletedTask,
                        ChangeCudaPackagePreferenceAsync: () => Task.CompletedTask,
                        RuntimeGridPreviewMouseLeftButtonDown: (_, _) => { },
                        VerifyRuntimeRowClick: noOp,
                        DeleteRuntimeRowClick: noOp,
                        RuntimeSourceRowClick: noOp,
                        InstallRuntimePackageRowClick: noOp,
                        CheckRuntimePackageUpdateRowClick: noOp,
                        DeleteRuntimePackageRowClick: noOp,
                        ConfigureRuntimeGridColumnSizing: _ => { },
                        ConfigureRuntimeBuildGridColumnSizing: _ => { })));
                var runtimeCombos = VisualDescendants<ComboBox>(runtimesControls.Root).ToDictionary(combo => combo.Name, StringComparer.Ordinal);
                var runtimeTitles = VisualDescendants<TextBlock>(runtimesControls.Root)
                    .Where(text => text.Text is "Installed Local Builds" or "Runtime Downloads")
                    .ToDictionary(text => text.Text, StringComparer.Ordinal);
                Assert.Same(runtimeTitles["Installed Local Builds"].Parent, ((FrameworkElement)runtimeCombos["InstalledRuntimeTypeFilter"].Parent).Parent);
                Assert.Same(runtimeTitles["Runtime Downloads"].Parent, ((FrameworkElement)runtimeCombos["RuntimeDownloadTypeFilter"].Parent).Parent);
                Assert.Equal(["All", "AMD", "Intel", "NVIDIA"], runtimeCombos["RuntimeDownloadTypeFilter"].Items.Cast<string>().ToArray());
                Assert.Equal(["All", "Windows", "Linux"], runtimeCombos["InstalledRuntimePlatformFilter"].Items.Cast<string>().ToArray());
                runtimeCombos["InstalledRuntimeTypeFilter"].SelectedItem = "AMD";
                runtimeCombos["InstalledRuntimePlatformFilter"].SelectedItem = "Linux";
                Assert.Equal("Vulkan", Assert.Single(runtimesViewModel.Runtimes.Rows).Name);
                runtimeCombos["RuntimeDownloadTypeFilter"].SelectedItem = "AMD";
                runtimeCombos["RuntimeDownloadPlatformFilter"].SelectedItem = "Linux";
                Assert.Equal(["Vulkan", "Add custom source repository"], runtimesViewModel.RuntimePackages.Rows.Select(row => row.Label).ToArray());
                Assert.Equal("Build from source", runtimesControls.RuntimePackageGrid.Columns[5].Header);
                Assert.Equal("Install", runtimesControls.RuntimePackageGrid.Columns[6].Header);
                Assert.All(runtimesControls.RuntimePackageGrid.Columns.Skip(5), column =>
                {
                    Assert.True(column.Width.IsStar);
                    Assert.Equal(.75, column.Width.Value);
                });
                Assert.Equal(2, VisualDescendants<DataGrid>(runtimesControls.Root).Count());
                runtimeCombos["RuntimeDownloadTypeFilter"].SelectedItem = "All";
                runtimeCombos["RuntimeDownloadPlatformFilter"].SelectedItem = "All";
                runtimesControls.Root.Measure(new Size(1024, 680));
                runtimesControls.Root.Arrange(new Rect(0, 0, 1024, 680));
                runtimesControls.Root.UpdateLayout();
                var runtimeCheckButtons = VisualDescendants<Button>(runtimesControls.RuntimePackageGrid)
                    .Where(button => Equals(button.Content, "Check"))
                    .ToArray();
                Assert.True(runtimeCheckButtons.Length >= 2);
                Assert.All(runtimeCheckButtons, button => Assert.Equal("", LocalLlmConsole.VisualRole.GetButtonRole(button)));
                Assert.DoesNotContain(VisualDescendants<TextBlock>(runtimesControls.Root), text => Equals(text.Text, "Registered llama-server builds found on disk."));
                Assert.DoesNotContain(VisualDescendants<TextBlock>(runtimesControls.Root), text => Equals(text.Text, "Install prebuilt runtimes, or check, download, and build source in the same row."));
                Assert.DoesNotContain(VisualDescendants<TextBlock>(runtimesControls.Root), text => Equals(text.Text, "Runtime Jobs"));
                Assert.DoesNotContain(VisualDescendants<Button>(runtimesControls.Root), button => Equals(button.Content, "Show advanced") || Equals(button.Content, "Hide advanced"));

                AssertSettingsAndHelpSurfaces(settingsViewModel, persistedSettings);
            }
            finally
            {
                typeof(LocalLlmConsole.MainWindow)
                    .GetMethod("DisposeTrayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
            }
        });
    }

}
