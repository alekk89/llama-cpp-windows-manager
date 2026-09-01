using System.ComponentModel;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LocalLlmConsole.Services;

public sealed class UiLayoutPersistenceService
{
    private const int LayoutVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly StateStore _stateStore;
    private ShellAttachment? _shell;

    public UiLayoutPersistenceService(StateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public async Task AttachShellAsync(Window window, ContentControl pageHost, Func<string> currentPage)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(pageHost);
        ArgumentNullException.ThrowIfNull(currentPage);
        if (_shell is not null) return;

        var attachment = new ShellAttachment(this, window, pageHost, currentPage);
        _shell = attachment;
        await attachment.InitializeAsync();
    }

    public Task SaveShellAsync()
        => _shell?.SaveNowAsync() ?? Task.CompletedTask;

    private async Task<PageAttachment> RestorePageAsync(string page, FrameworkElement root)
    {
        await WaitUntilLoadedAsync(root);
        var scope = PageScope(page);
        var json = await _stateStore.GetUiLayoutStateAsync(scope);
        var attachment = new PageAttachment(this, scope, root);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                attachment.Apply(JsonSerializer.Deserialize<PageLayout>(json, JsonOptions));
            }
            catch (JsonException ex)
            {
                Trace.TraceWarning($"Ignoring invalid saved UI layout for {scope}: {ex.Message}");
            }
        }

        attachment.Observe();
        return attachment;
    }

    private Task SavePageAsync(string scope, FrameworkElement root)
        => _stateStore.SaveUiLayoutStateAsync(
            scope,
            JsonSerializer.Serialize(PageLayout.Capture(root), JsonOptions));

    private async Task RestoreWindowAsync(Window window)
    {
        var json = await _stateStore.GetUiLayoutStateAsync("window.main");
        if (string.IsNullOrWhiteSpace(json)) return;
        try
        {
            ApplyWindowLayout(window, JsonSerializer.Deserialize<WindowLayout>(json, JsonOptions));
        }
        catch (JsonException ex)
        {
            Trace.TraceWarning($"Ignoring invalid saved main-window layout: {ex.Message}");
        }
    }

    private Task SaveWindowAsync(Window window)
        => _stateStore.SaveUiLayoutStateAsync(
            "window.main",
            JsonSerializer.Serialize(WindowLayout.Capture(window), JsonOptions));

    private static async Task WaitUntilLoadedAsync(FrameworkElement root)
    {
        if (!root.IsLoaded)
        {
            var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            RoutedEventHandler? handler = null;
            handler = (_, _) =>
            {
                root.Loaded -= handler;
                loaded.TrySetResult();
            };
            root.Loaded += handler;
            await loaded.Task;
        }

        await root.Dispatcher.InvokeAsync(root.UpdateLayout, DispatcherPriority.Loaded);
    }

    private static string PageScope(string page)
    {
        var normalized = new string((page ?? "page")
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray()).Trim('-');
        return $"page.{(string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized)}";
    }

    private static void ApplyWindowLayout(Window window, WindowLayout? layout)
    {
        if (layout is null
            || !FinitePositive(layout.Width)
            || !FinitePositive(layout.Height)
            || !double.IsFinite(layout.Left)
            || !double.IsFinite(layout.Top))
            return;

        var virtualLeft = SystemParameters.VirtualScreenLeft;
        var virtualTop = SystemParameters.VirtualScreenTop;
        var virtualWidth = Math.Max(window.MinWidth, SystemParameters.VirtualScreenWidth);
        var virtualHeight = Math.Max(window.MinHeight, SystemParameters.VirtualScreenHeight);
        var width = Math.Clamp(layout.Width, window.MinWidth, virtualWidth);
        var height = Math.Clamp(layout.Height, window.MinHeight, virtualHeight);
        const double visibleEdge = 64;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Width = width;
        window.Height = height;
        window.Left = Math.Clamp(layout.Left, virtualLeft - width + visibleEdge, virtualLeft + virtualWidth - visibleEdge);
        window.Top = Math.Clamp(layout.Top, virtualTop, virtualTop + virtualHeight - visibleEdge);
        window.WindowState = layout.Maximized ? WindowState.Maximized : WindowState.Normal;
    }

    private static bool FinitePositive(double value) => double.IsFinite(value) && value > 0;

    private sealed class ShellAttachment
    {
        private readonly UiLayoutPersistenceService _owner;
        private readonly Window _window;
        private readonly ContentControl _pageHost;
        private readonly Func<string> _currentPage;
        private readonly DispatcherTimer _windowSaveTimer;
        private readonly SemaphoreSlim _pageSwitchGate = new(1, 1);
        private readonly DependencyPropertyDescriptor _contentDescriptor;
        private PageAttachment? _page;

        public ShellAttachment(
            UiLayoutPersistenceService owner,
            Window window,
            ContentControl pageHost,
            Func<string> currentPage)
        {
            _owner = owner;
            _window = window;
            _pageHost = pageHost;
            _currentPage = currentPage;
            _windowSaveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(400), DispatcherPriority.Background, SaveWindowTimerTick, window.Dispatcher)
            {
                IsEnabled = false
            };
            _contentDescriptor = DependencyPropertyDescriptor.FromProperty(ContentControl.ContentProperty, typeof(ContentControl));
        }

        public async Task InitializeAsync()
        {
            await _owner.RestoreWindowAsync(_window);
            _window.LocationChanged += WindowLayoutChanged;
            _window.SizeChanged += WindowLayoutChanged;
            _window.StateChanged += WindowLayoutChanged;
            _contentDescriptor.AddValueChanged(_pageHost, PageContentChanged);
            await SwitchPageAsync();
        }

        public async Task SaveNowAsync()
        {
            _windowSaveTimer.Stop();
            if (_page is not null)
                await _page.SaveNowAsync();
            await _owner.SaveWindowAsync(_window);
        }

        private void WindowLayoutChanged(object? sender, EventArgs args)
        {
            if (_window.WindowState == WindowState.Minimized) return;
            _windowSaveTimer.Stop();
            _windowSaveTimer.Start();
        }

        private async void SaveWindowTimerTick(object? sender, EventArgs args)
        {
            _windowSaveTimer.Stop();
            try { await _owner.SaveWindowAsync(_window); }
            catch (Exception ex) { Trace.TraceWarning($"Could not persist the main-window layout: {ex.Message}"); }
        }

        private void PageContentChanged(object? sender, EventArgs args)
            => _ = SwitchPageSafelyAsync();

        private async Task SwitchPageSafelyAsync()
        {
            try { await SwitchPageAsync(); }
            catch (Exception ex) { Trace.TraceWarning($"Could not restore the page layout: {ex.Message}"); }
        }

        private async Task SwitchPageAsync()
        {
            await _pageSwitchGate.WaitAsync();
            try
            {
                if (_page is not null)
                {
                    await _page.SaveNowAsync();
                    _page.Dispose();
                    _page = null;
                }

                if (_pageHost.Content is FrameworkElement root)
                    _page = await _owner.RestorePageAsync(_currentPage(), root);
            }
            finally
            {
                _pageSwitchGate.Release();
            }
        }
    }

    private sealed class PageAttachment : IDisposable
    {
        private readonly UiLayoutPersistenceService _owner;
        private readonly string _scope;
        private readonly FrameworkElement _root;
        private readonly DispatcherTimer _saveTimer;
        private readonly Dictionary<DataGrid, HashSet<DataGridColumn>> _proportionalColumns;
        private readonly List<(DataGridColumn Column, EventHandler Handler)> _columnHandlers = [];
        private readonly List<(DataGrid Grid, EventHandler<DataGridColumnEventArgs> Handler)> _displayHandlers = [];
        private readonly List<(DataGrid Grid, MouseButtonEventHandler Pressed, DragCompletedEventHandler Completed)> _columnResizeHandlers = [];
        private readonly Dictionary<DataGrid, (DataGridColumn Column, double MaxWidth)> _activeColumnResizes = [];
        private readonly List<(GridSplitter Splitter, DragCompletedEventHandler Handler)> _splitterHandlers = [];
        private bool _restoring;
        private bool _disposed;

        public PageAttachment(UiLayoutPersistenceService owner, string scope, FrameworkElement root)
        {
            _owner = owner;
            _scope = scope;
            _root = root;
            _proportionalColumns = Descendants<DataGrid>(root).ToDictionary(
                grid => grid,
                grid => grid.Columns.Where(column => column.Width.IsStar).ToHashSet());
            _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, SaveTimerTick, root.Dispatcher)
            {
                IsEnabled = false
            };
        }

        public void Apply(PageLayout? layout)
        {
            if (layout is not { Version: LayoutVersion }) return;
            _restoring = true;
            try
            {
                var grids = Descendants<DataGrid>(_root).ToArray();
                for (var index = 0; index < Math.Min(grids.Length, layout.DataGrids.Count); index++)
                    ApplyGrid(grids[index], layout.DataGrids[index]);

                var splitters = Descendants<GridSplitter>(_root).ToArray();
                for (var index = 0; index < Math.Min(splitters.Length, layout.Splitters.Count); index++)
                    ApplySplitter(splitters[index], layout.Splitters[index]);
                _root.UpdateLayout();
                foreach (var grid in grids)
                    RestoreProportionalColumns(grid);
                _root.UpdateLayout();
            }
            finally
            {
                _restoring = false;
            }
        }

        public void Observe()
        {
            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            foreach (var grid in Descendants<DataGrid>(_root))
            {
                foreach (var column in grid.Columns)
                {
                    EventHandler handler = (_, _) => ScheduleSave();
                    descriptor.AddValueChanged(column, handler);
                    _columnHandlers.Add((column, handler));
                }

                EventHandler<DataGridColumnEventArgs> displayHandler = (_, _) => ScheduleSave();
                grid.ColumnDisplayIndexChanged += displayHandler;
                _displayHandlers.Add((grid, displayHandler));

                MouseButtonEventHandler resizePressed = (_, args) => BeginColumnResize(grid, args);
                DragCompletedEventHandler resizeCompleted = (_, args) => CompleteColumnResize(grid, args);
                grid.AddHandler(Mouse.PreviewMouseDownEvent, resizePressed, handledEventsToo: true);
                grid.AddHandler(Thumb.DragCompletedEvent, resizeCompleted, handledEventsToo: true);
                _columnResizeHandlers.Add((grid, resizePressed, resizeCompleted));
            }

            foreach (var splitter in Descendants<GridSplitter>(_root))
            {
                DragCompletedEventHandler handler = (_, _) => ScheduleSave();
                splitter.DragCompleted += handler;
                _splitterHandlers.Add((splitter, handler));
            }
        }

        public async Task SaveNowAsync()
        {
            if (_disposed) return;
            _saveTimer.Stop();
            await _owner.SavePageAsync(_scope, _root);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _saveTimer.Stop();
            var descriptor = DependencyPropertyDescriptor.FromProperty(DataGridColumn.WidthProperty, typeof(DataGridColumn));
            foreach (var (column, handler) in _columnHandlers)
                descriptor.RemoveValueChanged(column, handler);
            foreach (var (grid, handler) in _displayHandlers)
                grid.ColumnDisplayIndexChanged -= handler;
            foreach (var (grid, pressed, completed) in _columnResizeHandlers)
            {
                grid.RemoveHandler(Mouse.PreviewMouseDownEvent, pressed);
                grid.RemoveHandler(Thumb.DragCompletedEvent, completed);
            }
            foreach (var (_, active) in _activeColumnResizes)
                active.Column.MaxWidth = active.MaxWidth;
            _activeColumnResizes.Clear();
            foreach (var (splitter, handler) in _splitterHandlers)
                splitter.DragCompleted -= handler;
        }

        private void BeginColumnResize(DataGrid grid, MouseButtonEventArgs args)
        {
            if (_restoring || _disposed || args.ChangedButton != MouseButton.Left
                || args.OriginalSource is not DependencyObject source
                || ResizedColumn(grid, source) is not { } resizedColumn
                || !FinitePositive(resizedColumn.ActualWidth))
                return;

            if (_activeColumnResizes.Remove(grid, out var previous))
                previous.Column.MaxWidth = previous.MaxWidth;

            var originalMaxWidth = resizedColumn.MaxWidth;
            var availableGrowth = _proportionalColumns.TryGetValue(grid, out var proportionalColumns)
                ? proportionalColumns
                    .Where(column => !ReferenceEquals(column, resizedColumn))
                    .Sum(column => Math.Max(0, column.ActualWidth - column.MinWidth))
                : 0;
            var viewportLimit = resizedColumn.ActualWidth + availableGrowth;
            if (double.IsFinite(viewportLimit) && viewportLimit >= resizedColumn.ActualWidth)
            {
                resizedColumn.MaxWidth = Math.Min(originalMaxWidth, viewportLimit);
                _activeColumnResizes[grid] = (resizedColumn, originalMaxWidth);
            }

            resizedColumn.Width = new DataGridLength(resizedColumn.ActualWidth, DataGridLengthUnitType.Pixel);
        }

        private void CompleteColumnResize(DataGrid grid, DragCompletedEventArgs args)
        {
            if (_restoring || _disposed || !_activeColumnResizes.Remove(grid, out var active))
                return;

            var renderedWidth = active.Column.ActualWidth;
            active.Column.MaxWidth = active.MaxWidth;
            if (!FinitePositive(renderedWidth)) return;

            active.Column.Width = new DataGridLength(renderedWidth, DataGridLengthUnitType.Pixel);
            RestoreProportionalColumns(grid);
            grid.UpdateLayout();
            ScheduleSave();
        }

        private static DataGridColumn? ResizedColumn(DataGrid grid, DependencyObject source)
        {
            if (source is not FrameworkElement { Name: "PART_LeftHeaderGripper" or "PART_RightHeaderGripper" } gripper
                || VisualTreeTraversal.FindAncestor<DataGridColumnHeader>(source) is not { Column: { } headerColumn } header)
                return null;

            return gripper.Name == "PART_LeftHeaderGripper"
                ? grid.Columns.FirstOrDefault(column => column.DisplayIndex == headerColumn.DisplayIndex - 1)
                : header.Column;
        }

        private void RestoreProportionalColumns(DataGrid grid)
        {
            if (_proportionalColumns.TryGetValue(grid, out var proportionalColumns))
            {
                var renderedWidths = grid.Columns.ToDictionary(column => column, column => column.ActualWidth);
                foreach (var column in proportionalColumns)
                {
                    if (renderedWidths.TryGetValue(column, out var width) && FinitePositive(width))
                        column.Width = new DataGridLength(width, DataGridLengthUnitType.Star);
                }
            }
        }

        private void ScheduleSave()
        {
            if (_restoring || _disposed) return;
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private async void SaveTimerTick(object? sender, EventArgs args)
        {
            _saveTimer.Stop();
            try { await SaveNowAsync(); }
            catch (Exception ex) { Trace.TraceWarning($"Could not persist {_scope} layout: {ex.Message}"); }
        }
    }

    private sealed record PageLayout(int Version, IReadOnlyList<DataGridLayout> DataGrids, IReadOnlyList<SplitterLayout> Splitters)
    {
        public static PageLayout Capture(FrameworkElement root)
            => new(
                LayoutVersion,
                Descendants<DataGrid>(root).Select(DataGridLayout.Capture).ToArray(),
                Descendants<GridSplitter>(root).Select(SplitterLayout.Capture).ToArray());
    }

    private sealed record DataGridLayout(IReadOnlyList<DataGridColumnLayout> Columns)
    {
        public static DataGridLayout Capture(DataGrid grid)
            => new(grid.Columns.Select((column, index) => DataGridColumnLayout.Capture(column, index)).ToArray());
    }

    private sealed record DataGridColumnLayout(int Index, int DisplayIndex, double Value, DataGridLengthUnitType Unit)
    {
        public static DataGridColumnLayout Capture(DataGridColumn column, int index)
            => new(index, column.DisplayIndex, column.Width.Value, column.Width.UnitType);
    }

    private sealed record SplitterLayout(
        GridResizeDirection Direction,
        int FirstIndex,
        GridLengthLayout First,
        int SecondIndex,
        GridLengthLayout Second)
    {
        public static SplitterLayout Capture(GridSplitter splitter)
        {
            var direction = splitter.ResizeDirection;
            var first = direction == GridResizeDirection.Columns ? Grid.GetColumn(splitter) - 1 : Grid.GetRow(splitter) - 1;
            var second = first + 2;
            if (VisualTreeTraversal.FindAncestor<Grid>(splitter) is not { } grid)
                return new(direction, -1, GridLengthLayout.Empty, -1, GridLengthLayout.Empty);
            return direction == GridResizeDirection.Columns
                ? new(direction, first, GridLengthLayout.Capture(grid.ColumnDefinitions, first), second, GridLengthLayout.Capture(grid.ColumnDefinitions, second))
                : new(direction, first, GridLengthLayout.Capture(grid.RowDefinitions, first), second, GridLengthLayout.Capture(grid.RowDefinitions, second));
        }
    }

    private sealed record GridLengthLayout(double Value, GridUnitType Unit)
    {
        public static GridLengthLayout Empty { get; } = new(0, GridUnitType.Pixel);

        public static GridLengthLayout Capture(IList<RowDefinition> definitions, int index)
            => index >= 0 && index < definitions.Count ? From(definitions[index].Height) : Empty;

        public static GridLengthLayout Capture(IList<ColumnDefinition> definitions, int index)
            => index >= 0 && index < definitions.Count ? From(definitions[index].Width) : Empty;

        private static GridLengthLayout From(GridLength length) => new(length.Value, length.GridUnitType);
    }

    private sealed record WindowLayout(double Left, double Top, double Width, double Height, bool Maximized)
    {
        public static WindowLayout Capture(Window window)
        {
            var bounds = window.WindowState == WindowState.Normal ? new Rect(window.Left, window.Top, window.Width, window.Height) : window.RestoreBounds;
            return new(bounds.Left, bounds.Top, bounds.Width, bounds.Height, window.WindowState == WindowState.Maximized);
        }
    }

    private static void ApplyGrid(DataGrid grid, DataGridLayout layout)
    {
        foreach (var saved in layout.Columns.Where(column => column.Index >= 0 && column.Index < grid.Columns.Count))
        {
            if (!double.IsFinite(saved.Value) || saved.Value < 0) continue;
            grid.Columns[saved.Index].Width = new DataGridLength(Math.Min(saved.Value, 100_000), saved.Unit);
        }

        foreach (var saved in layout.Columns
                     .Where(column => column.Index >= 0 && column.Index < grid.Columns.Count && column.DisplayIndex >= 0 && column.DisplayIndex < grid.Columns.Count)
                     .OrderBy(column => column.DisplayIndex))
            grid.Columns[saved.Index].DisplayIndex = saved.DisplayIndex;
    }

    private static void ApplySplitter(GridSplitter splitter, SplitterLayout layout)
    {
        if (splitter.ResizeDirection != layout.Direction
            || VisualTreeTraversal.FindAncestor<Grid>(splitter) is not { } grid)
            return;
        if (layout.Direction == GridResizeDirection.Columns)
        {
            Apply(grid.ColumnDefinitions, layout.FirstIndex, layout.First);
            Apply(grid.ColumnDefinitions, layout.SecondIndex, layout.Second);
        }
        else
        {
            Apply(grid.RowDefinitions, layout.FirstIndex, layout.First);
            Apply(grid.RowDefinitions, layout.SecondIndex, layout.Second);
        }
    }

    private static void Apply(IList<RowDefinition> definitions, int index, GridLengthLayout saved)
    {
        if (index >= 0 && index < definitions.Count && Valid(saved))
            definitions[index].Height = new GridLength(Math.Min(saved.Value, 100_000), saved.Unit);
    }

    private static void Apply(IList<ColumnDefinition> definitions, int index, GridLengthLayout saved)
    {
        if (index >= 0 && index < definitions.Count && Valid(saved))
            definitions[index].Width = new GridLength(Math.Min(saved.Value, 100_000), saved.Unit);
    }

    private static bool Valid(GridLengthLayout saved)
        => double.IsFinite(saved.Value) && saved.Value >= 0;

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T match) yield return match;
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            foreach (var child in Descendants<T>(VisualTreeHelper.GetChild(root, index)))
                yield return child;
    }
}
