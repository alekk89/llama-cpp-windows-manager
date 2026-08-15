using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace LocalLlmConsole;

public static class HelpResultsFactory
{
    public static void Populate(
        StackPanel host,
        HelpSearchResult result,
        IReadOnlyDictionary<string, HelpSectionDefinition> sections,
        Action<string> navigate)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(navigate);

        host.Children.Clear();
        if (result.Articles.Count == 0)
        {
            host.Children.Add(EmptyState(result.Query));
            return;
        }

        foreach (var article in result.Articles)
        {
            var section = sections.TryGetValue(article.SectionKey, out var found)
                ? found
                : result.ActiveSection;
            host.Children.Add(ArticleCard(article, Loc.T(section.LabelKey), navigate));
        }
    }

    private static Border ArticleCard(
        HelpArticleDefinition article,
        string sectionLabel,
        Action<string> navigate)
    {
        var header = new StackPanel();
        var titleRow = new DockPanel();
        var category = Text(sectionLabel.ToUpperInvariant(), 10, muted: true);
        category.Margin = new Thickness(12, 2, 0, 0);
        DockPanel.SetDock(category, Dock.Right);
        titleRow.Children.Add(category);
        titleRow.Children.Add(Text(article.Title, 14, bold: true));
        header.Children.Add(titleRow);
        var summary = Text(article.Summary, 12, muted: true);
        summary.Margin = new Thickness(0, 2, 18, 0);
        header.Children.Add(summary);

        var body = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (var detail in article.Details)
        {
            var row = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var bullet = Text("•", 12, bold: true);
            bullet.Width = 18;
            DockPanel.SetDock(bullet, Dock.Left);
            row.Children.Add(bullet);
            row.Children.Add(Text(detail, 12));
            body.Children.Add(row);
        }

        if (article.Actions.Count > 0)
        {
            var actions = new WrapPanel { Margin = new Thickness(18, 4, 0, 0) };
            foreach (var action in article.Actions)
            {
                var button = new WpfButton
                {
                    Content = action.Label,
                    MinHeight = 28,
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(0, 0, 6, 4),
                    ToolTip = Loc.T("Help.Action.NavigateTooltip", action.Label)
                };
                AutomationProperties.SetName(button, action.Label);
                button.Click += (_, _) => navigate(action.Target);
                actions.Children.Add(button);
            }
            body.Children.Add(actions);
        }

        var expander = new Expander
        {
            Header = header,
            Content = body,
            Padding = new Thickness(12, 10, 12, 10),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(expander, article.Title);
        AutomationProperties.SetHelpText(expander, article.Summary);

        return new Border
        {
            Background = Brush("PanelBackAlt"),
            BorderBrush = Brush("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 0, 8),
            Child = expander
        };
    }

    private static Border EmptyState(string query)
    {
        var panel = new StackPanel { Margin = new Thickness(8) };
        panel.Children.Add(Text(Loc.T("Help.Search.NoMatchTitle"), 15, bold: true));
        panel.Children.Add(Text(
            string.IsNullOrWhiteSpace(query)
                ? Loc.T("Help.Search.NoMatchCategory")
                : Loc.T("Help.Search.NoMatchQuery"),
            12,
            muted: true));
        return new Border
        {
            Background = Brush("PanelBackAlt"),
            BorderBrush = Brush("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(16),
            Child = panel
        };
    }

    private static TextBlock Text(string value, int size, bool bold = false, bool muted = false)
        => new()
        {
            Text = value,
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(muted ? "TextMuted" : "TextMain"),
            TextWrapping = TextWrapping.Wrap
        };

    private static WpfBrush Brush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
