using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public static partial class LaunchSettingsPanelFactory
{
    private static Grid LaunchSettingsGrid()
    {
        var grid = new Grid { Margin = new Thickness(0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(98) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        return grid;
    }

    private static Border LaunchSection(string title, Grid grid)
    {
        grid.Margin = new Thickness(0, 2, 0, 0);

        var header = new DockPanel
        {
            LastChildFill = true,
            Margin = new Thickness(0, 0, 0, 4)
        };
        header.Children.Add(new Border
        {
            Width = 3,
            Height = 17,
            Background = (WpfBrush)WpfApplication.Current.Resources["AccentStrong"],
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            VerticalAlignment = VerticalAlignment.Center
        });

        var section = new StackPanel();
        section.Children.Add(header);
        section.Children.Add(new Border
        {
            Height = 1,
            Background = (WpfBrush)WpfApplication.Current.Resources["PanelBorder"],
            Margin = new Thickness(0, 0, 0, 5)
        });
        section.Children.Add(grid);

        return new Border
        {
            Background = (WpfBrush)WpfApplication.Current.Resources["SurfaceRaised"],
            BorderBrush = (WpfBrush)WpfApplication.Current.Resources["PanelBorderStrong"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = section
        };
    }

    private static WrapPanel Bar()
        => new() { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

    private static WpfButton Button(string text, Func<Task> click)
    {
        var button = new WpfButton { Content = text };
        if (string.Equals(text, Loc.T("Launch.SaveForModelButton"), StringComparison.Ordinal)
            || string.Equals(text, Loc.T("Launch.SaveAsNewButton"), StringComparison.Ordinal))
            VisualRole.SetButtonRole(button, VisualRole.Primary);
        else if (string.Equals(text, Loc.T("Launch.ResetDefaultsButton"), StringComparison.Ordinal))
            VisualRole.SetButtonRole(button, VisualRole.Danger);
        button.ToolTip = TooltipText(ButtonToolTip(text));
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += async (_, _) => await click();
        return button;
    }

    private static string ButtonToolTip(string text)
    {
        var t = (text ?? "").Trim();
        if (string.Equals(t, Loc.T("Launch.SaveForModelButton"))) return Loc.T("Tooltip.SaveForModel");
        if (string.Equals(t, Loc.T("Launch.SaveAsDefaultButton"))) return Loc.T("Tooltip.SaveAsDefault");
        if (string.Equals(t, Loc.T("Launch.ResetDefaultsButton"))) return Loc.T("Tooltip.ResetDefaults");
        if (string.Equals(t, Loc.T("Launch.SaveAsNewButton"))) return Loc.T("Tooltip.SaveAsNewButton");
        if (string.Equals(t, "Choose")) return "Choose a GGUF file.";
        return string.IsNullOrWhiteSpace(t) ? "" : $"Run {t}.";
    }

    private static ScrollViewer Scroll(UIElement child, Thickness? padding = null)
    {
        var viewer = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var content = new Border { Padding = padding ?? new Thickness(16), Child = child };
        content.SetBinding(FrameworkElement.WidthProperty, new System.Windows.Data.Binding(nameof(ScrollViewer.ViewportWidth)) { Source = viewer });
        viewer.Content = content;
        viewer.Loaded += (_, _) => viewer.Dispatcher.BeginInvoke(new Action(viewer.ScrollToTop), System.Windows.Threading.DispatcherPriority.ContextIdle);
        return viewer;
    }

    private static TextBlock Text(string text, int size = 13, bool bold = false, bool muted = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = muted ? (WpfBrush)WpfApplication.Current.Resources["TextMuted"] : (WpfBrush)WpfApplication.Current.Resources["TextMain"],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, size >= 18 ? 10 : 0, 0, size >= 18 ? 10 : 8)
    };

    private static string TooltipText(string text) => text;

    private sealed class LaunchSettingsPanelBuilder(
        Dictionary<string, List<FrameworkElement>> launchSettingElements,
        HashSet<string> advancedLaunchSettingLabels,
        List<LaunchSettingsSectionElements> launchSettingSections,
        List<FrameworkElement> advancedLaunchSections)
    {
        private readonly Dictionary<Grid, List<string>> _sectionLabelsByGrid = new();

        public void AddLaunchSetting(Grid grid, string label, FrameworkElement control, string? tooltip = null)
        {
            var tooltipText = TooltipText(string.IsNullOrWhiteSpace(tooltip)
                ? LaunchSettingMetadataService.Tooltip(label)
                : tooltip);
            control.ToolTip = tooltipText;
            var index = grid.Children.Count / 2;
            var row = index / 2;
            var rightSide = index % 2 == 1;
            if (!rightSide) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var labelText = new TextBlock
            {
                Text = label,
                Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 7, 1),
                ToolTip = tooltipText
            };
            Grid.SetRow(labelText, row);
            Grid.SetColumn(labelText, rightSide ? 3 : 0);
            grid.Children.Add(labelText);
            control.Height = 28;
            control.MinHeight = 28;
            control.MinWidth = Math.Max(control.MinWidth, 72);
            control.Margin = new Thickness(0, 0, 4, 1);
            control.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(control, row);
            Grid.SetColumn(control, rightSide ? 4 : 1);
            grid.Children.Add(control);
            launchSettingElements[label] = new List<FrameworkElement> { labelText, control };
            if (!_sectionLabelsByGrid.TryGetValue(grid, out var labels))
            {
                labels = [];
                _sectionLabelsByGrid[grid] = labels;
            }

            labels.Add(label);
        }

        public void AddAdvancedLaunchSetting(Grid grid, string label, FrameworkElement control, string? tooltip = null)
        {
            AddLaunchSetting(grid, label, control, tooltip);
            advancedLaunchSettingLabels.Add(label);
            if (launchSettingElements.TryGetValue(label, out var elements))
                advancedLaunchSections.AddRange(elements);
        }

        public void AddSection(string title, FrameworkElement section, Grid grid, bool isAdvancedSection = false)
        {
            var labels = _sectionLabelsByGrid.TryGetValue(grid, out var sectionLabels)
                ? sectionLabels.ToArray()
                : [];
            launchSettingSections.Add(new LaunchSettingsSectionElements(title, section, labels, isAdvancedSection));
        }

        public void AddAdvancedSection(FrameworkElement section)
            => advancedLaunchSections.Add(section);
    }
}
