using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

/// <summary>Compact, keyboard-accessible one-click selector for supported usage periods.</summary>
public sealed class LifetimeRangeSelector : StackPanel
{
    private readonly Dictionary<UsageMetricsRange, WpfButton> _buttons = [];

    public LifetimeRangeSelector()
    {
        Orientation = System.Windows.Controls.Orientation.Horizontal;
        foreach (var choice in Choices())
        {
            var button = new WpfButton
            {
                Content = choice.ShortLabel,
                ToolTip = choice.FullLabel,
                MinWidth = choice.Range == UsageMetricsRange.All ? 44 : 38,
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(_buttons.Count == 0 ? 0 : 3, 0, 0, 0),
                Tag = choice.Range
            };
            AutomationProperties.SetName(button, choice.FullLabel);
            button.Click += RangeButton_Click;
            _buttons.Add(choice.Range, button);
            Children.Add(button);
        }
        SetRange(UsageMetricsRange.All, raiseEvent: false);
    }

    public event EventHandler? SelectionChanged;

    public UsageMetricsRange SelectedRange { get; private set; }

    public void SetRange(UsageMetricsRange range, bool raiseEvent = false)
    {
        SelectedRange = range;
        foreach (var (value, button) in _buttons)
        {
            var selected = value == range;
            button.Background = ResourceBrush(selected ? "AccentSoft" : "ControlBack");
            button.BorderBrush = ResourceBrush(selected ? "Accent" : "PanelBorderStrong");
            button.Foreground = ResourceBrush(selected ? "TextMain" : "TextMuted");
            button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            AutomationProperties.SetHelpText(button, selected ? Loc.T("Lifetime.Range.Selected") : "");
        }
        if (raiseEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RangeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: UsageMetricsRange range } || range == SelectedRange) return;
        SetRange(range, raiseEvent: true);
    }

    private static IReadOnlyList<(UsageMetricsRange Range, string ShortLabel, string FullLabel)> Choices()
        =>
        [
            (UsageMetricsRange.OneDay, "1D", Loc.T("Lifetime.Range.1Day")),
            (UsageMetricsRange.SevenDays, "7D", Loc.T("Lifetime.Range.7Days")),
            (UsageMetricsRange.ThirtyDays, "30D", Loc.T("Lifetime.Range.30Days")),
            (UsageMetricsRange.All, Loc.T("Lifetime.Range.AllShort"), Loc.T("Lifetime.Range.All"))
        ];

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
