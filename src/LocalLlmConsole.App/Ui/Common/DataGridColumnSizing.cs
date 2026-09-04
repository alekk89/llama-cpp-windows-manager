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
}
