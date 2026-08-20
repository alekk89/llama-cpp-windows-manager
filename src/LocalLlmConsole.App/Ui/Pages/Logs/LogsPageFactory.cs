using System.Collections;
using System.Windows;
using System.Windows.Controls;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed record LogsPageActions(
    RoutedEventHandler Refresh,
    RoutedEventHandler OpenSelected,
    RoutedEventHandler OpenLogsFolder,
    RoutedEventHandler CreateDiagnosticsBundle,
    RoutedEventHandler DeleteSelected,
    RoutedEventHandler DeleteAll,
    RoutedEventHandler OpenRow,
    RoutedEventHandler DeleteRow,
    SelectionChangedEventHandler SelectionChanged);

public sealed record LogsPageRequest(
    IEnumerable Rows,
    LogsPageActions Actions,
    Func<string, string> ButtonToolTip);

public sealed record LogsPageControls(
    DataGrid LogsGrid,
    WpfTextBox LogsBox);

public sealed record LogsPageBuildResult(
    Grid Content,
    LogsPageControls Controls);

public static class LogsPageFactory
{
    public static LogsPageBuildResult Create(LogsPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Rows);
        ArgumentNullException.ThrowIfNull(request.Actions);
        ArgumentNullException.ThrowIfNull(request.ButtonToolTip);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition());

        var toolbar = Toolbar(request);
        Grid.SetRow(toolbar, 0);
        root.Children.Add(toolbar);

        var logsGrid = PageSectionFactory.GridFor(
            (Loc.T("Logs.Col.Type"), "C1", .9),
            (Loc.T("Logs.Col.File"), "C2", 2.1),
            (Loc.T("Logs.Col.Related"), "C3", 2.5),
            (Loc.T("Logs.Col.Updated"), "C4", 1.1),
            (Loc.T("Logs.Col.Size"), "C5", .7));
        logsGrid.SelectionMode = DataGridSelectionMode.Extended;
        logsGrid.SelectionUnit = DataGridSelectionUnit.FullRow;
        PageSectionFactory.AddButtonColumn(logsGrid, Loc.T("Logs.ActionBtn.Open"), "C6", "B1", request.Actions.OpenRow, .55, tooltipBinding: "T1");
        PageSectionFactory.AddButtonColumn(logsGrid, Loc.T("Logs.ActionBtn.Delete"), "C7", "B2", request.Actions.DeleteRow, .65, tooltipBinding: "T2", visualRole: VisualRole.Danger);
        logsGrid.ItemsSource = request.Rows;
        DataGridRowContextMenu.Attach(
            logsGrid,
            new(row => ((UiRow)row).C6,
                row => row is UiRow { B1: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.OpenRow, row),
                ToolTip: row => ((UiRow)row).T1),
            new(row => ((UiRow)row).C7,
                row => row is UiRow { B2: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.DeleteRow, row),
                SeparatorBefore: true,
                ToolTip: row => ((UiRow)row).T2));
        logsGrid.SelectionChanged += request.Actions.SelectionChanged;
        var listFrame = PageSectionFactory.GridFrame(logsGrid);
        Grid.SetRow(listFrame, 1);
        root.Children.Add(listFrame);

        root.Children.Add(PageSectionFactory.HorizontalGridSplitter(2));

        var logsBox = new WpfTextBox
        {
            IsReadOnly = true,
            IsUndoEnabled = false,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto
        };
        var viewer = new Border
        {
            Background = (WpfBrush)WpfApplication.Current.Resources["InputBack"],
            BorderBrush = (WpfBrush)WpfApplication.Current.Resources["PanelBorder"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Margin = new Thickness(0, 6, 0, 6),
            Child = logsBox
        };
        Grid.SetRow(viewer, 3);
        root.Children.Add(viewer);

        return new LogsPageBuildResult(root, new LogsPageControls(logsGrid, logsBox));
    }

    private static Grid Toolbar(LogsPageRequest request)
    {
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var leftActions = Bar();
        leftActions.Children.Add(Button(Loc.T("Logs.RefreshButton"), request.Actions.Refresh, request.ButtonToolTip));
        leftActions.Children.Add(Button(Loc.T("Logs.OpenSelectedButton"), request.Actions.OpenSelected, request.ButtonToolTip));
        leftActions.Children.Add(Button(Loc.T("Logs.OpenFolderButton"), request.Actions.OpenLogsFolder, request.ButtonToolTip));
        leftActions.Children.Add(Button(Loc.T("Logs.CreateDiagnosticsButton"), request.Actions.CreateDiagnosticsBundle, request.ButtonToolTip));
        toolbar.Children.Add(leftActions);

        var rightActions = Bar();
        rightActions.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        var deleteSelected = Button(Loc.T("Logs.DeleteSelectedButton"), request.Actions.DeleteSelected, request.ButtonToolTip);
        var deleteAll = Button(Loc.T("Logs.DeleteAllButton"), request.Actions.DeleteAll, request.ButtonToolTip);
        VisualRole.SetButtonRole(deleteSelected, VisualRole.Danger);
        VisualRole.SetButtonRole(deleteAll, VisualRole.Danger);
        rightActions.Children.Add(deleteSelected);
        rightActions.Children.Add(deleteAll);
        Grid.SetColumn(rightActions, 2);
        toolbar.Children.Add(rightActions);
        return toolbar;
    }

    private static WrapPanel Bar() => new()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal,
        Margin = new Thickness(0)
    };

    private static WpfButton Button(string text, RoutedEventHandler click, Func<string, string> toolTip)
    {
        var button = new WpfButton
        {
            Content = text,
            ToolTip = toolTip(text)
        };
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += click;
        return button;
    }
}
