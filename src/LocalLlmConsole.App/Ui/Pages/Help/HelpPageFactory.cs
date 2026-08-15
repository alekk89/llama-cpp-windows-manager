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
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed record HelpPageActions(
    Action<string> SelectSection,
    Action<string> SearchChanged);

public sealed record HelpPageRequest(
    HelpSectionDefinition ActiveSection,
    IReadOnlyList<HelpSectionDefinition> Sections,
    HelpPageActions Actions);

public sealed record HelpPageControls(
    WpfTextBox SearchBox,
    WpfButton ClearSearchButton,
    TextBlock SearchHint,
    TextBlock ResultsSummary,
    StackPanel ResultsHost,
    IReadOnlyDictionary<string, WpfButton> SectionButtons);

public sealed record HelpPageBuildResult(
    Grid Content,
    HelpPageControls Controls);

public static class HelpPageFactory
{
    public static HelpPageBuildResult Create(HelpPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ActiveSection);
        ArgumentNullException.ThrowIfNull(request.Sections);
        ArgumentNullException.ThrowIfNull(request.Actions);
        ArgumentNullException.ThrowIfNull(request.Actions.SelectSection);
        ArgumentNullException.ThrowIfNull(request.Actions.SearchChanged);

        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());

        var header = Header();
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var search = SearchBar(request.Actions.SearchChanged, out var searchBox, out var clearButton, out var hint);
        Grid.SetRow(search, 1);
        root.Children.Add(search);

        var categories = CategoryBar(request, out var sectionButtons);
        Grid.SetRow(categories, 2);
        root.Children.Add(categories);

        var resultsSummary = Text("", 12, muted: true);
        resultsSummary.Margin = new Thickness(2, 0, 0, 8);
        AutomationProperties.SetLiveSetting(resultsSummary, AutomationLiveSetting.Polite);
        AutomationProperties.SetName(resultsSummary, Loc.T("Help.Search.ResultsAutomationName"));

        var resultsHost = new StackPanel { Margin = new Thickness(0, 0, 8, 12) };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = resultsHost
        };
        var resultsArea = new DockPanel();
        DockPanel.SetDock(resultsSummary, Dock.Top);
        resultsArea.Children.Add(resultsSummary);
        resultsArea.Children.Add(scroll);
        Grid.SetRow(resultsArea, 3);
        root.Children.Add(resultsArea);

        return new HelpPageBuildResult(
            root,
            new HelpPageControls(searchBox, clearButton, hint, resultsSummary, resultsHost, sectionButtons));
    }

    private static StackPanel Header()
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(Text(Loc.T("Nav.Help"), 24, bold: true));
        var summary = Text(
            Loc.T("Help.Page.Intro"),
            13,
            muted: true);
        summary.Margin = new Thickness(0);
        panel.Children.Add(summary);
        return panel;
    }

    private static Border SearchBar(
        Action<string> searchChanged,
        out WpfTextBox searchBox,
        out WpfButton clearButton,
        out TextBlock hint)
    {
        var layout = new Grid();
        layout.ColumnDefinitions.Add(new ColumnDefinition());
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var inputHost = new Grid();
        var input = new WpfTextBox
        {
            Height = 34,
            MinHeight = 34,
            Padding = new Thickness(10, 3, 10, 3),
            VerticalContentAlignment = VerticalAlignment.Center,
            Foreground = Brush("TextMain"),
            Background = Brush("InputBack"),
            BorderBrush = Brush("PanelBorderStrong"),
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 13,
            ToolTip = Loc.T("Help.Search.Tooltip")
        };
        AutomationProperties.SetName(input, Loc.T("Help.Search.AutomationName"));
        AutomationProperties.SetHelpText(input, Loc.T("Help.Search.AutomationHelp"));

        var placeholder = Text(Loc.T("Help.Search.Placeholder"), 13, muted: true);
        placeholder.Margin = new Thickness(11, 0, 8, 0);
        placeholder.VerticalAlignment = VerticalAlignment.Center;
        placeholder.IsHitTestVisible = false;

        inputHost.Children.Add(input);
        inputHost.Children.Add(placeholder);
        layout.Children.Add(inputHost);

        var clear = new WpfButton
        {
            Content = Loc.T("Common.ClearButton"),
            MinWidth = 64,
            Height = 34,
            Margin = new Thickness(8, 0, 0, 0),
            Visibility = Visibility.Collapsed,
            ToolTip = Loc.T("Help.Search.ClearTooltip")
        };
        AutomationProperties.SetName(clear, Loc.T("Help.Search.ClearAutomationName"));
        clear.Click += (_, _) => input.Clear();
        Grid.SetColumn(clear, 1);
        layout.Children.Add(clear);

        input.TextChanged += (_, _) =>
        {
            var hasText = !string.IsNullOrWhiteSpace(input.Text);
            placeholder.Visibility = hasText ? Visibility.Collapsed : Visibility.Visible;
            clear.Visibility = hasText ? Visibility.Visible : Visibility.Collapsed;
            searchChanged(input.Text);
        };

        searchBox = input;
        clearButton = clear;
        hint = placeholder;
        return new Border
        {
            Background = Brush("PanelBackAlt"),
            BorderBrush = Brush("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = layout
        };
    }

    private static WrapPanel CategoryBar(
        HelpPageRequest request,
        out IReadOnlyDictionary<string, WpfButton> buttons)
    {
        var result = new Dictionary<string, WpfButton>(StringComparer.Ordinal);
        var panel = new WrapPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        foreach (var section in request.Sections)
        {
            var button = new WpfButton
            {
                Content = Loc.T(section.LabelKey),
                MinHeight = 30,
                Padding = new Thickness(12, 4, 12, 4),
                Margin = new Thickness(0, 0, 6, 6),
                ToolTip = Loc.T(section.SummaryKey)
            };
            AutomationProperties.SetName(button, Loc.T("Help.Section.AutomationName", Loc.T(section.LabelKey)));
            AutomationProperties.SetHelpText(button, Loc.T(section.SummaryKey));
            button.Click += (_, _) => request.Actions.SelectSection(section.Key);
            if (string.Equals(section.Key, request.ActiveSection.Key, StringComparison.Ordinal))
                button.Tag = "Active";
            result[section.Key] = button;
            panel.Children.Add(button);
        }
        buttons = result;
        return panel;
    }

    private static TextBlock Text(string value, int size, bool bold = false, bool muted = false)
        => new()
        {
            Text = value,
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = size,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
            Foreground = Brush(muted ? "TextMuted" : "TextMain"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };

    private static WpfBrush Brush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
