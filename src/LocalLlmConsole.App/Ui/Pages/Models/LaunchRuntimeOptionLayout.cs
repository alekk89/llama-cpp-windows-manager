using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Services;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;

namespace LocalLlmConsole;

internal static class LaunchRuntimeOptionLayout
{
    private const double EditorHeight = 28;

    public static Grid CreateGroupRowsGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        return grid;
    }

    public static void ReflowGroup(RuntimeOptionGroupView group)
    {
        group.RowsGrid.RowDefinitions.Clear();
        var visibleIndex = 0;
        foreach (var optionRow in group.Rows.Where(candidate => candidate.Row.Visibility == Visibility.Visible))
        {
            var row = visibleIndex / 2;
            if (visibleIndex % 2 == 0)
                group.RowsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(optionRow.Row, row);
            Grid.SetColumn(optionRow.Row, visibleIndex % 2 == 0 ? 0 : 2);
            visibleIndex++;
        }
    }

    public static Border CreateGroup(string title, FrameworkElement rows)
    {
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new Border
        {
            Width = 3,
            Height = 17,
            Background = ResourceBrush("AccentStrong"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = ResourceBrush("PanelBorder"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        content.Children.Add(rows);
        return new Border
        {
            Background = ResourceBrush("SurfaceRaised"),
            BorderBrush = ResourceBrush("PanelBorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = content
        };
    }

    public static Grid CreateRow(RuntimeLaunchOptionDefinition option, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2), MinHeight = EditorHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock
        {
            Text = LaunchSettingMetadataService.RuntimeOptionLabel(RuntimeLaunchOptionSwitchService.DisplayFlag(option)),
            Foreground = ResourceBrush("TextSoft"),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = OptionToolTip(option)
        });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    public static string OptionToolTip(RuntimeLaunchOptionDefinition option)
    {
        var description = string.IsNullOrWhiteSpace(option.Description) ? option.Name : option.Description;
        var advertisedDefault = string.IsNullOrWhiteSpace(option.DefaultValue) ? "" : $"Default: {option.DefaultValue}";
        var inheritance = string.IsNullOrWhiteSpace(advertisedDefault)
            ? "Leave unchanged to inherit the runtime default."
            : $"{advertisedDefault}. Leave unchanged to inherit it.";
        var aliases = string.Join(", ", option.Aliases.Where(alias => alias.StartsWith("--", StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase));
        return $"{LaunchSettingMetadataService.RuntimeOptionLabel(RuntimeLaunchOptionSwitchService.DisplayFlag(option))} ({aliases}){Environment.NewLine}{description}{Environment.NewLine}{inheritance}";
    }

    public static string SearchText(RuntimeLaunchOptionDefinition option)
        => $"{LaunchSettingMetadataService.RuntimeOptionLabel(RuntimeLaunchOptionSwitchService.DisplayFlag(option))} {option.Name} {string.Join(" ", option.Aliases)} {option.ValueHint} {option.Description} {option.DefaultValue}";

    private static WpfBrush ResourceBrush(string key) => (WpfBrush)WpfApplication.Current.Resources[key];
}

internal sealed record RuntimeOptionRow(FrameworkElement Row, string SearchText);

internal sealed record RuntimeOptionGroupView(
    string Title,
    FrameworkElement Root,
    Grid RowsGrid,
    List<RuntimeOptionRow> Rows);
