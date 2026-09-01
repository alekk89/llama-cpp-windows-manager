using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfLayoutPersistenceTests : WpfUiTestBase
{
    [Fact]
    public async Task UserResizedColumnsSplittersAndWindowBoundsSurviveRecreation()
    {
        await RunStaAsync(async () =>
        {
            var database = Path.Combine(TestWorkspace, $"layout-{Guid.NewGuid():N}.db");
            await using var store = new StateStore(database);
            await store.InitializeAsync();

            var first = BuildShell();
            var firstService = new UiLayoutPersistenceService(store);
            first.Window.Show();
            await firstService.AttachShellAsync(first.Window, first.Host, () => "Models");
            first.Grid.Columns[0].Width = new DataGridLength(315);
            first.Grid.Columns[1].Width = new DataGridLength(145);
            first.Grid.Columns[1].DisplayIndex = 0;
            first.Root.RowDefinitions[0].Height = new GridLength(245);
            first.Root.RowDefinitions[2].Height = new GridLength(355);
            first.Window.Width = 930;
            first.Window.Height = 710;
            await firstService.SaveShellAsync();
            first.Window.Close();

            var second = BuildShell();
            var secondService = new UiLayoutPersistenceService(store);
            second.Window.Show();
            await secondService.AttachShellAsync(second.Window, second.Host, () => "Models");

            Assert.Equal(315, second.Grid.Columns[0].Width.Value);
            Assert.Equal(145, second.Grid.Columns[1].Width.Value);
            Assert.Equal(1, second.Grid.Columns[0].DisplayIndex);
            Assert.Equal(0, second.Grid.Columns[1].DisplayIndex);
            Assert.Equal(245, second.Root.RowDefinitions[0].Height.Value);
            Assert.Equal(355, second.Root.RowDefinitions[2].Height.Value);
            Assert.InRange(second.Window.Width, 929, 931);
            Assert.InRange(second.Window.Height, 709, 711);
            second.Window.Close();
        });
    }

    [Fact]
    public async Task ColumnResizeStaysStablePersistsProportionsAndRefillsViewport()
    {
        await RunStaAsync(async () =>
        {
            var database = Path.Combine(TestWorkspace, $"compact-layout-{Guid.NewGuid():N}.db");
            await using var store = new StateStore(database);
            await store.InitializeAsync();

            var first = BuildShell(proportionalColumns: true);
            var firstService = new UiLayoutPersistenceService(store);
            first.Window.Show();
            await firstService.AttachShellAsync(first.Window, first.Host, () => "Models");
            first.Window.UpdateLayout();

            var compactColumn = first.Grid.Columns[1];
            var compactWidth = compactColumn.ActualWidth;
            var compactMaxWidth = compactColumn.MaxWidth;
            Assert.InRange(compactWidth, 48, 64);
            var nameColumnWidth = first.Grid.Columns[0].ActualWidth;
            var header = VisualDescendants<DataGridColumnHeader>(first.Grid)
                .Single(item => ReferenceEquals(item.Column, compactColumn));
            var gripper = VisualDescendants<Thumb>(header)
                .Single(item => item.Name == "PART_RightHeaderGripper");
            gripper.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Mouse.PreviewMouseDownEvent
            });

            Assert.True(first.Grid.Columns[0].Width.IsStar);
            Assert.True(compactColumn.Width.IsAbsolute);
            Assert.Equal(compactWidth, compactColumn.Width.Value, precision: 1);
            Assert.Equal(
                compactWidth + nameColumnWidth - first.Grid.Columns[0].MinWidth,
                compactColumn.MaxWidth,
                precision: 1);
            gripper.RaiseEvent(new DragCompletedEventArgs(0, 0, false)
            {
                RoutedEvent = Thumb.DragCompletedEvent
            });
            first.Window.UpdateLayout();

            Assert.All(first.Grid.Columns, column => Assert.True(column.Width.IsStar));
            Assert.Equal(compactMaxWidth, compactColumn.MaxWidth);
            Assert.Equal(compactWidth, compactColumn.ActualWidth, precision: 1);
            string? savedLayout = null;
            for (var attempt = 0; attempt < 20 && string.IsNullOrWhiteSpace(savedLayout); attempt++)
            {
                await Task.Delay(50);
                savedLayout = await store.GetUiLayoutStateAsync("page.models");
            }
            Assert.False(string.IsNullOrWhiteSpace(savedLayout));
            first.Window.Close();

            var second = BuildShell(proportionalColumns: true);
            var secondService = new UiLayoutPersistenceService(store);
            second.Window.Show();
            await secondService.AttachShellAsync(second.Window, second.Host, () => "Models");

            Assert.All(second.Grid.Columns, column => Assert.True(column.Width.IsStar));
            Assert.Equal(compactWidth, second.Grid.Columns[1].ActualWidth, precision: 1);

            second.Window.Width = 1000;
            await second.Window.Dispatcher.InvokeAsync(second.Window.UpdateLayout, DispatcherPriority.Loaded);
            Assert.True(second.Grid.Columns[1].ActualWidth > compactWidth);
            Assert.InRange(
                Math.Abs(second.Grid.Columns.Sum(column => column.ActualWidth) - second.Grid.ActualWidth),
                0,
                2);
            second.Window.Close();
        });
    }

    [Fact]
    public async Task PreviouslySavedPixelLayoutRestoresOriginallyFlexibleColumnsAsProportions()
    {
        await RunStaAsync(async () =>
        {
            var database = Path.Combine(TestWorkspace, $"pixel-layout-{Guid.NewGuid():N}.db");
            await using var store = new StateStore(database);
            await store.InitializeAsync();

            var first = BuildShell(proportionalColumns: true);
            var firstService = new UiLayoutPersistenceService(store);
            first.Window.Show();
            await firstService.AttachShellAsync(first.Window, first.Host, () => "Models");
            first.Window.UpdateLayout();
            var renderedWidths = first.Grid.Columns.Select(column => column.ActualWidth).ToArray();
            for (var index = 0; index < first.Grid.Columns.Count; index++)
                first.Grid.Columns[index].Width = new DataGridLength(renderedWidths[index]);
            await firstService.SaveShellAsync();
            first.Window.Close();

            var second = BuildShell(proportionalColumns: true);
            var secondService = new UiLayoutPersistenceService(store);
            second.Window.Show();
            await secondService.AttachShellAsync(second.Window, second.Host, () => "Models");

            Assert.All(second.Grid.Columns, column => Assert.True(column.Width.IsStar));
            Assert.Equal(renderedWidths[1], second.Grid.Columns[1].ActualWidth, precision: 1);
            second.Window.Width = 1000;
            await second.Window.Dispatcher.InvokeAsync(second.Window.UpdateLayout, DispatcherPriority.Loaded);
            Assert.True(second.Grid.Columns[1].ActualWidth > renderedWidths[1]);
            second.Window.Close();
        });
    }

    private static (Window Window, ContentControl Host, Grid Root, DataGrid Grid) BuildShell(
        bool proportionalColumns = false,
        double width = 800)
    {
        var dataGrid = new DataGrid { AutoGenerateColumns = false };
        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "Name",
            Width = proportionalColumns ? new DataGridLength(15, DataGridLengthUnitType.Star) : new DataGridLength(100)
        });
        dataGrid.Columns.Add(new DataGridTextColumn
        {
            Header = "State",
            MinWidth = proportionalColumns ? 48 : 20,
            Width = proportionalColumns ? new DataGridLength(1, DataGridLengthUnitType.Star) : new DataGridLength(200)
        });
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(100) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(dataGrid);
        var splitter = PageSectionFactory.HorizontalGridSplitter(1);
        root.Children.Add(splitter);
        var lower = new Border();
        Grid.SetRow(lower, 2);
        root.Children.Add(lower);
        var host = new ContentControl { Content = root };
        var window = new Window
        {
            Content = host,
            Width = width,
            Height = 600,
            MinWidth = 400,
            MinHeight = 300,
            ShowInTaskbar = false
        };
        return (window, host, root, dataGrid);
    }
}
