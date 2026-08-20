using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace LocalLlmConsole;

public sealed record DataGridRowContextAction(
    Func<object, string> Header,
    Func<object, bool> CanExecute,
    Func<object, Task> ExecuteAsync,
    bool SeparatorBefore = false,
    Func<object, bool>? IsVisible = null,
    Func<object, string>? ToolTip = null);

public static class DataGridRowContextMenu
{
    public static void Attach(DataGrid grid, params DataGridRowContextAction[] actions)
    {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(actions);

        var menu = new ContextMenu();
        var entries = new List<(DataGridRowContextAction Action, Separator? Separator, MenuItem Item)>();
        foreach (var action in actions)
        {
            Separator? separator = null;
            if (action.SeparatorBefore)
            {
                separator = new Separator();
                menu.Items.Add(separator);
            }

            var item = new MenuItem();
            item.Click += async (_, _) =>
            {
                if (grid.SelectedItem is { } row && action.CanExecute(row))
                    await action.ExecuteAsync(row);
            };
            menu.Items.Add(item);
            entries.Add((action, separator, item));
        }

        menu.Opened += (_, _) => UpdateEntries(grid.SelectedItem, entries);
        grid.ContextMenu = menu;
        grid.PreviewMouseRightButtonDown += (_, args) => SelectRightClickedRow(grid, args);
    }

    public static Task RaiseRowActionAsync(RoutedEventHandler handler, object row)
    {
        ArgumentNullException.ThrowIfNull(handler);
        ArgumentNullException.ThrowIfNull(row);

        var source = new MenuItem { Tag = row };
        handler(source, new RoutedEventArgs(MenuItem.ClickEvent, source));
        return Task.CompletedTask;
    }

    private static void UpdateEntries(
        object? row,
        IEnumerable<(DataGridRowContextAction Action, Separator? Separator, MenuItem Item)> entries)
    {
        foreach (var (action, separator, item) in entries)
        {
            var visible = row is not null && (action.IsVisible?.Invoke(row) ?? true);
            item.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            item.Header = row is null ? "" : action.Header(row);
            item.IsEnabled = row is not null && action.CanExecute(row);
            item.ToolTip = row is null ? null : action.ToolTip?.Invoke(row);
            if (separator is not null)
                separator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static void SelectRightClickedRow(DataGrid grid, MouseButtonEventArgs args)
    {
        for (var element = args.OriginalSource as DependencyObject;
             element is not null;
             element = VisualTreeHelper.GetParent(element))
        {
            if (element is not DataGridRow row) continue;
            row.IsSelected = true;
            grid.SelectedItem = row.Item;
            return;
        }
    }
}
