using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPoint = System.Windows.Point;

namespace LocalLlmConsole;

public sealed partial class OverviewDashboardCardView
{
    private OverviewDashboardMetricRowView? _dragRow;
    private WpfPoint _dragStart;
    private bool _reordering;
    private bool _rowDragActive;

    private void ConfigureMetricReorderDrag()
    {
        _values.PreviewMouseLeftButtonDown += BeginMetricDrag;
        _values.PreviewMouseMove += TrackMetricDrag;
        _values.PreviewMouseLeftButtonUp += FinishMetricDrag;
        _values.LostMouseCapture += (_, _) => EndMetricDrag();
    }

    private void BeginMetricDrag(object sender, MouseButtonEventArgs args)
    {
        if (!_reordering || args.ChangedButton != MouseButton.Left) return;
        var source = args.OriginalSource as DependencyObject;
        _dragRow = _rows.Values.FirstOrDefault(row =>
            ReferenceEquals(row.Root, source) || (source is not null && row.Root.IsAncestorOf(source)));
        if (_dragRow is null) return;

        _dragStart = args.GetPosition(_values);
        _rowDragActive = false;
        Mouse.Capture(_values, CaptureMode.SubTree);
        args.Handled = true;
    }

    private void TrackMetricDrag(object sender, WpfMouseEventArgs args)
    {
        if (_dragRow is not { } row) return;
        if (args.LeftButton != MouseButtonState.Pressed)
        {
            EndMetricDrag();
            return;
        }

        var pointer = args.GetPosition(_values);
        if (!_rowDragActive)
        {
            if (Math.Abs(pointer.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
                return;
            _rowDragActive = true;
            row.SetDragState(true);
        }

        MoveMetric(row.MetricId, MetricDropIndex(pointer.Y, row.Root));
        args.Handled = true;
    }

    private void FinishMetricDrag(object sender, MouseButtonEventArgs args)
    {
        if (_dragRow is null || args.ChangedButton != MouseButton.Left) return;
        EndMetricDrag();
        args.Handled = true;
    }

    private int MetricDropIndex(double pointerY, Grid draggedRoot)
    {
        var roots = _values.Children.OfType<Grid>().ToArray();
        var source = Array.IndexOf(roots, draggedRoot);
        var insertion = roots.Length;
        for (var index = 0; index < roots.Length; index++)
        {
            var midpoint = roots[index].TranslatePoint(
                new WpfPoint(0, roots[index].ActualHeight / 2), _values).Y;
            if (pointerY >= midpoint) continue;
            insertion = index;
            break;
        }
        if (insertion > source) insertion--;
        return Math.Clamp(insertion, 0, roots.Length - 1);
    }

    private void MoveMetric(string metricId, int target)
    {
        if (!_rows.TryGetValue(metricId, out var row)) return;
        var source = _values.Children.IndexOf(row.Root);
        target = Math.Clamp(target, 0, _values.Children.Count - 1);
        if (source < 0 || source == target) return;
        _values.Children.RemoveAt(source);
        _values.Children.Insert(target, row.Root);
        UpdateReorderRows(active: true);
    }

    private void EndMetricDrag()
    {
        var row = _dragRow;
        _dragRow = null;
        _rowDragActive = false;
        row?.SetDragState(false);
        if (ReferenceEquals(Mouse.Captured, _values))
            Mouse.Capture(null);
    }
}
