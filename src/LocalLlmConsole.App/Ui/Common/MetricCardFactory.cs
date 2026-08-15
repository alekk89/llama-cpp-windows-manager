using System.Text.RegularExpressions;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfProgressBar = System.Windows.Controls.ProgressBar;

namespace LocalLlmConsole;

public static class MetricCardFactory
{
    private const double MetricCardHeight = 104;
    private const double GraphValueBandHeight = 31;
    private static readonly Regex MetricImportantValuePattern = new(
        @"\d[\d,]*(?:\.\d+)?(?:/\d[\d,]*(?:\.\d+)?)?\s*(?:t/s|/s|avg|%|\u00b0?C|GiB|GB|MiB|tokens?|t)?",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly System.Windows.Media.FontFamily MetricValueFont = new("Cascadia Mono, Consolas, Segoe UI");
    private static readonly ConditionalWeakTable<Grid, MetricRenderState> RenderStates = new();

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
    {
        if (target is null) return;

        var normalizedValue = string.IsNullOrWhiteSpace(value) ? "..." : value.TrimEnd();
        var renderState = RenderStates.GetOrCreateValue(target);
        if (string.Equals(renderState.Value, normalizedValue, StringComparison.Ordinal)
            && renderState.EmphasizeLoadedStatus == emphasizeLoadedStatus)
            return;
        renderState.Value = normalizedValue;
        renderState.EmphasizeLoadedStatus = emphasizeLoadedStatus;

        target.Children.Clear();
        target.RowDefinitions.Clear();
        var lines = normalizedValue
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.None);

        for (var i = 0; i < lines.Length; i++)
        {
            target.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (TryAddStatusNameMetricLine(target, lines[i], emphasizeLoadedStatus, i))
                continue;

            if (MetricShouldRenderNeutralStatus(target, lines[i]))
            {
                AddSpanningMetricBlock(target, MetricPlainValueBlock(lines[i].Trim(), compact: false), i);
                continue;
            }

            if (MetricShouldEmphasizeWholeLine(target, lines[i], emphasizeLoadedStatus))
            {
                AddSpanningMetricBlock(target, MetricValueBlock(lines[i].Trim(), compact: false, emphasizeWholeLine: true), i);
                continue;
            }

            var (label, metricValue) = SplitMetricLine(lines[i]);
            if (!string.IsNullOrWhiteSpace(label))
            {
                if (IsGraphMetricTarget(target))
                {
                    AddSpanningMetricBlock(target, MetricLabeledValueBlock(label, metricValue), i);
                    continue;
                }

                var labelBlock = new TextBlock
                {
                    Text = label,
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 7, 1)
                };
                Grid.SetRow(labelBlock, i);
                Grid.SetColumn(labelBlock, 0);
                target.Children.Add(labelBlock);

                var valueBlock = MetricValueBlock(metricValue, compact: true);
                Grid.SetRow(valueBlock, i);
                Grid.SetColumn(valueBlock, 1);
                target.Children.Add(valueBlock);
            }
            else
            {
                AddSpanningMetricBlock(target, MetricValueBlock(metricValue, compact: false), i);
            }
        }
    }

    public static void SetLastKnownMetricText(TextBlock? target, DateTimeOffset capturedAt, DateTimeOffset now)
    {
        if (target is null) return;

        var age = now <= capturedAt ? Loc.T("Metrics.JustNow") : DisplayFormatService.Elapsed(now - capturedAt);
        target.Text = Loc.T("Metrics.LastKnownAgo", age);
        target.ToolTip = Loc.T("Tooltip.MetricsLastKnown");
        target.Visibility = Visibility.Visible;
    }

    public static void ClearLastKnownMetricText(TextBlock? target)
    {
        if (target is null) return;

        target.Text = "";
        target.ToolTip = null;
        target.Visibility = Visibility.Collapsed;
    }

    public static (string Label, string Value) SplitMetricLine(string line)
    {
        var text = line.Trim();
        if (string.IsNullOrWhiteSpace(text)) return ("", "");

        var colon = text.IndexOf(':');
        if (colon > 0 && colon < 16)
            return (text[..colon].Trim(), text[(colon + 1)..].Trim());

        foreach (var label in new[] { "KV cache", "Context", "Accepted", "Prompt", "Micro", "Batch", "Cont", "Gen" })
        {
            if (text.StartsWith(label + " ", StringComparison.Ordinal))
                return (label, text[label.Length..].Trim());
        }

        return ("", text);
    }

    private static double MetricLabelColumnWidth(string label)
        => string.Equals(label, Loc.T("Overview.Metric.ModelStatus"), StringComparison.Ordinal)
            || string.Equals(label, "Overview.Metric.ModelStatus", StringComparison.Ordinal)
            ? 82
            : 64;

    public static bool IsNeutralMetricStatus(string text)
    {
        var normalized = text.Trim();
        return string.Equals(normalized, "None", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Stopped", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Loading", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Loaded", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Warm", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Unavailable", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Unknown runtime", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "Unknown model", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "No runtime", StringComparison.OrdinalIgnoreCase)
               || string.Equals(normalized, "No loaded runtime", StringComparison.OrdinalIgnoreCase)
               || normalized.StartsWith("Failed", StringComparison.OrdinalIgnoreCase);
    }

    private static bool MetricShouldRenderNeutralStatus(Grid target, string line)
    {
        if (target.Tag is not string label || !IsStatusNameMetricLabel(label)) return false;
        var text = line.Trim();
        return !string.IsNullOrWhiteSpace(text) && IsNeutralMetricStatus(text);
    }

    private static bool TryAddStatusNameMetricLine(Grid target, string line, bool emphasizeLoadedStatus, int row)
    {
        if (target.Tag is not string label) return false;
        var text = line.Trim();
        if (string.IsNullOrWhiteSpace(text)) return false;
        if (!IsStatusNameMetricLabel(label)) return false;

        if (IsNeutralMetricStatus(text))
        {
            AddSpanningMetricBlock(target, MetricPlainValueBlock(text, compact: false), row);
            return true;
        }

        if (IsStatusNameMetricLabel(label)
            && TrySplitModelStatusName(text, out var statusPrefix, out var modelName))
        {
            AddSpanningMetricBlock(target, MetricStatusNameBlock(statusPrefix, modelName), row);
            return true;
        }

        if (IsStatusNameMetricLabel(label) && row == 0)
        {
            AddSpanningMetricBlock(target, MetricPlainValueBlock(text, compact: false), row);
            return true;
        }

        return false;
    }

    private static void AddSpanningMetricBlock(Grid target, UIElement block, int row)
    {
        Grid.SetRow(block, row);
        Grid.SetColumn(block, 0);
        Grid.SetColumnSpan(block, 2);
        target.Children.Add(block);
    }

    private static bool TrySplitModelStatusName(string text, out string statusPrefix, out string modelName)
    {
        statusPrefix = "";
        modelName = "";

        foreach (var prefix in new[] { "Loaded Model:", "Loading Model:", "Loaded:", "Loading:", "Warm:", "Stopped:" })
        {
            if (!text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            var separator = prefix.Length;
            var remainder = text[separator..].TrimStart();
            if (string.IsNullOrWhiteSpace(remainder)) return false;

            statusPrefix = $"{text[..separator].TrimEnd(':')} ";
            modelName = remainder;
            return true;
        }

        return false;
    }

    private static bool IsStatusNameMetricLabel(string tag)
        => string.Equals(tag, Loc.T("Overview.Metric.ModelStatus"), StringComparison.Ordinal)
           || string.Equals(tag, "Overview.Metric.ModelStatus", StringComparison.Ordinal);

    private static bool IsGraphMetricTarget(Grid target)
        => target.Tag is string label
           && (string.Equals(label, Loc.T("Overview.Metric.Tokens"), StringComparison.Ordinal)
               || string.Equals(label, Loc.T("Overview.Metric.MtpTokens"), StringComparison.Ordinal)
               || string.Equals(label, Loc.T("Overview.Metric.KvCache"), StringComparison.Ordinal));

    private static bool MetricShouldEmphasizeWholeLine(Grid target, string line, bool emphasizeLoadedStatus)
    {
        if (target.Tag is not string label) return false;
        var text = line.Trim();
        if (string.IsNullOrWhiteSpace(text) || IsNeutralMetricStatus(text)) return false;

        if (IsStatusNameMetricLabel(label))
            return false;

        return false;
    }

    private static TextBlock MetricPlainValueBlock(string text, bool compact)
    {
        var block = new TextBlock
        {
            FontSize = compact ? 11.5 : 12,
            FontWeight = FontWeights.Medium,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 14,
            Margin = new Thickness(0, 0, 0, 1),
            ToolTip = text
        };
        block.Inlines.Add(new Run(string.IsNullOrWhiteSpace(text) ? "..." : text));
        return block;
    }

    private static TextBlock MetricStatusNameBlock(string statusPrefix, string emphasizedName)
    {
        var block = new TextBlock
        {
            FontSize = 12,
            FontWeight = FontWeights.Medium,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 14,
            Margin = new Thickness(0, 0, 0, 1),
            ToolTip = $"{statusPrefix}{emphasizedName}".Trim()
        };
        if (!string.IsNullOrWhiteSpace(statusPrefix))
        {
            block.Inlines.Add(new Run(statusPrefix)
            {
                Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"]
            });
        }

        block.Inlines.Add(new Run(emphasizedName)
        {
            FontWeight = FontWeights.Bold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"]
        });
        return block;
    }

    private static TextBlock MetricLabeledValueBlock(string label, string value)
    {
        var text = new TextBlock
        {
            FontSize = 11.5,
            FontWeight = FontWeights.Medium,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 14,
            Margin = new Thickness(0, 0, 0, 1),
            ToolTip = $"{label}: {value}"
        };
        text.Inlines.Add(new Run($"{label} ")
        {
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"]
        });
        AddMetricValueInlines(text, value);
        return text;
    }

    private static TextBlock MetricValueBlock(string text, bool compact, bool emphasizeWholeLine = false)
    {
        var block = new TextBlock
        {
            FontSize = compact ? 11.5 : 12,
            FontWeight = FontWeights.Medium,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis,
            LineHeight = 14,
            Margin = new Thickness(0, 0, 0, 1),
            ToolTip = text
        };
        AddMetricValueInlines(block, text, emphasizeWholeLine);
        return block;
    }

    private static void AddMetricValueInlines(TextBlock block, string text, bool emphasizeWholeLine = false)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            block.Inlines.Add(new Run("..."));
            return;
        }

        if (emphasizeWholeLine)
        {
            block.Inlines.Add(new Run(text)
            {
                FontWeight = FontWeights.Bold,
                Foreground = (WpfBrush)WpfApplication.Current.Resources["AccentBlue"]
            });
            return;
        }

        var index = 0;
        foreach (Match match in MetricImportantValuePattern.Matches(text))
        {
            if (match.Index > index)
                block.Inlines.Add(new Run(text[index..match.Index])
                {
                    Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"]
                });

            var valueRun = new Run(match.Value)
            {
                FontFamily = MetricValueFont,
                FontWeight = FontWeights.Bold,
                Foreground = (WpfBrush)WpfApplication.Current.Resources["AccentBlue"]
            };
            Typography.SetNumeralAlignment(valueRun, FontNumeralAlignment.Tabular);
            block.Inlines.Add(valueRun);
            index = match.Index + match.Length;
        }

        if (index < text.Length)
        {
            block.Inlines.Add(new Run(text[index..])
            {
                Foreground = index == 0
                    ? (WpfBrush)WpfApplication.Current.Resources["TextMain"]
                    : (WpfBrush)WpfApplication.Current.Resources["TextMuted"]
            });
        }
    }

    private sealed class MetricRenderState
    {
        public string Value { get; set; } = "";

        public bool EmphasizeLoadedStatus { get; set; }
    }
}
