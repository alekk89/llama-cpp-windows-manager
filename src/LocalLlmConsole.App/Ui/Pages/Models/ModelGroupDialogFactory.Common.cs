using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public static partial class ModelGroupDialogFactory
{
    private static Grid EditorFields(params (string Label, FrameworkElement Control)[] fields)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var row = 0; row < fields.Length; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });
            var label = new TextBlock
            {
                Text = fields[row].Label,
                Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            Grid.SetRow(label, row);
            grid.Children.Add(label);
            var control = fields[row].Control;
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, row);
            Grid.SetColumn(control, 1);
            grid.Children.Add(control);
        }
        return grid;
    }

    private static void RefreshProfileCounts(
        IEnumerable<ModelGroupEditorRow> rows,
        IReadOnlyDictionary<string, string> assignments)
    {
        foreach (var row in rows)
            row.ProfileCount = assignments.Count(pair => pair.Value.Equals(row.EditorKey, StringComparison.OrdinalIgnoreCase));
    }

    private static DataGrid GroupGrid(ObservableCollection<ModelGroupEditorRow> rows)
    {
        var grid = new DataGrid
        {
            ItemsSource = rows,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Single,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            MinHeight = 240
        };
        PageSectionFactory.PolishGrid(grid);
        var textStyle = (Style)WpfApplication.Current.Resources["GridCellText"];
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("Runtimes.CustomRepo.NameLabel"),
            Binding = new WpfBinding(nameof(ModelGroupEditorRow.Name)),
            Width = new DataGridLength(1.4, DataGridLengthUnitType.Star),
            MinWidth = 130,
            IsReadOnly = true,
            ElementStyle = textStyle
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("ModelGroups.Column.Policy"),
            Binding = new WpfBinding(nameof(ModelGroupEditorRow.RetentionLabel)),
            Width = new DataGridLength(1.1, DataGridLengthUnitType.Star),
            MinWidth = 125,
            ElementStyle = textStyle
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("ModelGroups.Column.IdleMinutes"),
            Binding = new WpfBinding(nameof(ModelGroupEditorRow.IdleMinutesLabel)),
            Width = new DataGridLength(.7, DataGridLengthUnitType.Star),
            MinWidth = 90,
            ElementStyle = textStyle
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("ModelGroups.Column.Priority"),
            Binding = new WpfBinding(nameof(ModelGroupEditorRow.EvictionPriorityLabel)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star),
            MinWidth = 120,
            ElementStyle = textStyle
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("ModelGroups.Column.Profiles"),
            Binding = new WpfBinding(nameof(ModelGroupEditorRow.ProfileCount)),
            Width = new DataGridLength(.45, DataGridLengthUnitType.Star),
            MinWidth = 65,
            IsReadOnly = true,
            ElementStyle = textStyle
        });
        return grid;
    }

    private static Grid CompactToolbar(string title, params WpfButton[] actions)
    {
        var toolbar = new Grid { Margin = new Thickness(1, 1, 0, 4) };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition());
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            VerticalAlignment = VerticalAlignment.Center
        });
        var actionBar = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        foreach (var action in actions)
            actionBar.Children.Add(action);
        Grid.SetColumn(actionBar, 1);
        toolbar.Children.Add(actionBar);
        return toolbar;
    }

    private static Grid Layout()
    {
        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        return grid;
    }

    private static FrameworkElement Header(string title, string description)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"]
        });
        panel.Children.Add(new TextBlock
        {
            Text = description,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            Margin = new Thickness(0, 3, 0, 0)
        });
        return panel;
    }

    private static WpfWindow Dialog(WpfWindow owner, string title, double width, double height)
        => new()
        {
            Owner = owner,
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 460),
            MinHeight = Math.Min(height, 200),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            FlowDirection = owner.FlowDirection
        };

    private static WpfBorder Frame(UIElement child)
        => new()
        {
            Background = (WpfBrush)WpfApplication.Current.Resources["PanelBack"],
            BorderBrush = (WpfBrush)WpfApplication.Current.Resources["PanelBorderStrong"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(14),
            Child = child
        };

    private static WpfButton Button(string text, bool primary = false)
    {
        var button = new WpfButton
        {
            Content = text,
            MinWidth = 74,
            Height = 29,
            MinHeight = 29,
            Padding = new Thickness(10, 2, 10, 2),
            Margin = new Thickness(6, 0, 0, 0)
        };
        if (primary) VisualRole.SetButtonRole(button, VisualRole.Primary);
        return button;
    }

    private static string UniqueNewName(IEnumerable<ModelGroupEditorRow> rows)
    {
        var names = rows.Select(row => row.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 1; ; index++)
        {
            var name = index == 1 ? Loc.T("ModelGroups.DefaultName") : Loc.T("ModelGroups.DefaultNameIndexed", index);
            if (!names.Contains(name)) return name;
        }
    }

    private static string? Validate(IReadOnlyCollection<ModelGroupEditorRow> rows)
    {
        if (rows.Any(row => string.IsNullOrWhiteSpace(row.Name))) return Loc.T("ModelGroups.Validation.EveryNameRequired");
        if (rows.Any(row => row.Name.Trim().Length > 80)) return Loc.T("ModelGroups.Validation.NameLength");
        if (rows.GroupBy(row => row.Name.Trim(), StringComparer.OrdinalIgnoreCase).Any(group => group.Count() > 1))
            return Loc.T("ModelGroups.Validation.UniqueNames");
        if (rows.Any(row => row.IdleMinutes is < ModelGroupService.MinimumIdleMinutes or > ModelGroupService.MaximumIdleMinutes))
            return Loc.T("ModelGroups.Validation.IdleRange", ModelGroupService.MinimumIdleMinutes, ModelGroupService.MaximumIdleMinutes);
        return null;
    }

    public static string? ValidateProposedName(
        IReadOnlyCollection<ModelGroupEditorRow> rows,
        string proposedName,
        string exceptEditorKey = "")
    {
        if (string.IsNullOrWhiteSpace(proposedName)) return Loc.T("ModelGroups.Validation.NameRequired");
        if (proposedName.Trim().Length > 80) return Loc.T("ModelGroups.Validation.NameLength");
        if (rows.Any(row =>
                !row.EditorKey.Equals(exceptEditorKey, StringComparison.OrdinalIgnoreCase)
                && row.Name.Trim().Equals(proposedName.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Loc.T("ModelGroups.Validation.DuplicateName", proposedName.Trim());
        return null;
    }

    private static ModelGroupEditorRow Clone(ModelGroupEditorRow row)
        => new()
        {
            EditorKey = row.EditorKey,
            Id = row.Id,
            Name = row.Name.Trim(),
            RetentionMode = row.RetentionMode,
            IdleMinutes = row.IdleMinutes,
            EvictionPriority = row.EvictionPriority,
            ProfileCount = row.ProfileCount
        };

    private static void CopyValues(ModelGroupEditorRow source, ModelGroupEditorRow target)
    {
        target.Name = source.Name;
        target.RetentionMode = source.RetentionMode;
        target.IdleMinutes = source.IdleMinutes;
        target.EvictionPriority = source.EvictionPriority;
    }
}
