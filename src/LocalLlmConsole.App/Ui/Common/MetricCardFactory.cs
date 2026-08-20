using System.Windows;
using System.Windows.Controls;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace LocalLlmConsole;

public static class MetricCardFactory
{
    private const double MetricCardHeight = 104;
    private const double GraphValueBandHeight = 31;
    public static Grid AddMetric(Grid grid, string label, int row, int column)
        => AddMetric(grid, label, row, column, includeProgress: false, out _, out _);

    public static Grid AddMetric(Grid grid, string label, int row, int column, out TextBlock lastKnown)
        => AddMetric(grid, label, row, column, includeProgress: false, out _, out lastKnown);

    public static Grid AddMetricGraph(
        Grid grid,
        string label,
        int row,
        int column,
        out MetricSparkline graph,
        out TextBlock lastKnown,
        string primaryBrushKey = "AccentBlue",
        string secondaryBrushKey = "Accent",
        double? fixedMaximum = null)
    {
        var metric = AddMetric(grid, label, row, column, out lastKnown);
        // Reserve the same two-line value band in every graph card. Without this,
        // one-line states such as "Inactive" move the sparkline upward while the
        // two-line KV-cache summary leaves it lower in the card.
        metric.Height = GraphValueBandHeight;
        graph = new MetricSparkline
        {
            Height = 28,
            Margin = new Thickness(0, 2, 0, 0),
            PrimaryBrushKey = primaryBrushKey,
            SecondaryBrushKey = secondaryBrushKey,
            FixedMaximum = fixedMaximum,
            ToolTip = $"{label} history (latest 60 samples)"
        };
        if (metric.Parent is StackPanel stack)
            stack.Children.Add(graph);
        return metric;
    }

    public static Grid AddMetric(Grid grid, string label, int row, int column, bool includeProgress, out WpfProgressBar? progress)
        => AddMetric(grid, label, row, column, includeProgress, out progress, out _);

    /// <summary>Creates a metric card with an English Loc key for internal comparisons.</summary>
    public static Grid AddMetric(Grid grid, string label, int row, int column, string labelKey)
        => AddMetric(grid, label, row, column, includeProgress: false, out _, out _, labelKey);

    /// <summary>Creates a metric card with an English Loc key for internal comparisons.</summary>
    public static Grid AddMetric(Grid grid, string label, int row, int column, out TextBlock lastKnown, string labelKey)
        => AddMetric(grid, label, row, column, includeProgress: false, out _, out lastKnown, labelKey);

    /// <summary>Creates a metric card. <paramref name="labelKey"/> is the English Loc key used for internal comparisons (e.g., "Overview.Metric.ModelStatus").</summary>
    public static Grid AddMetric(
        Grid grid,
        string label,
        int row,
        int column,
        bool includeProgress,
        out WpfProgressBar? progress,
        out TextBlock lastKnown,
        string labelKey = "")
    {
        progress = null;
        var card = new Border
        {
            Style = (Style)WpfApplication.Current.Resources["MetricCard"],
            Height = MetricCardHeight,
            ClipToBounds = true,
            Margin = new Thickness(column == 0 ? 0 : 5, 0, column == 0 ? 5 : 0, 8)
        };
        var stack = new StackPanel();
        var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var labelText = new TextBlock
        {
            Text = label,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        header.Children.Add(labelText);
        lastKnown = new TextBlock
        {
            FontSize = 10,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            MaxWidth = 150,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(lastKnown, 1);
        header.Children.Add(lastKnown);
        stack.Children.Add(header);
        var valueRows = new Grid { Tag = string.IsNullOrEmpty(labelKey) ? label : labelKey };
        valueRows.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MetricLabelColumnWidth(label)) });
        valueRows.ColumnDefinitions.Add(new ColumnDefinition());
        SetMetricText(valueRows, "...");
        stack.Children.Add(valueRows);
        if (includeProgress)
        {
            progress = new WpfProgressBar
            {
                Height = 4,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 5, 0, 0)
            };
            stack.Children.Add(progress);
        }
        card.Child = stack;
        Grid.SetRow(card, row);
        Grid.SetColumn(card, column);
        grid.Children.Add(card);
        return valueRows;
    }

    public static void SetMetricText(Grid? target, string value, bool emphasizeLoadedStatus = false)
        => MetricCardRenderer.SetMetricText(target, value, emphasizeLoadedStatus);

    public static void SetLastKnownMetricText(TextBlock? target, DateTimeOffset capturedAt, DateTimeOffset now)
        => MetricCardRenderer.SetLastKnownMetricText(target, capturedAt, now);

    public static void ClearLastKnownMetricText(TextBlock? target)
        => MetricCardRenderer.ClearLastKnownMetricText(target);

    public static (string Label, string Value) SplitMetricLine(string line)
        => MetricCardRenderer.SplitMetricLine(line);

    public static bool IsNeutralMetricStatus(string text)
        => MetricCardRenderer.IsNeutralMetricStatus(text);

    private static double MetricLabelColumnWidth(string label)
        => MetricCardRenderer.MetricLabelColumnWidth(label);
}
