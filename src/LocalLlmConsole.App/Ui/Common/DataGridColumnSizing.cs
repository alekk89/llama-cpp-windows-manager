using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

internal static class DataGridColumnSizing
{
    private static readonly DependencyProperty FillColumnProperty = DependencyProperty.RegisterAttached(
        "FillColumn", typeof(bool), typeof(DataGridColumnSizing), new PropertyMetadata(false));

    public static bool UsesFillColumn(DataGrid grid)
    {
        if (grid.Columns.Any(column => column.Width.IsStar)) grid.SetValue(FillColumnProperty, true);
        return (bool)grid.GetValue(FillColumnProperty);
    }

    public static DataGridColumn? StretchColumn(DataGrid grid)
        => grid.Columns.Where(column => column.Visibility == Visibility.Visible && column.CanUserResize && column.MaxWidth > column.MinWidth)
            .OrderBy(column => column is DataGridTextColumn ? 0 : 1)
            .ThenBy(column => column.DisplayIndex).FirstOrDefault();

    public static void FillLeftColumn(DataGrid grid)
    {
        var widths = grid.Columns.Where(column => column.Visibility == Visibility.Visible && double.IsFinite(column.ActualWidth) && column.ActualWidth > 0)
            .ToDictionary(column => column, column => column.ActualWidth);
        var stretch = StretchColumn(grid);
        if (stretch is null || !widths.ContainsKey(stretch)) return;
        var scroller = grid.Template.FindName("DG_ScrollViewer", grid) as ScrollViewer;
        var viewport = scroller is { ViewportWidth: > 0 } ? scroller.ViewportWidth
            : grid.ActualWidth - grid.BorderThickness.Left - grid.BorderThickness.Right;
        var available = viewport - grid.RowHeaderActualWidth - widths.Where(pair => pair.Key != stretch).Sum(pair => pair.Value);
        // Keep pixel widths so WPF cannot compress neighboring columns when Name reaches
        // its minimum. The grid's normal horizontal scrollbar handles the remaining overflow.
        foreach (var (column, width) in widths) column.Width = new DataGridLength(width);
        stretch.Width = new DataGridLength(Math.Clamp(available, stretch.MinWidth, stretch.MaxWidth));
    }

    /// <summary>
    /// Widths after moving the left edge of <paramref name="right"/> by
    /// <paramref name="growth"/> pixels (positive widens the columns from that
    /// edge rightwards). Returns how far the edge could actually move.
    /// </summary>
    public static (IReadOnlyDictionary<DataGridColumn, double> Widths, double Applied) PlanEdgeMove(
        IReadOnlyList<DataGridColumn> ordered,
        IReadOnlyDictionary<DataGridColumn, double> widths,
        DataGridColumn right,
        DataGridColumn stretch,
        double growth)
    {
        var result = widths.ToDictionary(pair => pair.Key, pair => pair.Value);
        var edge = ordered.ToList().IndexOf(right);
        if (edge < 0 || growth == 0 || !double.IsFinite(growth)) return (result, 0);
        var leftSide = ordered.Take(edge).Reverse().ToArray();   // nearest the edge first
        var rightSide = ordered.Skip(edge).ToArray();            // nearest the edge first
        var (growing, shrinking) = growth > 0 ? (rightSide, leftSide) : (leftSide, rightSide);
        if (growing.Length == 0 || shrinking.Length == 0) return (result, 0);

        var grower = growing.Contains(stretch) ? stretch : growing[0];
        if (!grower.CanUserResize) return (result, 0);
        var donors = shrinking.Where(column => column.CanUserResize && column != grower)
            .OrderBy(column => column == stretch ? 0 : 1)
            .ThenBy(column => Array.IndexOf(shrinking, column))
            .ToArray();
        var wanted = Math.Abs(growth);
        var room = Math.Max(0, grower.MaxWidth - widths[grower]);
        var available = donors.Sum(column => Math.Max(0, widths[column] - column.MinWidth));
        var applied = Math.Min(wanted, Math.Min(room, available));
        result[grower] = widths[grower] + applied;
        var remaining = applied;
        foreach (var donor in donors)
        {
            if (remaining <= 0) break;
            var take = Math.Min(remaining, Math.Max(0, widths[donor] - donor.MinWidth));
            result[donor] = widths[donor] - take;
            remaining -= take;
        }
        return (result, Math.Sign(growth) * applied);
    }
}
