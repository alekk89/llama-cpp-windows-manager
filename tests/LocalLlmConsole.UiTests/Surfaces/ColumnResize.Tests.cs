using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfColumnResizeTests : WpfUiTestBase
{
    [Theory]
    [InlineData("Models", true)]
    [InlineData("Models", false)]
    [InlineData("Runtimes", true)]
    [InlineData("Runtimes", false)]
    [InlineData("Packages", true)]
    [InlineData("Packages", false)]
    public async Task DeleteExpandsLeftByMovingInterveningColumnsIntoName(string page, bool useLeftGripper)
    {
        await RunStaAsync(async () =>
        {
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"left-resize-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();
            var (root, grid) = BuildPage(page);
            var host = new ContentControl { Content = root };
            var window = new Window { Content = host, Width = 1600, Height = 800, ShowInTaskbar = false };
            window.Show();
            try
            {
                await new UiLayoutPersistenceService(store).AttachShellAsync(window, host, () => page);
                await SettleAsync(window);
                var name = grid.Columns.OfType<DataGridTextColumn>().First();
                var delete = grid.Columns[^1];
                var preceding = grid.Columns[^2];
                DragColumn(grid, preceding, preceding.MinWidth - preceding.ActualWidth);
                await SettleAsync(window);
                DragColumn(grid, delete, delete.MinWidth - delete.ActualWidth);
                await SettleAsync(window);
                var widths = grid.Columns.ToDictionary(column => column, column => column.ActualWidth);
                var deleteLeft = ColumnLeft(grid, delete);
                DragBoundary(grid, useLeftGripper ? delete : preceding, useLeftGripper, -40);
                await SettleAsync(window);
                Assert.Equal(widths[delete] + 40, delete.ActualWidth, precision: 1);
                Assert.Equal(widths[name] - 40, name.ActualWidth, precision: 1);
                Assert.Equal(deleteLeft - 40, ColumnLeft(grid, delete), precision: 1);
                foreach (var column in grid.Columns.Where(column => column != name && column != delete))
                    Assert.Equal(widths[column], column.ActualWidth, precision: 1);
                DragBoundary(grid, useLeftGripper ? delete : preceding, useLeftGripper, 40);
                await SettleAsync(window);
                foreach (var column in grid.Columns)
                    Assert.Equal(widths[column], column.ActualWidth, precision: 1);
                DragBoundary(grid, useLeftGripper ? delete : preceding, useLeftGripper, -40);
                await SettleAsync(window);
                window.Width += 200;
                await SettleAsync(window);
                Assert.Equal(widths[delete] + 40, delete.ActualWidth, precision: 1);
            }
            finally { window.Close(); }
        });
    }

    private static double ColumnLeft(DataGrid grid, DataGridColumn column)
        => grid.Columns.Where(item => item.Visibility == Visibility.Visible && item.DisplayIndex < column.DisplayIndex)
            .Sum(item => item.ActualWidth);

    [Theory]
    [InlineData("Models")]
    [InlineData("Runtimes")]
    [InlineData("Packages")]
    public async Task LeftResizeRespectsMinimumWidthsAndCancellation(string page)
    {
        await RunStaAsync(async () =>
        {
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"resize-limits-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();
            var (root, grid) = BuildPage(page);
            var host = new ContentControl { Content = root };
            var window = new Window { Content = host, Width = 1600, Height = 800, ShowInTaskbar = false };
            window.Show();
            try
            {
                await new UiLayoutPersistenceService(store).AttachShellAsync(window, host, () => page);
                await SettleAsync(window);
                var name = grid.Columns.OfType<DataGridTextColumn>().First();
                var delete = grid.Columns[^1];
                var widths = grid.Columns.ToDictionary(column => column, column => column.ActualWidth);
                DragBoundary(grid, delete, true, -10000, canceled: true);
                await SettleAsync(window);
                foreach (var column in grid.Columns)
                    Assert.Equal(widths[column], column.ActualWidth, precision: 1);
                DragBoundarySteps(grid, delete, true, [-10000, 20]);
                await SettleAsync(window);
                Assert.Equal(name.MinWidth + 20, name.ActualWidth, precision: 1);
                Assert.Equal(widths[delete] + widths[name] - name.MinWidth - 20, delete.ActualWidth, precision: 1);
                DragBoundary(grid, delete, true, 10000);
                await SettleAsync(window);
                Assert.Equal(delete.MinWidth, delete.ActualWidth, precision: 1);
                foreach (var column in grid.Columns.Where(column => column != name && column != delete))
                    Assert.Equal(widths[column], column.ActualWidth, precision: 1);
                Assert.True(name.Width.IsAbsolute);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("Models")]
    [InlineData("Runtimes")]
    [InlineData("Packages")]
    public async Task EveryResizableColumnCanGrowAndShrinkAfterLayoutRestore(string page)
    {
        await RunStaAsync(async () =>
        {
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"manual-resize-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();
            var (root, grid) = BuildPage(page);
            var host = new ContentControl { Content = root };
            var window = new Window { Content = host, Width = 1600, Height = 800, ShowInTaskbar = false };
            window.Show();
            try
            {
                var service = new UiLayoutPersistenceService(store);
                await service.AttachShellAsync(window, host, () => page);
                await service.SaveShellAsync();
                window.Close();
                (root, grid) = BuildPage(page);
                host = new ContentControl { Content = root };
                window = new Window { Content = host, Width = 1200, Height = 800, ShowInTaskbar = false };
                window.Show();
                await new UiLayoutPersistenceService(store).AttachShellAsync(window, host, () => page);
                await SettleAsync(window);
                foreach (var column in grid.Columns.Where(column => column.CanUserResize && column.MaxWidth > column.MinWidth))
                {
                    var original = column.ActualWidth;
                    DragColumn(grid, column, 30);
                    await SettleAsync(window);
                    Assert.Equal(original + 30, column.ActualWidth, precision: 1);
                    DragColumn(grid, column, -30);
                    await SettleAsync(window);
                    Assert.Equal(original, column.ActualWidth, precision: 1);
                }
                var delete = grid.Columns[^1];
                DragColumn(grid, delete, delete.MinWidth - delete.ActualWidth);
                await SettleAsync(window);
                DragColumn(grid, delete, 30);
                await SettleAsync(window);
                Assert.Equal(delete.MinWidth + 30, delete.ActualWidth, precision: 1);
                DragBoundary(grid, delete, false, 20);
                await SettleAsync(window);
                Assert.Equal(delete.MinWidth + 50, delete.ActualWidth, precision: 1);
                window.Width += 200;
                await SettleAsync(window);
                Assert.Equal(delete.MinWidth + 50, delete.ActualWidth, precision: 1);
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("Models", false)]
    [InlineData("Runtimes", false)]
    [InlineData("Packages", false)]
    [InlineData("Models", true)]
    [InlineData("Runtimes", true)]
    [InlineData("Packages", true)]
    public async Task DraggingActionColumnsThenGrowingWindowOnlyExpandsName(string page, bool resizeNeighbor)
    {
        await RunStaAsync(async () =>
        {
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"resize-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();
            var (root, grid) = BuildPage(page);
            var host = new ContentControl { Content = root };
            var window = new Window { Content = host, Width = 1200, Height = 800, ShowInTaskbar = false };
            var service = new UiLayoutPersistenceService(store);
            window.Show();
            try
            {
                await service.AttachShellAsync(window, host, () => page);
                await SettleAsync(window);
                if (resizeNeighbor)
                {
                    DragColumn(grid, grid.Columns[^3], 1000);
                    await SettleAsync(window);
                }
                else
                {
                    foreach (var column in grid.Columns.TakeLast(2))
                    {
                        DragColumn(grid, column, column.MinWidth - column.ActualWidth);
                        await SettleAsync(window);
                        Assert.Equal(column.MinWidth, column.ActualWidth, precision: 1);
                    }
                }

                var name = grid.Columns.OfType<DataGridTextColumn>().First();
                var widths = grid.Columns.ToDictionary(column => column, column => column.ActualWidth);
                window.Width = 1600;
                await SettleAsync(window);
                Assert.True(name.ActualWidth > widths[name]);
                foreach (var column in grid.Columns.Where(column => !ReferenceEquals(column, name)))
                    Assert.Equal(widths[column], column.ActualWidth, precision: 1);
                Assert.All(grid.Columns, column => Assert.True(column.Width.IsAbsolute));

                window.Width = 1000;
                await SettleAsync(window);
                var narrowed = grid.Columns.Select(column => column.ActualWidth).ToArray();
                foreach (var column in grid.Columns.Where(column => !ReferenceEquals(column, name)))
                    Assert.Equal(widths[column], column.ActualWidth, precision: 1);
                window.Width = 1600;
                await SettleAsync(window);
                foreach (var column in grid.Columns.Where(column => !ReferenceEquals(column, name)))
                    Assert.Equal(narrowed[grid.Columns.IndexOf(column)], column.ActualWidth, precision: 1);

                await service.SaveShellAsync();
                window.Close();
                var restored = BuildPage(page);
                host = new ContentControl { Content = restored.Root };
                window = new Window { Content = host, Width = 1200, Height = 800, ShowInTaskbar = false };
                window.Show();
                await new UiLayoutPersistenceService(store).AttachShellAsync(window, host, () => page);
                await SettleAsync(window);
                foreach (var column in grid.Columns.Where(column => !ReferenceEquals(column, name)))
                    Assert.Equal(column.ActualWidth, restored.Grid.Columns[grid.Columns.IndexOf(column)].ActualWidth, precision: 1);
            }
            finally { window.Close(); }
        });
    }

    private static void DragColumn(DataGrid grid, DataGridColumn column, double delta)
    {
        var fromLeft = column != grid.Columns.OfType<DataGridTextColumn>().First();
        DragBoundary(grid, column, fromLeft, fromLeft ? -delta : delta);
    }

    private static void DragBoundary(DataGrid grid, DataGridColumn column, bool fromLeft, double delta, bool canceled = false)
        => DragBoundarySteps(grid, column, fromLeft, Enumerable.Repeat(delta / 4, 4), canceled);

    private static void DragBoundarySteps(DataGrid grid, DataGridColumn column, bool fromLeft, IEnumerable<double> changes, bool canceled = false)
    {
        var header = VisualDescendants<DataGridColumnHeader>(grid).Single(item => ReferenceEquals(item.Column, column));
        var gripper = VisualDescendants<Thumb>(header).Single(item => item.Name == (fromLeft ? "PART_LeftHeaderGripper" : "PART_RightHeaderGripper"));
        // Real mouse input hits the thumb's template, then WPF raises the drag events.
        var hit = VisualDescendants<Border>(gripper).First();
        hit.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent
        });
        gripper.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
        foreach (var change in changes)
            gripper.RaiseEvent(new DragDeltaEventArgs(change, 0) { RoutedEvent = Thumb.DragDeltaEvent });
        gripper.RaiseEvent(new DragCompletedEventArgs(0, 0, canceled) { RoutedEvent = Thumb.DragCompletedEvent });
    }

    private static async Task SettleAsync(Window window)
        => await window.Dispatcher.InvokeAsync(window.UpdateLayout, DispatcherPriority.ContextIdle);

    private static (FrameworkElement Root, DataGrid Grid) BuildPage(string page)
    {
        var viewModel = new MainWindowViewModel();
        viewModel.Models.Rows.Add(new ModelGridRow { Model = new ModelRecord("example", "Example model", "example.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow), Name = "Example model", Quant = "Q4_K_M", Size = "8 GB", OpenFolderAction = "Open", DeleteAction = "Delete" });
        viewModel.Runtimes.ReplaceRows([new RuntimeCatalogRow { Name = "Example runtime", Backend = "CUDA", State = "Built", Location = "D:\\runtimes\\example", Details = "Example" }]);
        viewModel.RuntimePackages.ReplaceRows([new RuntimePackagePresetRow { Label = "Example runtime", Backend = "CUDA", LocalStatus = "Installed", BuildSourceAction = "Build", InstallAction = "Install", CheckAction = "Check", DeleteAction = "Delete" }]);
        var noOp = new RoutedEventHandler((_, _) => { });
        if (page == "Models")
        {
            var controls = ModelsPageFactory.Create(new ModelsPageRequest(viewModel, TestWorkspace, new Border(),
                new ModelsPageActions(
                    () => Task.CompletedTask, () => Task.CompletedTask, () => Task.CompletedTask, () => { },
                    () => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
                    _ => Task.CompletedTask, (_, _) => Task.CompletedTask, (_, _) => Task.CompletedTask,
                    (_, _) => Task.CompletedTask, () => { }, (_, _) => { }, noOp, noOp,
                    () => Task.CompletedTask, () => Task.CompletedTask,
                    grid => typeof(MainWindow).GetMethod("SetModelGridColumnSizing", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [grid]))));
            return (controls.Root, controls.ModelsGrid);
        }

        var runtimes = RuntimesPageFactory.Create(new RuntimesPageRequest(viewModel, TestWorkspace, "stable",
            new RuntimesPageActions(
                () => Task.CompletedTask, () => Task.CompletedTask, _ => Task.CompletedTask, _ => Task.CompletedTask,
                noOp, noOp, noOp, noOp, noOp, noOp, MainWindow.SetRuntimeGridColumnSizing,
                grid => typeof(MainWindow).GetMethod("SetRuntimeBuildGridColumnSizing", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, [grid]))));
        return (runtimes.Root, page == "Packages" ? runtimes.RuntimePackageGrid : runtimes.RuntimeGrid);
    }
}
