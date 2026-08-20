using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace LocalLlmConsole;

public sealed record LifetimeInsightControls(
    TextBlock Requests,
    TextBlock RequestsDetail,
    TextBlock ActiveDays,
    TextBlock AveragePerActiveDay,
    TextBlock PromptRate,
    TextBlock GenerationRate,
    TextBlock PeakDay,
    TextBlock PeakDayDetail);

public static class LifetimeInsightsPanel
{
    public static Border Create(out LifetimeInsightControls controls)
    {
        var grid = new Grid();
        for (var index = 0; index < 6; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        grid.Children.Add(Item(Loc.T("Lifetime.Insight.Requests"), 0, out var requests, out var requestsDetail));
        grid.Children.Add(Item(Loc.T("Lifetime.Insight.ActiveDays"), 1, out var activeDays, out _));
        grid.Children.Add(Item(Loc.T("Lifetime.Insight.AverageDay"), 2, out var averageDay, out _));
        grid.Children.Add(Item(Loc.T("Lifetime.Insight.PromptRate"), 3, out var promptRate, out _));
        grid.Children.Add(Item(Loc.T("Lifetime.Insight.GenerationRate"), 4, out var generationRate, out _));
        grid.Children.Add(Item(Loc.T("Lifetime.Insight.PeakDay"), 5, out var peakDay, out var peakDetail));

        controls = new LifetimeInsightControls(
            requests,
            requestsDetail,
            activeDays,
            averageDay,
            promptRate,
            generationRate,
            peakDay,
            peakDetail);
        return new Border
        {
            Style = (Style)WpfApplication.Current.Resources["Panel"],
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 0, 0, 10),
            Child = grid
        };
    }

    private static Border Item(
        string label,
        int column,
        out TextBlock value,
        out TextBlock detail)
    {
        var stack = new StackPanel { Margin = new Thickness(10, 1, 10, 1) };
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 10,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        value = new TextBlock
        {
            Text = "…",
            Foreground = ResourceBrush("TextMain"),
            FontFamily = new WpfFontFamily("Cascadia Mono, Consolas, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = 15,
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        detail = new TextBlock
        {
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 9.5,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        stack.Children.Add(value);
        stack.Children.Add(detail);
        var border = new Border
        {
            BorderBrush = ResourceBrush("PanelBorder"),
            BorderThickness = new Thickness(column == 0 ? 0 : 1, 0, 0, 0),
            Child = stack
        };
        Grid.SetColumn(border, column);
        return border;
    }

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
