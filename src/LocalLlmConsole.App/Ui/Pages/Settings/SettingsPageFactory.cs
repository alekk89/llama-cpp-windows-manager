using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed record SettingsPageActions(
    SelectionChangedEventHandler ThemeChanged,
    RoutedEventHandler RevealSecret,
    RoutedEventHandler CopySecret,
    RoutedEventHandler RowAction);

public sealed record SettingsPageRequest(
    IEnumerable Rows,
    string ThemeMode,
    SettingsPageActions Actions);

public sealed record SettingsPageControls(
    DockPanel Root,
    WpfComboBox ThemeCombo,
    DataGrid SettingsGrid,
    Grid SettingsColumns);

public static class SettingsPageFactory
{
    public static SettingsPageControls Create(SettingsPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Rows);
        ArgumentNullException.ThrowIfNull(request.Actions);

        var root = new DockPanel { Margin = new Thickness(12) };
        var toolbar = Toolbar(request, out var themeCombo);
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var rows = request.Rows.Cast<EditableSettingRow>().ToList();
        var sections = SettingsSections(rows, request);
        root.Children.Add(sections.Root);

        return new SettingsPageControls(root, themeCombo, sections.FirstGrid, sections.Columns);
    }

    private static Grid Toolbar(SettingsPageRequest request, out WpfComboBox themeCombo)
    {
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var autoApplyHint = new TextBlock
        {
            Text = Loc.T("Settings.AutoApplyHint"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        };
        autoApplyHint.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        toolbar.Children.Add(autoApplyHint);

        var themeBar = new WrapPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var themeLabel = new TextBlock
        {
            Text = Loc.T("Settings.ThemeLabel"),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 6)
        };
        themeLabel.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        themeBar.Children.Add(themeLabel);
        themeCombo = new WpfComboBox
        {
            ItemsSource = new[] { "system", "light", "dark" },
            SelectedItem = AppPreferenceService.ThemeMode(request.ThemeMode),
            Width = 110
        };
        themeCombo.SelectionChanged += request.Actions.ThemeChanged;
        themeBar.Children.Add(themeCombo);
        Grid.SetColumn(themeBar, 1);
        toolbar.Children.Add(themeBar);
        return toolbar;
    }

    private static (ScrollViewer Root, DataGrid FirstGrid, Grid Columns) SettingsSections(
        IReadOnlyList<EditableSettingRow> rows,
        SettingsPageRequest request)
    {
        var columns = new Grid();
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        columns.ColumnDefinitions.Add(new ColumnDefinition());
        var left = new StackPanel { Margin = new Thickness(0, 0, 6, 0) };
        var right = new StackPanel { Margin = new Thickness(6, 0, 0, 0) };
        columns.Children.Add(left);
        Grid.SetColumn(right, 1);
        columns.Children.Add(right);

        DataGrid? firstGrid = null;
        var groupIndex = 0;

        foreach (var group in rows.GroupBy(row => row.Group))
        {
            var grid = SettingsGrid(group, request.Actions);
            firstGrid ??= grid;
            var target = groupIndex++ % 2 == 0 ? left : right;
            target.Children.Add(PageSectionFactory.GridSection(group.Key, grid));
        }

        var scroll = new ScrollViewer
        {
            Content = columns,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        return (scroll, firstGrid ?? SettingsGrid(Array.Empty<EditableSettingRow>(), request.Actions), columns);
    }

    private static DataGrid SettingsGrid(IEnumerable<EditableSettingRow> rows, SettingsPageActions actions)
    {
        var rowList = rows.ToList();
        var grid = new DataGrid
        {
            IsReadOnly = false,
            ItemsSource = rowList,
            RowHeight = 36
        };
        PageSectionFactory.PolishGrid(grid);
        var textStyle = (Style)WpfApplication.Current.Resources["GridCellText"];
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("Settings.Col.Setting"),
            Binding = new WpfBinding(nameof(EditableSettingRow.Label)),
            IsReadOnly = true,
            ElementStyle = SettingsGridColumnFactory.CellTextStyle(textStyle),
            MinWidth = 90,
            Width = new DataGridLength(.95, DataGridLengthUnitType.Star),
            CanUserResize = true
        });
        grid.Columns.Add(SettingsGridColumnFactory.ValueColumn(
            actions.RevealSecret,
            actions.CopySecret,
            actions.RowAction));
        return grid;
    }
}
