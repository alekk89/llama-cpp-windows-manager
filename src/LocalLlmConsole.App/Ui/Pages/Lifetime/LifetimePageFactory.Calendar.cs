using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WpfApplication = System.Windows.Application;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public static partial class LifetimePageFactory
{
    private static Border BuildCalendarSection(
        ScrollViewer calendarScroller,
        WpfComboBox calendarMetric,
        TextBlock historyNote,
        RoutedEventHandler clearSelectionClick,
        out TextBlock selectionSummary,
        out WpfButton clearSelection)
    {
        var stack = new StackPanel();
        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("Lifetime.DailyHistoryTitle"),
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain")
        });
        var selectionPanel = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(12, 0, 14, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        selectionSummary = new TextBlock
        {
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 10.5,
            VerticalAlignment = VerticalAlignment.Center
        };
        clearSelection = new WpfButton
        {
            Content = Loc.T("Lifetime.Selection.Clear"),
            ToolTip = Loc.T("Lifetime.Selection.ClearTooltip"),
            MinHeight = 32,
            Padding = new Thickness(7, 2, 7, 2),
            Margin = new Thickness(7, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        clearSelection.Click += clearSelectionClick;
        selectionPanel.Children.Add(selectionSummary);
        selectionPanel.Children.Add(clearSelection);
        Grid.SetColumn(selectionPanel, 1);
        header.Children.Add(selectionPanel);
        Grid.SetColumn(calendarMetric, 2);
        header.Children.Add(calendarMetric);
        var legend = BuildIntensityLegend();
        Grid.SetColumn(legend, 3);
        header.Children.Add(legend);
        stack.Children.Add(header);
        stack.Children.Add(calendarScroller);
        stack.Children.Add(historyNote);
        return new Border
        {
            Style = (Style)WpfApplication.Current.Resources["Panel"],
            Margin = new Thickness(0, 0, 0, 10),
            Child = stack
        };
    }

    private static StackPanel BuildIntensityLegend()
    {
        var stack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        stack.Children.Add(new TextBlock { Text = Loc.T("Lifetime.Calendar.Less"), FontSize = 10, Foreground = ResourceBrush("TextMuted"), Margin = new Thickness(0, 1, 5, 0) });
        foreach (var opacity in new[] { .28, .48, .72, 1d })
        {
            var brush = ResourceBrush("AccentBlue").Clone();
            brush.Opacity = opacity;
            stack.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(2), Background = brush, Margin = new Thickness(2, 1, 0, 0) });
        }
        stack.Children.Add(new TextBlock { Text = Loc.T("Lifetime.Calendar.More"), FontSize = 10, Foreground = ResourceBrush("TextMuted"), Margin = new Thickness(6, 1, 0, 0) });
        return stack;
    }

    private static WpfComboBox BuildCalendarMetricSelector(UsageCalendarMetric selected)
    {
        var options = new[]
        {
            new LifetimeCalendarMetricOption(UsageCalendarMetric.TotalTokens, Loc.T("Lifetime.CalendarMetric.Total")),
            new LifetimeCalendarMetricOption(UsageCalendarMetric.InputTokens, Loc.T("Lifetime.CalendarMetric.Input")),
            new LifetimeCalendarMetricOption(UsageCalendarMetric.GeneratedTokens, Loc.T("Lifetime.CalendarMetric.Output")),
            new LifetimeCalendarMetricOption(UsageCalendarMetric.CachedPromptTokens, Loc.T("Lifetime.CalendarMetric.Cached")),
            new LifetimeCalendarMetricOption(UsageCalendarMetric.Requests, Loc.T("Lifetime.CalendarMetric.Requests"))
        };
        var combo = new WpfComboBox
        {
            ItemsSource = options,
            SelectedItem = options.First(option => option.Metric == selected),
            MinWidth = 92,
            Margin = new Thickness(0, 0, 12, 0),
            Padding = new Thickness(7, 2, 7, 2),
            ToolTip = Loc.T("Lifetime.CalendarMetric.Tooltip"),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(combo, Loc.T("Lifetime.CalendarMetric.AutomationName"));
        return combo;
    }
}
