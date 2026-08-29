using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace LocalLlmConsole;

public static partial class BenchmarksPageFactory
{
    private static Grid CreateHistoryFooter(
        BenchmarksPageController controller,
        DataGrid history,
        TextBlock page,
        Button previous,
        Button next)
    {
        ConfigureHistoryInteractions(controller, history);
        var row = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        page.VerticalAlignment = VerticalAlignment.Center;
        page.Margin = new Thickness(4, 0, 12, 6);
        var pager = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        pager.Children.Add(previous);
        pager.Children.Add(page);
        pager.Children.Add(next);
        Grid.SetColumn(pager, 1);
        row.Children.Add(pager);
        return row;
    }

    private static void ConfigureHistoryInteractions(BenchmarksPageController controller, DataGrid history)
    {
        IReadOnlyList<BenchmarkRunRow> firstClickSelection = [];
        history.PreviewMouseLeftButtonDown += (sender, args) =>
        {
            var source = args.OriginalSource as DependencyObject;
            if (Ancestor<Button>(source) is not null) return;
            var clicked = ItemsControl.ContainerFromElement(history, source) as DataGridRow;
            if (args.ClickCount == 1)
            {
                firstClickSelection = clicked?.IsSelected == true
                    ? history.SelectedItems.Cast<BenchmarkRunRow>().ToArray()
                    : [];
                return;
            }
            if (args.ClickCount != 2 || clicked?.Item is not BenchmarkRunRow row) return;
            if (firstClickSelection.Count == 2 && firstClickSelection.Contains(row))
            {
                history.SelectedItems.Clear();
                foreach (var selected in firstClickSelection) history.SelectedItems.Add(selected);
            }
            controller.ActivateHistorySelection(sender, args);
            args.Handled = true;
        };
        history.PreviewMouseRightButtonDown += (_, args) =>
        {
            var clicked = ItemsControl.ContainerFromElement(history, args.OriginalSource as DependencyObject) as DataGridRow;
            if (clicked is null || clicked.IsSelected) return;
            history.SelectedItems.Clear();
            clicked.IsSelected = true;
        };

        var view = MenuAction("View report", controller.Details);
        var compare = MenuAction("Compare selected", controller.Compare);
        var pause = MenuAction("Pause after current test", controller.Pause);
        var resume = MenuAction("Resume selected run", controller.Resume);
        var export = MenuAction("Export results", controller.Export);
        var clone = MenuAction("Clone selected plan", controller.Clone);
        var openLog = MenuAction("Open run log", controller.OpenLog);
        var menu = new ContextMenu();
        menu.Items.Add(view);
        menu.Items.Add(compare);
        menu.Items.Add(new Separator());
        menu.Items.Add(pause);
        menu.Items.Add(resume);
        menu.Items.Add(export);
        menu.Items.Add(clone);
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuAction("Import plan", controller.ImportPlan));
        menu.Items.Add(MenuAction("Export current plan", controller.ExportPlan));
        menu.Items.Add(openLog);
        menu.Items.Add(MenuAction("Refresh runs", controller.Refresh));
        menu.Opened += (_, _) =>
        {
            var selected = history.SelectedItems.Count;
            view.IsEnabled = selected == 1;
            compare.IsEnabled = selected == 2;
            foreach (var item in new[] { pause, resume, export, clone, openLog }) item.IsEnabled = selected >= 1;
        };
        history.ContextMenu = menu;
    }

    private static MenuItem MenuAction(string text, RoutedEventHandler handler)
    {
        var item = new MenuItem { Header = text };
        item.Click += handler;
        return item;
    }

    private static T? Ancestor<T>(DependencyObject? source) where T : DependencyObject
    {
        while (source is not null)
        {
            if (source is T match) return match;
            source = VisualTreeHelper.GetParent(source);
        }
        return null;
    }
}
