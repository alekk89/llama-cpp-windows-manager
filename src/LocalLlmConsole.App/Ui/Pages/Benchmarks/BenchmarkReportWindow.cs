using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using Button = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public static class BenchmarkReportWindow
{
    public static void Show(
        Window owner,
        string runName,
        string summary,
        IReadOnlyList<BenchmarkSpeedReportSection> sections)
    {
        var window = new Window
        {
            Title = $"{Loc.T("Nav.Benchmarks")} — {runName}",
            Owner = owner,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Width = 1040,
            Height = 760,
            MinWidth = 720,
            MinHeight = 480,
            ShowInTaskbar = false
        };
        window.SetResourceReference(Window.BackgroundProperty, "PanelBack");

        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 22) };
        body.Children.Add(Heading(runName, 22));
        var summaryBlock = Text(summary, "TextMuted");
        summaryBlock.Margin = new Thickness(0, 5, 0, 14);
        body.Children.Add(summaryBlock);

        if (sections.Count == 0)
        {
            var empty = Text(Loc.T("Benchmarks.Report.Empty"), "TextMuted");
            empty.Margin = new Thickness(0, 12, 0, 12);
            body.Children.Add(empty);
        }
        else
        {
            foreach (var section in sections)
                body.Children.Add(ChartSection(section));
        }

        var close = new Button
        {
            Content = Loc.T("Common.Close"),
            MinWidth = 92,
            Padding = new Thickness(12, 5, 12, 5),
            Margin = new Thickness(0, 8, 22, 16),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            IsCancel = true
        };
        close.Click += (_, _) => window.Close();

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.Children.Add(new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        });
        Grid.SetRow(close, 1);
        layout.Children.Add(close);
        window.Content = layout;
        window.ShowDialog();
    }

    private static Border ChartSection(BenchmarkSpeedReportSection section)
    {
        var title = SectionTitle(section.Kind);
        var content = new StackPanel { Margin = new Thickness(14, 12, 14, 14) };
        content.Children.Add(Heading(title, 15));

        var maximum = Math.Max(section.Bars.Max(bar => bar.TokensPerSecond), double.Epsilon);
        foreach (var bar in section.Bars)
            content.Children.Add(BarRow(bar, maximum));
        if (section.TotalBars > section.Bars.Count)
        {
            var omitted = Text($"{section.Bars.Count}/{section.TotalBars}", "TextMuted");
            omitted.Margin = new Thickness(0, 8, 0, 0);
            content.Children.Add(omitted);
        }

        var frame = new Border
        {
            Child = content,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 0, 0, 14)
        };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        AutomationProperties.SetName(frame, title);
        return frame;
    }

    private static string SectionTitle(BenchmarkSpeedReportKind kind) => kind switch
    {
        BenchmarkSpeedReportKind.PromptProcessing => Loc.T("Lifetime.Insight.PromptRate"),
        BenchmarkSpeedReportKind.Generation => Loc.T("Lifetime.Insight.GenerationRate"),
        _ => $"{Loc.T("Lifetime.Insight.PromptRate")} + {Loc.T("Lifetime.Insight.GenerationRate")}"
    };

    private static Grid BarRow(BenchmarkSpeedReportBar bar, double maximum)
    {
        var row = new Grid
        {
            Margin = new Thickness(0, 4, 0, 4),
            MinHeight = string.IsNullOrWhiteSpace(bar.ConfigurationLabel) ? 34 : 48
        };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });

        var labelStack = new StackPanel
        {
            Margin = new Thickness(0, 2, 12, 2),
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!string.IsNullOrWhiteSpace(bar.ConfigurationLabel))
        {
            var configuration = Text(bar.ConfigurationLabel, "AccentBlue");
            configuration.FontWeight = FontWeights.SemiBold;
            configuration.FontSize = 13;
            labelStack.Children.Add(configuration);
        }
        var label = Text(bar.Label, "TextSoft");
        label.FontSize = 11.5;
        labelStack.Children.Add(label);
        row.Children.Add(labelStack);

        var track = new Border
        {
            Height = 18,
            CornerRadius = new CornerRadius(3),
            VerticalAlignment = VerticalAlignment.Center
        };
        track.SetResourceReference(Border.BackgroundProperty, "PanelBackAlt");
        var fill = new Border
        {
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        fill.SetResourceReference(Border.BackgroundProperty, "AccentBlue");
        track.Child = fill;
        track.SizeChanged += (_, args) =>
            fill.Width = Math.Max(2, args.NewSize.Width * Math.Clamp(bar.TokensPerSecond / maximum, 0, 1));
        Grid.SetColumn(track, 1);
        row.Children.Add(track);

        var value = Text($"{bar.TokensPerSecond:0.00} tok/s", "TextMain");
        value.FontWeight = FontWeights.SemiBold;
        value.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        value.VerticalAlignment = VerticalAlignment.Center;
        value.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(value, 2);
        row.Children.Add(value);
        var accessibleLabel = string.IsNullOrWhiteSpace(bar.ConfigurationLabel)
            ? bar.Label
            : $"{bar.ConfigurationLabel}, {bar.Label}";
        AutomationProperties.SetName(row, $"{accessibleLabel}: {bar.TokensPerSecond:0.00} tokens per second");
        return row;
    }

    private static TextBlock Heading(string text, double size)
    {
        var block = Text(text, "TextMain");
        block.FontSize = size;
        block.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetHeadingLevel(block, size > 18 ? AutomationHeadingLevel.Level1 : AutomationHeadingLevel.Level2);
        return block;
    }

    private static TextBlock Text(string text, string foreground)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = 12.5,
            TextWrapping = TextWrapping.Wrap
        };
        block.SetResourceReference(TextBlock.ForegroundProperty, foreground);
        return block;
    }
}
