using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfIsolatedSurfaceTests : WpfUiTestBase
{
    [Fact]
    public async Task SearchableComboBoxFiltersAsTheUserTypesAndRestoresItsCatalog()
    {
        await RunStaAsync(() =>
        {
            var choices = new[] { "Qwen 2.5", "Llama 3.1", "CUDA 3090 runtime" };
            var combo = new LocalLlmConsole.SearchableComboBox
            {
                ItemsSource = choices,
                SelectedItem = choices[0],
                Width = 260,
                FavoriteKeySelector = item => item?.ToString() ?? "",
                LoadFavoriteKeysAsync = () => Task.FromResult<IReadOnlySet<string>>(
                    new HashSet<string>([choices[1]], StringComparer.OrdinalIgnoreCase)),
                ToggleFavoriteAsync = _ => Task.FromResult(true)
            };
            var selectionChanges = 0;
            combo.SelectionChanged += (_, _) => selectionChanges++;
            var host = new Window { Content = combo, Width = 320, Height = 120, ShowInTaskbar = false };
            host.Show();
            try
            {
                combo.ApplyTemplate();
                Assert.Same(System.Windows.Application.Current.Resources[typeof(ComboBox)], combo.Style);
                Assert.False(combo.IsEditable);
                combo.IsDropDownOpen = true;
                Assert.False(combo.IsEditable);
                var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(combo.Template.FindName("PART_Popup", combo));
                var popupBorder = Assert.IsType<Border>(popup.Child);
                var popupGrid = Assert.IsType<Grid>(popupBorder.Child);
                var queryBox = Assert.IsType<TextBox>(popupGrid.Children[0]);
                Assert.Equal(choices[1], combo.Items.Cast<string>().First());
                var favoriteContainer = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromItem(choices[1]));
                favoriteContainer.ApplyTemplate();
                var favoriteChrome = Assert.IsType<Border>(favoriteContainer.Template.FindName("ItemChrome", favoriteContainer));
                Assert.Equal(new Thickness(8, 2, 8, 2), favoriteChrome.Padding);
                Assert.Equal(1, favoriteChrome.BorderThickness.Bottom);
                var composition = new TextComposition(InputManager.Current, combo, "3090");
                combo.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                {
                    RoutedEvent = TextCompositionManager.PreviewTextInputEvent
                });
                combo.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));

                Assert.Equal([choices[2]], combo.Items.Cast<string>().ToArray());
                Assert.Equal("3090", queryBox.Text);
                Assert.Equal("3090", combo.SearchQuery);

                combo.IsDropDownOpen = false;
                combo.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                Assert.False(combo.IsEditable);
                Assert.Equal([choices[1], choices[0], choices[2]], combo.Items.Cast<string>().ToArray());
                Assert.Equal(choices[0], combo.SelectedItem);
                Assert.Equal(0, selectionChanges);

                combo.IsDropDownOpen = true;
                combo.UpdateLayout();
                combo.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                var qwenContainer = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromItem(choices[0]));
                qwenContainer.ApplyTemplate();
                var itemChrome = Assert.IsType<Border>(qwenContainer.Template.FindName("ItemChrome", qwenContainer));
                var itemRow = Assert.IsType<Grid>(itemChrome.Child);
                var favoriteButton = Assert.Single(itemRow.Children.OfType<Button>());
                Assert.Equal(20, favoriteButton.Height);
                Assert.Equal(20, favoriteButton.Width);
                Assert.Equal(VerticalAlignment.Center, favoriteButton.VerticalAlignment);
                Assert.Equal(VerticalAlignment.Center, favoriteButton.VerticalContentAlignment);
                AssertVerticallyCentered(favoriteButton, itemRow);
                Assert.Equal(0, Grid.GetColumn(favoriteButton));
                Assert.Equal(1, Grid.GetColumn(Assert.Single(itemRow.Children.OfType<ContentPresenter>())));
                Assert.Equal("☆", favoriteButton.Content);
                favoriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                combo.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                Assert.Equal(choices[0], combo.Items.Cast<string>().First());
                qwenContainer = Assert.IsType<ComboBoxItem>(combo.ItemContainerGenerator.ContainerFromItem(choices[0]));
                qwenContainer.ApplyTemplate();
                itemChrome = Assert.IsType<Border>(qwenContainer.Template.FindName("ItemChrome", qwenContainer));
                itemRow = Assert.IsType<Grid>(itemChrome.Child);
                Assert.Equal("★", Assert.Single(itemRow.Children.OfType<Button>()).Content);
                composition = new TextComposition(InputManager.Current, combo, "llama");
                combo.RaiseEvent(new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
                {
                    RoutedEvent = TextCompositionManager.PreviewTextInputEvent
                });
                combo.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                combo.SelectedItem = choices[1];
                combo.IsDropDownOpen = false;
                combo.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Background, new Action(() => { }));
                Assert.Equal(choices[1], combo.SelectedItem);
                Assert.Equal(1, selectionChanges);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public async Task SearchableComboBoxOpensAtTheTopEvenWhenTheLastItemIsSelected()
    {
        await RunStaAsync(() =>
        {
            var choices = Enumerable.Range(1, 60).Select(index => $"Model {index:00}").ToArray();
            var combo = new LocalLlmConsole.SearchableComboBox
            {
                ItemsSource = choices,
                SelectedItem = choices[^1],
                Width = 260,
                MaxDropDownHeight = 140
            };
            var host = new Window { Content = combo, Width = 320, Height = 120, ShowInTaskbar = false };
            host.Show();
            try
            {
                combo.ApplyTemplate();
                combo.IsDropDownOpen = true;
                combo.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() => { }));
                var popup = Assert.IsType<System.Windows.Controls.Primitives.Popup>(combo.Template.FindName("PART_Popup", combo));
                var popupGrid = Assert.IsType<Grid>(Assert.IsType<Border>(popup.Child).Child);
                var listScroller = Assert.IsType<ScrollViewer>(popupGrid.Children[1]);
                Assert.True(listScroller.ScrollableHeight > 0);
                Assert.Equal(0, listScroller.VerticalOffset, precision: 1);
            }
            finally
            {
                host.Close();
            }
        });
    }

    [Fact]
    public async Task ProgrammaticDialogsExposeLabelsAndNamedCloseActions()
    {
        await RunStaAsync(() =>
        {
            var runtimeFields = typeof(LocalLlmConsole.RuntimeCustomRepositoryDialogFactory).GetMethod(
                "Fields",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(runtimeFields);
            FrameworkElement[] runtimeEditors = [new TextBox(), new TextBox(), new TextBox(), new ComboBox()];
            var runtimeGrid = Assert.IsType<Grid>(runtimeFields.Invoke(null, [runtimeEditors]));
            var runtimeLabels = runtimeGrid.Children.OfType<Label>().ToArray();
            Assert.Equal(runtimeEditors.Length, runtimeLabels.Length);
            for (var index = 0; index < runtimeEditors.Length; index++)
            {
                Assert.Same(runtimeEditors[index], runtimeLabels[index].Target);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(runtimeEditors[index])));
            }

            var groupFields = typeof(LocalLlmConsole.ModelGroupDialogFactory).GetMethod(
                "EditorFields",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(groupFields);
            var groupName = new TextBox();
            var groupPolicy = new ComboBox();
            var groupGrid = Assert.IsType<Grid>(groupFields.Invoke(null,
                [new (string Label, FrameworkElement Control)[] { ("Name", groupName), ("Policy", groupPolicy) }]));
            Assert.Contains(groupGrid.Children.OfType<Label>(), label => ReferenceEquals(label.Target, groupName));
            Assert.Contains(groupGrid.Children.OfType<Label>(), label => ReferenceEquals(label.Target, groupPolicy));

            AssertNamedCloseButton(typeof(LocalLlmConsole.OverviewDashboardCardTitleDialog), "Header", [new Window()]);
            AssertNamedCloseButton(typeof(LocalLlmConsole.OverviewDashboardMetricPicker), "DialogHeader", [new Window(), "Metrics"]);
        });

        static void AssertNamedCloseButton(Type type, string methodName, object[] arguments)
        {
            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var header = Assert.IsType<Grid>(method.Invoke(null, arguments));
            var close = Assert.Single(header.Children.OfType<Button>());
            Assert.Equal(LocalLlmConsole.Localization.Loc.T("Accessibility.CloseDialog"), AutomationProperties.GetName(close));
            Assert.False(string.IsNullOrWhiteSpace(close.ToolTip?.ToString()));
        }
    }

    [Fact]
    public async Task RuntimeFiltersAndActionsRenderIndependently()
    {
        await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-runtime-smoke"));
            var viewModel = new MainWindowViewModel();
            var now = DateTimeOffset.UtcNow;
            var cudaRuntime = new RuntimeRecord("runtime-cuda", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, typeof(RuntimeRecord).Assembly.Location, "{}", now);
            var vulkanRuntime = new RuntimeRecord("runtime-vulkan", "Vulkan", RuntimeMode.Wsl, RuntimeBackend.Vulkan, "vulkan", "{}", now);
            viewModel.Runtimes.ReplaceRows([
                new RuntimeCatalogRow { Name = "Vulkan", Backend = "Vulkan WSL", State = "Built", Location = "vulkan", Details = "Vulkan runtime details", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux, Runtime = vulkanRuntime },
                new RuntimeCatalogRow { Name = "CUDA", Backend = "CUDA Windows", State = "Built", Location = "cuda", Details = "CUDA runtime details", Vendor = RuntimeInventoryFilterService.Nvidia, Platform = RuntimeInventoryFilterService.Windows, Runtime = cudaRuntime }
            ], new HashSet<string>(StringComparer.OrdinalIgnoreCase) { cudaRuntime.Id });
            Assert.Equal(["CUDA", "Vulkan"], viewModel.Runtimes.Rows.Select(row => row.Name).ToArray());
            viewModel.RuntimePackages.ReplaceRows([
                new RuntimePackagePresetRow { Label = "CUDA", Vendor = RuntimeInventoryFilterService.Nvidia, Platform = RuntimeInventoryFilterService.Windows, BuildSourceAction = "Check", CanBuildSource = true, CheckAction = "Check", CanCheck = true },
                new RuntimePackagePresetRow { Label = "Vulkan", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux, BuildSourceAction = "Download", CanBuildSource = true },
                new RuntimePackagePresetRow { Label = "Add custom source repository", Vendor = RuntimeInventoryFilterService.All, Platform = RuntimeInventoryFilterService.All, BuildSourceAction = "Add", CanBuildSource = true }
            ]);
            var noOp = new RoutedEventHandler((_, _) => { });
            var toggledFavoriteId = "";
            var defaultRuntimeId = "";
            var controls = LocalLlmConsole.RuntimesPageFactory.Create(new LocalLlmConsole.RuntimesPageRequest(
                viewModel, settings.RuntimeRoot, settings.CudaPackagePreference,
                new LocalLlmConsole.RuntimesPageActions(
                    () => Task.CompletedTask, () => Task.CompletedTask,
                    runtime => { toggledFavoriteId = runtime.Id; return Task.CompletedTask; },
                    runtime =>
                    {
                        defaultRuntimeId = defaultRuntimeId == runtime.Id ? "" : runtime.Id;
                        viewModel.Runtimes.ReplaceRows(viewModel.Runtimes.Rows.ToArray(), new HashSet<string> { cudaRuntime.Id }, defaultRuntimeId);
                        return Task.CompletedTask;
                    }, noOp, noOp, noOp,
                    noOp, noOp, noOp, LocalLlmConsole.MainWindow.SetRuntimeGridColumnSizing, _ => { })));
            Assert.Equal(Visibility.Collapsed, controls.RuntimeSearch.Input.Visibility);
            controls.RuntimeSearch.Toggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, controls.RuntimeSearch.Input.Visibility);
            controls.RuntimeSearch.Input.Text = "CUDA";
            Assert.Equal("CUDA", Assert.Single(controls.RuntimeGrid.Items.Cast<RuntimeCatalogRow>()).Name);
            controls.RuntimeSearch.Input.Text = "";
            var combos = VisualDescendants<ComboBox>(controls.Root).ToDictionary(combo => combo.Name, StringComparer.Ordinal);
            combos["InstalledRuntimeTypeFilter"].SelectedItem = "AMD";
            combos["InstalledRuntimePlatformFilter"].SelectedItem = "Linux";
            Assert.Equal("Vulkan", Assert.Single(viewModel.Runtimes.Rows).Name);
            combos["RuntimeDownloadTypeFilter"].SelectedItem = "AMD";
            combos["RuntimeDownloadPlatformFilter"].SelectedItem = "Linux";
            Assert.Equal(["Vulkan", "Add custom source repository"], viewModel.RuntimePackages.Rows.Select(row => row.Label).ToArray());
            controls.Root.Measure(new Size(1024, 680));
            controls.Root.Arrange(new Rect(0, 0, 1024, 680));
            controls.Root.UpdateLayout();
            combos["InstalledRuntimeTypeFilter"].SelectedItem = "All";
            combos["InstalledRuntimePlatformFilter"].SelectedItem = "All";
            controls.Root.UpdateLayout();
            controls.RuntimeGrid.SelectedItem = viewModel.Runtimes.Rows[0];
            controls.RuntimeGrid.ScrollIntoView(viewModel.Runtimes.Rows[0]);
            controls.RuntimeGrid.UpdateLayout();
            var runtimeRow = Assert.IsType<DataGridRow>(controls.RuntimeGrid.ItemContainerGenerator.ContainerFromItem(viewModel.Runtimes.Rows[0]));
            Assert.Equal(Visibility.Collapsed, runtimeRow.DetailsVisibility);
            var detailsButton = VisualDescendants<Button>(runtimeRow).Single(button => Equals(button.Content, "⋮"));
            var favoriteButton = VisualDescendants<Button>(runtimeRow).Single(button => Equals(button.Content, "★"));
            Assert.Equal("Remove from favorites", favoriteButton.ToolTip);
            favoriteButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(cudaRuntime.Id, toggledFavoriteId);
            Assert.Equal(20, detailsButton.ActualWidth, precision: 1);
            Assert.Equal(detailsButton.ActualWidth, detailsButton.ActualHeight, precision: 1);
            Assert.Equal(detailsButton.ActualWidth, favoriteButton.ActualWidth, precision: 1);
            Assert.Equal(detailsButton.VerticalAlignment, favoriteButton.VerticalAlignment);
            AssertVerticallyCentered(detailsButton, runtimeRow);
            AssertVerticallyCentered(favoriteButton, runtimeRow);
            var favoriteCell = Assert.IsType<DataGridCell>(LocalLlmConsole.VisualTreeTraversal.FindAncestor<DataGridCell>(favoriteButton));
            Assert.Equal(0, Assert.IsType<System.Windows.Media.SolidColorBrush>(favoriteCell.Background).Color.A);
            Assert.Same(System.Windows.Application.Current.TryFindResource("Accent"), favoriteButton.Foreground);
            Assert.Equal(DataGridLengthUnitType.Pixel, controls.RuntimeGrid.Columns[1].Width.UnitType);
            Assert.Equal(DataGridLengthUnitType.Star, controls.RuntimeGrid.Columns[2].Width.UnitType);
            Assert.Equal(DataGridLengthUnitType.Pixel, controls.RuntimeGrid.Columns[4].Width.UnitType);
            Assert.Equal(48, Assert.IsType<LocalLlmConsole.FlexibleTextDataGridColumn>(controls.RuntimeGrid.Columns[2]).MinWidth);
            Assert.Equal(48, Assert.IsType<LocalLlmConsole.FlexibleActionDataGridColumn>(controls.RuntimeGrid.Columns[6]).MinWidth);
            Assert.Equal(36, Assert.IsType<LocalLlmConsole.ResponsiveActionDataGridColumn>(controls.RuntimeGrid.Columns[^1]).MinWidth);
            Assert.Equal(36, Assert.IsType<LocalLlmConsole.ResponsiveActionDataGridColumn>(controls.RuntimePackageGrid.Columns[^1]).MinWidth);
            controls.RuntimeGrid.Columns[^1].Width = new DataGridLength(36);
            controls.RuntimeGrid.UpdateLayout();
            var deleteButton = Assert.Single(VisualDescendants<LocalLlmConsole.ResponsiveActionButton>(runtimeRow), button => button.CompactLabel == "×");
            Assert.Contains(VisualDescendants<LocalLlmConsole.ResponsiveActionButton>(runtimeRow), button => button.CompactLabel == "\uE73E");
            var packageButtons = VisualDescendants<LocalLlmConsole.ResponsiveActionButton>(controls.RuntimePackageGrid).ToArray();
            Assert.Contains(packageButtons, button => button.CompactLabel == "\uE896");
            Assert.Contains(packageButtons, button => button.CompactLabel == "\uE72C");
            Assert.Equal("×", deleteButton.Content);
            controls.RuntimeGrid.Columns[^1].Width = new DataGridLength(100);
            controls.RuntimeGrid.UpdateLayout();
            Assert.Equal("Delete", deleteButton.Content);
            detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Visible, runtimeRow.DetailsVisibility);
            Assert.Equal("⋮", detailsButton.Content);
            detailsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, runtimeRow.DetailsVisibility);
            controls.RuntimeGrid.ContextMenu!.IsOpen = true;
            Assert.Equal(Visibility.Collapsed, runtimeRow.DetailsVisibility);
            var favorite = controls.RuntimeGrid.ContextMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Remove from favorites"));
            favorite.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Assert.Equal(cudaRuntime.Id, toggledFavoriteId);
            controls.RuntimeGrid.ContextMenu.IsOpen = false;
            controls.RuntimeGrid.SelectedItem = viewModel.Runtimes.Rows[0];
            var runtimeMenu = OpenContextMenu(controls.RuntimeGrid);
            var setDefault = runtimeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Set as default runtime"));
            Assert.True(setDefault.IsEnabled);
            setDefault.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            runtimeMenu.IsOpen = false;
            Assert.Equal(cudaRuntime.Id, defaultRuntimeId);
            controls.RuntimeGrid.UpdateLayout();
            controls.RuntimeGrid.SelectedItem = viewModel.Runtimes.Rows[0];
            var highlightedRow = Assert.IsType<DataGridRow>(controls.RuntimeGrid.ItemContainerGenerator.ContainerFromItem(viewModel.Runtimes.Rows[0]));
            Assert.Equal(FontWeights.SemiBold, highlightedRow.FontWeight);
            Assert.Equal(3, highlightedRow.BorderThickness.Left);
            Assert.Equal("Default runtime for new profiles", highlightedRow.ToolTip);
            runtimeMenu = OpenContextMenu(controls.RuntimeGrid);
            runtimeMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            runtimeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Clear default runtime"))
                .RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            runtimeMenu.IsOpen = false;
            Assert.Equal("", defaultRuntimeId);
            controls.RuntimeGrid.SelectedItem = viewModel.Runtimes.Rows[1];
            runtimeMenu = OpenContextMenu(controls.RuntimeGrid);
            runtimeMenu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Assert.False(runtimeMenu.Items.OfType<MenuItem>().Single(item => Equals(item.Header, "Set as default runtime")).IsEnabled);
            runtimeMenu.IsOpen = false;
            Assert.True(VisualDescendants<LocalLlmConsole.ResponsiveActionButton>(controls.RuntimePackageGrid).Count(button => button.FullLabel == "Check") >= 2);
        });
    }

    [Fact]
    public async Task MinimumWindowLogsAndUpdatesKeepEveryActionReachable()
    {
        await RunStaAsync(() =>
        {
            var noOp = new RoutedEventHandler((_, _) => { });
            var logs = LocalLlmConsole.LogsPageFactory.Create(new LocalLlmConsole.LogsPageRequest(
                Array.Empty<LogFileRow>(),
                new LocalLlmConsole.LogsPageActions(
                    noOp, noOp, noOp, noOp, noOp, noOp, noOp, noOp, (_, _) => { }),
                text => text));
            logs.Content.Measure(new Size(624, 504));
            logs.Content.Arrange(new Rect(0, 0, 624, 504));
            logs.Content.UpdateLayout();
            var logButtonLabels = new HashSet<string>(StringComparer.Ordinal)
            {
                LocalLlmConsole.Localization.Loc.T("Logs.RefreshButton"),
                LocalLlmConsole.Localization.Loc.T("Logs.OpenSelectedButton"),
                LocalLlmConsole.Localization.Loc.T("Logs.OpenFolderButton"),
                LocalLlmConsole.Localization.Loc.T("Logs.CreateDiagnosticsButton"),
                LocalLlmConsole.Localization.Loc.T("Logs.DeleteSelectedButton"),
                LocalLlmConsole.Localization.Loc.T("Logs.DeleteAllButton")
            };
            var logButtons = VisualDescendants<Button>(logs.Content)
                .Where(button => button.Content is string label && logButtonLabels.Contains(label))
                .ToArray();
            Assert.Equal(6, logButtons.Length);
            Assert.All(logButtons, button =>
            {
                var point = button.TranslatePoint(new Point(0, 0), logs.Content);
                Assert.InRange(point.X, 0, logs.Content.ActualWidth - button.ActualWidth + 1);
            });
            Assert.True(logButtons.Select(button => button.TranslatePoint(new Point(0, 0), logs.Content).Y)
                .DistinctBy(y => Math.Round(y))
                .Count() > 1);

            var updatesViewModel = new UpdatesPageViewModel();
            updatesViewModel.SetLatestUpdate(new AppUpdateInfo(
                true,
                "v2.5.0",
                "v2.6.0",
                "Release v2.6.0",
                string.Join(' ', Enumerable.Repeat("Detailed release note", 120)),
                "https://example.test/releases/v2.6.0",
                "manager.zip",
                "https://example.test/manager.zip",
                123));
            var updates = LocalLlmConsole.UpdatesPageFactory.Create(new LocalLlmConsole.UpdatesPageRequest(
                updatesViewModel,
                new LocalLlmConsole.UpdatesPageActions(() => Task.CompletedTask, () => { })));
            updates.Content.Measure(new Size(624, 504));
            updates.Content.Arrange(new Rect(0, 0, 624, 504));
            updates.Content.UpdateLayout();
            Assert.Equal(ScrollBarVisibility.Auto, updates.Content.VerticalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, updates.Content.HorizontalScrollBarVisibility);
            Assert.True(updates.Content.ExtentHeight > updates.Content.ViewportHeight);
            updates.Content.ScrollToEnd();
            updates.Content.UpdateLayout();
            Assert.True(updates.Content.VerticalOffset > 0);
        });
    }
}
