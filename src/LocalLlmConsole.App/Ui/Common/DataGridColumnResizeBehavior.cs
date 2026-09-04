using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace LocalLlmConsole;

internal sealed class DataGridColumnResizeBehavior : IDisposable
{
    private static readonly DependencyProperty OwnerProperty = DependencyProperty.RegisterAttached(
        "ResizeOwner", typeof(DataGridColumnResizeBehavior), typeof(DataGridColumnResizeBehavior));
    private readonly DataGrid _grid;
    private readonly Action _completed;
    private Thumb? _thumb;
    private DragState? _drag;

    static DataGridColumnResizeBehavior()
    {
        // Run before WPF's per-thumb handlers, which resize only the immediate neighbor.
        // Other thumbs retain their normal behavior unless this attachment owns the drag.
        EventManager.RegisterClassHandler(typeof(Thumb), Thumb.DragStartedEvent, new DragStartedEventHandler(HandleDrag));
        EventManager.RegisterClassHandler(typeof(Thumb), Thumb.DragDeltaEvent, new DragDeltaEventHandler(HandleDrag));
        EventManager.RegisterClassHandler(typeof(Thumb), Thumb.DragCompletedEvent, new DragCompletedEventHandler(HandleDrag));
    }

    public DataGridColumnResizeBehavior(DataGrid grid, Action completed)
    {
        _grid = grid;
        _completed = completed;
        grid.AddHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Begin), handledEventsToo: true);
        grid.SizeChanged += SizeChanged;
        grid.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ScrollChanged));
    }

    public void Dispose()
    {
        _grid.RemoveHandler(Mouse.PreviewMouseDownEvent, new MouseButtonEventHandler(Begin));
        _grid.SizeChanged -= SizeChanged;
        _grid.RemoveHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(ScrollChanged));
        Finish(canceled: true);
    }

    private void SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (args.WidthChanged && _drag is null) DataGridColumnSizing.FillLeftColumn(_grid);
    }

    private void ScrollChanged(object sender, ScrollChangedEventArgs args)
    {
        if (_drag is null && args.ViewportWidthChange != 0
            && ReferenceEquals(args.OriginalSource, _grid.Template.FindName("DG_ScrollViewer", _grid)))
            DataGridColumnSizing.FillLeftColumn(_grid);
    }

    private void Begin(object sender, MouseButtonEventArgs args)
    {
        if (args.ChangedButton != MouseButton.Left || args.ClickCount != 1 || !_grid.CanUserResizeColumns
            || args.OriginalSource is not DependencyObject source
            || VisualTreeTraversal.FindAncestor<Thumb>(source) is not { Name: "PART_LeftHeaderGripper" or "PART_RightHeaderGripper" } thumb
            || VisualTreeTraversal.FindAncestor<DataGridColumnHeader>(thumb) is not { Column: { } headerColumn }) return;

        var next = _grid.Columns.Where(column => column.Visibility == Visibility.Visible && column.DisplayIndex > headerColumn.DisplayIndex)
            .OrderBy(column => column.DisplayIndex).FirstOrDefault();
        var fromLeft = thumb.Name == "PART_LeftHeaderGripper" || next is not null;
        var target = thumb.Name == "PART_LeftHeaderGripper" ? headerColumn : next ?? headerColumn;
        var stretch = DataGridColumnSizing.StretchColumn(_grid);
        if (stretch is null || target == stretch || !target.CanUserResize || target.MaxWidth <= target.MinWidth) return;

        Finish(canceled: true);
        var widths = _grid.Columns.Where(column => column.Visibility == Visibility.Visible && double.IsFinite(column.ActualWidth) && column.ActualWidth > 0)
            .ToDictionary(column => column, column => column.ActualWidth);
        if (!widths.ContainsKey(target) || !widths.ContainsKey(stretch)) return;
        _drag = new DragState(target, stretch, widths, fromLeft ? -1 : 1);
        _thumb = thumb;
        thumb.SetValue(OwnerProperty, this);
        foreach (var (column, width) in widths) column.Width = new DataGridLength(width);
    }

    private static void HandleDrag(object sender, RoutedEventArgs args)
    {
        if (sender is not Thumb thumb || thumb.GetValue(OwnerProperty) is not DataGridColumnResizeBehavior owner) return;
        args.Handled = true;
        if (args is DragDeltaEventArgs delta) owner.Move(delta.HorizontalChange);
        else if (args is DragCompletedEventArgs completed) owner.Finish(completed.Canceled);
    }

    private void Move(double horizontalChange)
    {
        if (_drag is not { } drag || !double.IsFinite(horizontalChange)) return;
        drag.Delta += drag.Direction * horizontalChange;
        var targetWidth = drag.Widths[drag.Target];
        var stretchWidth = drag.Widths[drag.Stretch];
        var minimum = Math.Max(drag.Target.MinWidth, targetWidth - (drag.Stretch.MaxWidth - stretchWidth));
        var maximum = Math.Min(drag.Target.MaxWidth, targetWidth + stretchWidth - drag.Stretch.MinWidth);
        var width = Math.Clamp(targetWidth + drag.Delta, minimum, maximum);
        drag.Delta = width - targetWidth;
        drag.Target.Width = new DataGridLength(width);
        drag.Stretch.Width = new DataGridLength(stretchWidth - (width - targetWidth));
    }

    private void Finish(bool canceled)
    {
        if (_drag is not { } drag) return;
        _thumb?.ClearValue(OwnerProperty);
        _thumb = null;
        _drag = null;
        if (canceled)
            foreach (var (column, width) in drag.Widths) column.Width = new DataGridLength(width);
        _grid.UpdateLayout();
        DataGridColumnSizing.FillLeftColumn(_grid);
        _grid.UpdateLayout();
        _completed();
    }

    private sealed record DragState(DataGridColumn Target, DataGridColumn Stretch, Dictionary<DataGridColumn, double> Widths, int Direction)
    {
        public double Delta { get; set; }
    }
}
