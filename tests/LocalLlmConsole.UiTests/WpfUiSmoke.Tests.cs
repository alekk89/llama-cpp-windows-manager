using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
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
                Assert.Equal("v2.3.0", appVersionText.Text);
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
                        (_, _) => { }),
                    _ => { }));
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
                        () => Task.CompletedTask,
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

                LocalLlmConsole.MetricCardFactory.SetMetricText(
                    overview.RuntimeDashboardGpu,
                    "CPU: AMD Ryzen 9 7950X\nTelemetry: 18.5% load | 16C/32T | 57.2 °C thermal\nGPU 0: AMD Radeon RX 7900 XTX | 53.4% | 8.0/24.0 GiB");
                Assert.Equal(3, overview.RuntimeDashboardGpu.RowDefinitions.Count);

                var metricCards = new[]
                {
                    overview.RuntimeDashboardModel,
                    overview.RuntimeDashboardGpu,
                    overview.RuntimeDashboardKvCache,
                    overview.RuntimeDashboardTokens,
                    overview.RuntimeDashboardMtpTokens,
                    overview.RuntimeDashboardSlots
                }.Select(MetricCard).ToArray();
                Assert.All(metricCards, card => Assert.Equal(104, card.Height));
                var metricDashboard = Assert.IsType<Grid>(metricCards[0].Parent);
                Assert.Equal(2, metricDashboard.ColumnDefinitions.Count);
                Assert.All(VisualDescendants<GridSplitter>(overview.Root), splitter => Assert.True(splitter.ShowsPreview));

                overview.RuntimeDashboardTokensGraph.Push("model|runtime|8081", 35, 17.5);
                overview.RuntimeDashboardMtpTokensGraph.Push("model|runtime|8081", 4.5, 3.5);
                overview.RuntimeDashboardKvCacheGraph.Push("model|runtime|8081", 47.25);
                LocalLlmConsole.MetricCardFactory.SetMetricText(overview.RuntimeDashboardTokens, "Gen 35 t/s\nPrompt 17.5 t/s");
                var unchangedMetricChild = overview.RuntimeDashboardTokens.Children[0];
                LocalLlmConsole.MetricCardFactory.SetMetricText(overview.RuntimeDashboardTokens, "Gen 35 t/s\nPrompt 17.5 t/s");
                Assert.Same(unchangedMetricChild, overview.RuntimeDashboardTokens.Children[0]);
                LocalLlmConsole.MetricCardFactory.SetMetricText(overview.RuntimeDashboardMtpTokens, "Inactive");
                LocalLlmConsole.MetricCardFactory.SetMetricText(overview.RuntimeDashboardKvCache, "Used 8,192 t | 50%\nCapacity 16,384 t | unified");
                overview.Root.UpdateLayout();
                Assert.Equal(1, overview.RuntimeDashboardTokensGraph.SampleCount);
                Assert.Equal(1, overview.RuntimeDashboardMtpTokensGraph.SampleCount);
                Assert.Equal(1, overview.RuntimeDashboardKvCacheGraph.SampleCount);
                var graphOffsets = new[]
                {
                    (Graph: overview.RuntimeDashboardTokensGraph, Card: MetricCard(overview.RuntimeDashboardTokens)),
                    (Graph: overview.RuntimeDashboardMtpTokensGraph, Card: MetricCard(overview.RuntimeDashboardMtpTokens)),
                    (Graph: overview.RuntimeDashboardKvCacheGraph, Card: MetricCard(overview.RuntimeDashboardKvCache))
                }.Select(item => item.Graph.TranslatePoint(new Point(0, 0), item.Card).Y).ToArray();
                Assert.All(graphOffsets, offset => Assert.Equal(graphOffsets[0], offset, precision: 1));
                Assert.All(
                    new[]
                    {
                        overview.RuntimeDashboardTokens,
                        overview.RuntimeDashboardMtpTokens,
                        overview.RuntimeDashboardKvCache
                    },
                    metric => Assert.All(
                        metric.Children.OfType<TextBlock>(),
                        line => Assert.Equal(2, Grid.GetColumnSpan(line))));

                var modelBar = Assert.IsType<Grid>(overview.ModelCombo.Parent);
                overview.Root.Measure(new Size(700, 680));
                overview.Root.Arrange(new Rect(0, 0, 700, 680));
                overview.Root.UpdateLayout();
                Assert.Equal(2, metricDashboard.ColumnDefinitions.Count);
                Assert.Equal(1, Grid.GetRow(overview.LaunchProfileCombo));
                Assert.Equal(2, Grid.GetRowSpan(overview.LoadButton));
                Assert.True(modelBar.ActualWidth < 760);

                overview.Root.InvalidateMeasure();
                overview.Root.Measure(new Size(1024, 680));
                overview.Root.Arrange(new Rect(0, 0, 1024, 680));
                overview.Root.UpdateLayout();
                Assert.Equal(2, metricDashboard.ColumnDefinitions.Count);
                Assert.Equal(0, Grid.GetRow(overview.LaunchProfileCombo));
                Assert.Equal(1, Grid.GetRowSpan(overview.LoadButton));
                Assert.Equal(1, Grid.GetRow(MetricCard(overview.RuntimeDashboardSlots)));
                Assert.Equal(0, Grid.GetColumn(MetricCard(overview.RuntimeDashboardSlots)));

                overview.Root.InvalidateMeasure();
                overview.Root.Measure(new Size(1180, 680));
                overview.Root.Arrange(new Rect(0, 0, 1180, 680));
                overview.Root.UpdateLayout();
                Assert.Equal(3, metricDashboard.ColumnDefinitions.Count);
                Assert.Equal(0, Grid.GetRow(MetricCard(overview.RuntimeDashboardSlots)));
                Assert.Equal(2, Grid.GetColumn(MetricCard(overview.RuntimeDashboardSlots)));
                Assert.Equal(1, Grid.GetRow(MetricCard(overview.RuntimeDashboardTokens)));
                Assert.Equal(1, Grid.GetRow(MetricCard(overview.RuntimeDashboardMtpTokens)));
                Assert.Equal(1, Grid.GetRow(MetricCard(overview.RuntimeDashboardKvCache)));
                Assert.All(
                    new[]
                    {
                        overview.RuntimeDashboardTokensGraph,
                        overview.RuntimeDashboardMtpTokensGraph,
                        overview.RuntimeDashboardKvCacheGraph
                    },
                    graph => Assert.Equal(28, graph.ActualHeight));

                var overviewState = new LocalLlmConsole.OverviewPageState();
                overviewState.Apply(overview);
                overviewState.ApplyUiPreferences(settings with
                {
                    ShowOverviewHardware = false,
                    ShowOverviewMtpTokens = false,
                    ShowOverviewLiveRuntimeLog = false,
                    ShowOverviewAllMetrics = false
                });
                overview.Root.UpdateLayout();
                Assert.Equal(Visibility.Collapsed, MetricCard(overview.RuntimeDashboardGpu).Visibility);
                Assert.Equal(Visibility.Collapsed, MetricCard(overview.RuntimeDashboardMtpTokens).Visibility);
                Assert.Equal(Visibility.Collapsed, overview.RuntimeLogSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.MetricsSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.RuntimeSectionsSplitter.Visibility);
                Assert.Equal(0, overview.Root.RowDefinitions[2].Height.Value);
                Assert.Equal(0, overview.Root.RowDefinitions[3].Height.Value);
                Assert.Equal(0, overview.Root.RowDefinitions[4].Height.Value);
                Assert.Equal(3, metricDashboard.ColumnDefinitions.Count);
                Assert.Equal(2, metricDashboard.RowDefinitions.Count);

                overviewState.ApplyUiPreferences(settings with
                {
                    ShowOverviewModelStatus = false,
                    ShowOverviewHardware = false,
                    ShowOverviewSlots = false,
                    ShowOverviewTokens = false,
                    ShowOverviewMtpTokens = false,
                    ShowOverviewKvCache = false
                });
                Assert.Equal(Visibility.Collapsed, overview.ModelStatusSection.Visibility);
                overviewState.ApplyUiPreferences(settings);
                Assert.Equal(Visibility.Visible, overview.ModelStatusSection.Visibility);
                Assert.Equal(Visibility.Visible, overview.RuntimeLogSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.MetricsSection.Visibility);
                Assert.Equal(Visibility.Collapsed, overview.RuntimeSectionsSplitter.Visibility);

                for (var sample = 0; sample < 65; sample++)
                    overview.RuntimeDashboardTokensGraph.Push("model|runtime|8081", sample, sample / 2.0);
                Assert.Equal(60, overview.RuntimeDashboardTokensGraph.SampleCount);
                overview.RuntimeDashboardTokensGraph.Push("other|runtime|8082", 1, 2);
                Assert.Equal(1, overview.RuntimeDashboardTokensGraph.SampleCount);

                overview.Root.Measure(new Size(580, 900));
                overview.Root.Arrange(new Rect(0, 0, 580, 900));
                overview.Root.UpdateLayout();
                Assert.Single(metricDashboard.ColumnDefinitions);
                Assert.All(metricCards, card => Assert.Equal(104, card.ActualHeight));

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
