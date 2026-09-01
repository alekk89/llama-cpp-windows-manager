using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed record SettingsPageActions(
    SelectionChangedEventHandler ThemeChanged,
    RoutedEventHandler RevealSecret,
    RoutedEventHandler CopySecret,
    RoutedEventHandler RowAction,
    Action SliderCommitted);

public sealed record SettingsPageRequest(
    IEnumerable Rows,
    string ThemeMode,
    SettingsPageActions Actions,
    StartupLaunchProfileSettingsSnapshot? StartupProfiles = null,
    StartupLaunchProfileSettingsActions? StartupProfileActions = null);

public sealed record StartupLaunchProfileSettingsActions(
    Func<string, Task> AddAsync,
    Func<string, Task> RemoveAsync,
    Func<Task<StartupLaunchProfileSettingsSnapshot>> RefreshAsync,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class StartupLaunchProfileSettingsControls
{
    private readonly ObservableCollection<StartupLaunchProfileChoice> _available = [];
    private readonly ObservableCollection<StartupLaunchProfileChoice> _selected = [];

    public WpfComboBox ProfileCombo { get; }
    public WpfButton AddButton { get; }
    public DataGrid SelectedGrid { get; }
    public TextBlock EmptyText { get; }

    public StartupLaunchProfileSettingsControls(
        WpfComboBox profileCombo,
        WpfButton addButton,
        DataGrid selectedGrid,
        TextBlock emptyText)
    {
        ProfileCombo = profileCombo;
        AddButton = addButton;
        SelectedGrid = selectedGrid;
        EmptyText = emptyText;
        ProfileCombo.ItemsSource = _available;
        SelectedGrid.ItemsSource = _selected;
    }

    public void Apply(StartupLaunchProfileSettingsSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _available.Clear();
        foreach (var choice in snapshot.Available)
            _available.Add(choice);
        _selected.Clear();
        foreach (var choice in snapshot.Selected)
            _selected.Add(choice);
        ProfileCombo.SelectedItem = null;
        AddButton.IsEnabled = false;
        EmptyText.Visibility = _selected.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        SelectedGrid.Visibility = _selected.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

}

public sealed record SettingsPageControls(
    DockPanel Root,
    WpfComboBox ThemeCombo,
    DataGrid SettingsGrid,
    Grid SettingsColumns,
    StartupLaunchProfileSettingsControls StartupProfiles);

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
        var startupProfileSection = StartupProfileSection(
            request.StartupProfiles ?? StartupLaunchProfileSettingsSnapshot.Empty,
            request.StartupProfileActions,
            out var startupProfileControls);
        var sections = SettingsSections(rows, request, startupProfileSection);
        root.Children.Add(sections.Root);

        return new SettingsPageControls(root, themeCombo, sections.FirstGrid, sections.Columns, startupProfileControls);
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
        SettingsPageRequest request,
        FrameworkElement startupProfileSection)
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
        var sectionViews = new List<FrameworkElement>();

        foreach (var group in rows.GroupBy(row => row.Group))
        {
            var grid = SettingsGrid(group, request.Actions);
            firstGrid ??= grid;
            var target = groupIndex++ % 2 == 0 ? left : right;
            var section = PageSectionFactory.GridSection(group.Key, grid);
            sectionViews.Add(section);
            target.Children.Add(section);
            if (string.Equals(group.Key, Loc.T("Settings.Group.Model"), StringComparison.Ordinal))
            {
                var startupTarget = groupIndex++ % 2 == 0 ? left : right;
                sectionViews.Add(startupProfileSection);
                startupTarget.Children.Add(startupProfileSection);
            }
        }

        SettingsPageResponsiveCoordinator.Configure(columns, left, right, sectionViews);

        var scroll = new ScrollViewer
        {
            Content = columns,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        return (scroll, firstGrid ?? SettingsGrid(Array.Empty<EditableSettingRow>(), request.Actions), columns);
    }

    private static FrameworkElement StartupProfileSection(
        StartupLaunchProfileSettingsSnapshot snapshot,
        StartupLaunchProfileSettingsActions? actions,
        out StartupLaunchProfileSettingsControls controls)
    {
        var body = new StackPanel { Margin = new Thickness(8) };
        var picker = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        picker.ColumnDefinitions.Add(new ColumnDefinition());
        picker.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var combo = new SearchableComboBox
        {
            DisplayMemberPath = nameof(StartupLaunchProfileChoice.DisplayName),
            SearchTextSelector = item => (item as StartupLaunchProfileChoice)?.DisplayName ?? "",
            Height = 28,
            MinHeight = 28,
            Margin = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            ToolTip = Loc.T("Settings.StartupProfiles.Title")
        };
        AutomationProperties.SetName(combo, Loc.T("Settings.StartupProfiles.Title"));
        AutomationProperties.SetHelpText(combo, Loc.T("Settings.StartupProfiles.Title"));
        picker.Children.Add(combo);
        var add = new WpfButton
        {
            Content = Loc.T("Runtimes.CustomRepo.AddButton"),
            IsEnabled = false,
            Height = 28,
            MinHeight = 28,
            Padding = new Thickness(8, 1, 8, 2),
            Margin = new Thickness(7, 0, 0, 0)
        };
        VisualRole.SetButtonRole(add, VisualRole.Primary);
        AutomationProperties.SetName(add, Loc.T("Runtimes.CustomRepo.AddButton"));
        Grid.SetColumn(add, 1);
        picker.Children.Add(add);
        body.Children.Add(picker);

        var empty = new TextBlock
        {
            Text = Loc.T("Dashboard.Value.None"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(2, 4, 2, 6)
        };
        empty.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft");
        body.Children.Add(empty);

        var selected = PageSectionFactory.GridFor(
            (Loc.T("Overview.SessionsCol.Model"), nameof(StartupLaunchProfileChoice.ModelName), 1.2),
            (Loc.T("Lifetime.Filter.Profile"), nameof(StartupLaunchProfileChoice.ProfileName), 1.0),
            (Loc.T("Models.Col.Port"), nameof(StartupLaunchProfileChoice.Port), .45));
        selected.AutoGenerateColumns = false;
        selected.IsReadOnly = true;
        selected.HeadersVisibility = DataGridHeadersVisibility.Column;
        selected.MaxHeight = 220;
        AutomationProperties.SetName(selected, Loc.T("Settings.StartupProfiles.Title"));
        body.Children.Add(selected);

        controls = new StartupLaunchProfileSettingsControls(combo, add, selected, empty);
        var state = controls;
        AddRemoveButtonColumn(selected, actions, state);
        controls.Apply(snapshot);
        combo.SelectionChanged += (_, _) => add.IsEnabled = combo.SelectedItem is StartupLaunchProfileChoice;
        add.Click += async (_, _) =>
        {
            if (combo.SelectedItem is not StartupLaunchProfileChoice choice || actions is null) return;
            await actions.RunEventAsync(async () =>
            {
                await actions.AddAsync(choice.ProfileId);
                state.Apply(await actions.RefreshAsync());
            });
        };

        return PageSectionFactory.FramedSection(Loc.T("Settings.StartupProfiles.Title"), body);
    }

    private static void AddRemoveButtonColumn(
        DataGrid grid,
        StartupLaunchProfileSettingsActions? actions,
        StartupLaunchProfileSettingsControls state)
    {
        var button = new FrameworkElementFactory(typeof(ResponsiveActionButton));
        button.SetValue(ResponsiveActionButton.FullLabelProperty, Loc.T("Models.ActionBtn.Remove"));
        button.SetValue(ResponsiveActionButton.CompactLabelProperty, "×");
        button.SetValue(FrameworkElement.ToolTipProperty, Loc.T("Models.Profile.RemoveTitle"));
        button.SetValue(AutomationProperties.NameProperty, Loc.T("Models.ActionBtn.Remove"));
        button.SetBinding(FrameworkElement.TagProperty, new WpfBinding("."));
        PageSectionFactory.ConfigureGridActionButton(button);
        button.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler(async (sender, _) =>
        {
            if (sender is not WpfButton { Tag: StartupLaunchProfileChoice choice } || actions is null) return;
            await actions.RunEventAsync(async () =>
            {
                await actions.RemoveAsync(choice.ProfileId);
                state.Apply(await actions.RefreshAsync());
            });
        }));
        grid.Columns.Add(new ResponsiveActionDataGridColumn
        {
            Header = Loc.T("Common.ActionButton"),
            Width = new DataGridLength(.55, DataGridLengthUnitType.Star),
            MinWidth = 36,
            CellTemplate = new DataTemplate { VisualTree = button }
        });
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
            actions.RowAction,
            actions.SliderCommitted));
        return grid;
    }
}
