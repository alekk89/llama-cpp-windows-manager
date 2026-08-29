using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfIsolatedSurfaceTests : WpfUiTestBase
{
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
            viewModel.Runtimes.ReplaceRows([
                new RuntimeCatalogRow { Name = "CUDA", Backend = "CUDA Windows", State = "Built", Location = "cuda", Details = "", Vendor = RuntimeInventoryFilterService.Nvidia, Platform = RuntimeInventoryFilterService.Windows },
                new RuntimeCatalogRow { Name = "Vulkan", Backend = "Vulkan WSL", State = "Built", Location = "vulkan", Details = "", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux }
            ]);
            viewModel.RuntimePackages.ReplaceRows([
                new RuntimePackagePresetRow { Label = "CUDA", Vendor = RuntimeInventoryFilterService.Nvidia, Platform = RuntimeInventoryFilterService.Windows, BuildSourceAction = "Check", CanBuildSource = true, CheckAction = "Check", CanCheck = true },
                new RuntimePackagePresetRow { Label = "Vulkan", Vendor = RuntimeInventoryFilterService.Amd, Platform = RuntimeInventoryFilterService.Linux, BuildSourceAction = "Download", CanBuildSource = true },
                new RuntimePackagePresetRow { Label = "Add custom source repository", Vendor = RuntimeInventoryFilterService.All, Platform = RuntimeInventoryFilterService.All, BuildSourceAction = "Add", CanBuildSource = true }
            ]);
            var noOp = new RoutedEventHandler((_, _) => { });
            var controls = LocalLlmConsole.RuntimesPageFactory.Create(new LocalLlmConsole.RuntimesPageRequest(
                viewModel, settings.RuntimeRoot, settings.CudaPackagePreference,
                new LocalLlmConsole.RuntimesPageActions(
                    () => Task.CompletedTask, () => Task.CompletedTask, (_, _) => { }, noOp, noOp, noOp,
                    noOp, noOp, noOp, _ => { }, _ => { })));
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
            Assert.True(VisualDescendants<Button>(controls.RuntimePackageGrid).Count(button => Equals(button.Content, "Check")) >= 2);
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
