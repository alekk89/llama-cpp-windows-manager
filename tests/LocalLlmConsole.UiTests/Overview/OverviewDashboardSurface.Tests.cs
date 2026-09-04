using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewDashboardTests : WpfUiTestBase
{
    [Fact]
    public async Task OverviewDashboardRendersResizesAndCustomizesIndependently()
    {
        await RunStaAsync(() =>
        {
            var (settings, overview) = CreateOverviewSurface();
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
            Assert.All(dashboard.Cards, card =>
            {
                Assert.True(card.Root.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(card.Root)));
                Assert.Contains("Ctrl+Arrow", System.Windows.Automation.AutomationProperties.GetHelpText(card.Root), StringComparison.Ordinal);
                Assert.All(card.MetricRows.Values, row =>
                {
                    Assert.True(row.Root.Focusable);
                    Assert.False(string.IsNullOrWhiteSpace(System.Windows.Automation.AutomationProperties.GetName(row.Root)));
                });
            });
            Assert.IsType<Style>(System.Windows.Application.Current.Resources["KeyboardFocusVisual"]);
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
            AssertDashboardCheckItemTemplate(promptChartItem);
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
                ShowOverviewModelSection = false,
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
            Assert.Equal(Visibility.Collapsed, overview.ModelStatusSection.Visibility);
            Assert.Equal(Visibility.Collapsed, overview.RuntimeSectionsSplitter.Visibility);
            Assert.True(overview.Root.RowDefinitions[0].Height.IsAuto);
            Assert.Equal(Visibility.Visible, overview.LoadedSessionsGrid.Visibility);
            Assert.Equal(Visibility.Visible, overview.ModelCombo.Visibility);
            Assert.Equal(0, overview.Root.RowDefinitions[1].Height.Value);
            Assert.Equal(0, overview.Root.RowDefinitions[2].Height.Value);
            Assert.Equal(0, overview.Root.RowDefinitions[3].Height.Value);
            Assert.Equal(0, overview.Root.RowDefinitions[4].Height.Value);
            Assert.Empty(metricDashboard.ColumnDefinitions);
            Assert.Empty(metricDashboard.RowDefinitions);
            Assert.Equal(3, metricCanvas.Children.Count);

            overviewState.ApplyUiPreferences(settings with { ShowOverviewModelSection = true });
            Assert.Equal(Visibility.Visible, overview.ModelStatusSection.Visibility);
            Assert.True(overview.Root.RowDefinitions[1].Height.IsAuto);

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
                new OverviewSessionRow
                {
                    Kind = OverviewEndpointKind.Session, ModelName = "First", ProfileName = "", Size = "",
                    State = "", Endpoint = "", Runtime = "", Backend = "", SessionId = "session-1"
                },
                new OverviewSessionRow
                {
                    Kind = OverviewEndpointKind.Session, ModelName = "Metrics source", ProfileName = "", Size = "",
                    State = "", Endpoint = "", Runtime = "", Backend = "", SessionId = "session-2"
                }
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
                    ToggleRuntimeFavoriteAsync: _ => Task.CompletedTask,
                    ToggleDefaultRuntimeAsync: _ => Task.CompletedTask,
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
                .Where(button => button is LocalLlmConsole.ResponsiveActionButton { FullLabel: "Check" })
                .ToArray();
            Assert.True(runtimeCheckButtons.Length >= 2);
            Assert.All(runtimeCheckButtons, button => Assert.Equal("", LocalLlmConsole.VisualRole.GetButtonRole(button)));
            Assert.DoesNotContain(VisualDescendants<TextBlock>(runtimesControls.Root), text => Equals(text.Text, "Registered llama-server builds found on disk."));
            Assert.DoesNotContain(VisualDescendants<TextBlock>(runtimesControls.Root), text => Equals(text.Text, "Install prebuilt runtimes, or check, download, and build source in the same row."));
            Assert.DoesNotContain(VisualDescendants<TextBlock>(runtimesControls.Root), text => Equals(text.Text, "Runtime Jobs"));
            Assert.DoesNotContain(VisualDescendants<Button>(runtimesControls.Root), button => Equals(button.Content, "Show advanced") || Equals(button.Content, "Hide advanced"));

            AssertSettingsAndHelpSurfaces(settingsViewModel, persistedSettings);

        });
    }

}
