using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfFlowDirection = System.Windows.FlowDirection;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace LocalLlmConsole;

/// <summary>
/// A small, dependency-free time-series plot intended for overview metric cards.
/// A caption above the plot names the time span the samples cover; hovering
/// the plot or focusing its metric row adds a min, max, and latest readout.
/// </summary>
public sealed class MetricSparkline : FrameworkElement
{
    public const int MaximumSamples = 60;
    public const double CaptionHeight = 12;
    private const double CaptionFontSize = 9;
    private const double PlotInset = 3;
    private const double MinimumSharedReadoutWidth = 64;
    private static readonly Typeface CaptionTypeface = new("Segoe UI");
    private readonly List<MetricSparklineSample> _samples = [];
    private readonly string _metricName = "";
    private string _seriesKey = "";
    private string _unit = "";
    private double? _stableMaximum;
    private UIElement? _focusHost;

    public MetricSparkline()
    {
        ToolTip = "";
        ToolTipService.SetShowDuration(this, 20000);
    }

    public string PrimaryBrushKey { get; init; } = "AccentBlue";

    public string SecondaryBrushKey { get; init; } = "Accent";

    public double? FixedMaximum { get; init; }

    /// <summary>Scale for unbounded metrics. Charts with <see cref="FixedMaximum"/> ignore it.</summary>
    public OverviewDashboardChartScale Scale { get; init; } = OverviewDashboardChartScale.Stable;

    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;

    public string MetricName
    {
        get => _metricName;
        init
        {
            _metricName = value ?? "";
            AutomationProperties.SetName(this, Loc.T("Dashboard.ChartTooltip", _metricName));
        }
    }

    public int SampleCount => _samples.Count;

    public string Unit => _unit;

    /// <summary>Time between the oldest and newest visible sample, when known.</summary>
    public TimeSpan? Span
        => _samples.Count > 1 && _samples[^1].SampledAt - _samples[0].SampledAt is { } span && span > TimeSpan.Zero
            ? span
            : null;

    public string SpanLabel => Span is { } span ? FormatSpan(span) : "";

    public MetricSparklineSummary? Summary => MetricSparklineScale.Summarize(_samples.Select(sample => sample.Primary));

    public string ReadoutText => Summary is { } summary ? FormatReadout(summary, _unit) : "";

    public bool IsReadoutVisible
        => _samples.Count > 0
           && (IsMouseOver || IsKeyboardFocusWithin || _focusHost?.IsKeyboardFocusWithin == true);

    public MetricSparklineRange Range
    {
        get
        {
            if (FixedMaximum is > 0) return new(0, FixedMaximum.Value);
            var values = Values().ToArray();
            if (values.Length == 0) return new(0, 1);
            return Scale == OverviewDashboardChartScale.FitToData
                ? MetricSparklineScale.FitRange(values.Min(), values.Max())
                : new(0, _stableMaximum ?? MetricSparklineScale.StableMaximum(values.Max(), null));
        }
    }

    public void Push(
        string seriesKey,
        double? primary,
        double? secondary = null,
        DateTimeOffset? sampledAt = null,
        string? unit = null)
    {
        if (string.IsNullOrWhiteSpace(seriesKey))
        {
            Clear();
            return;
        }

        if (!string.Equals(_seriesKey, seriesKey, StringComparison.Ordinal))
        {
            _seriesKey = seriesKey;
            _samples.Clear();
            _stableMaximum = null;
        }

        if (!string.IsNullOrWhiteSpace(unit))
            _unit = unit.Trim();
        _samples.Add(new MetricSparklineSample(
            Normalize(primary),
            Normalize(secondary),
            sampledAt ?? TimeProvider.GetUtcNow()));
        if (_samples.Count > MaximumSamples)
            _samples.RemoveRange(0, _samples.Count - MaximumSamples);
        var values = Values().ToArray();
        if (values.Length > 0)
            _stableMaximum = MetricSparklineScale.StableMaximum(values.Max(), _stableMaximum);
        AutomationProperties.SetHelpText(this, Description());
        InvalidateVisual();
    }

    public void Clear()
    {
        _seriesKey = "";
        _samples.Clear();
        _stableMaximum = null;
        AutomationProperties.SetHelpText(this, "");
        InvalidateVisual();
    }

    public static string FormatSpan(TimeSpan span)
    {
        if (span.TotalSeconds < 90)
            return Loc.T("Dashboard.ChartSpanSeconds", Math.Max(1, (int)Math.Round(span.TotalSeconds)));
        if (span.TotalMinutes < 90)
            return Loc.T("Dashboard.ChartSpanMinutes", (int)Math.Round(span.TotalMinutes));
        return Loc.T("Dashboard.ChartSpanHours",
            Math.Round(span.TotalHours, 1).ToString("0.#", CultureInfo.CurrentCulture));
    }

    public static string FormatReadout(MetricSparklineSummary summary, string unit)
    {
        var readout = Loc.T(
            "Dashboard.ChartReadout",
            MetricSparklineScale.FormatValue(summary.Minimum),
            MetricSparklineScale.FormatValue(summary.Maximum),
            MetricSparklineScale.FormatValue(summary.Latest));
        return string.IsNullOrWhiteSpace(unit) ? readout : $"{readout} {unit}";
    }

    protected override void OnToolTipOpening(ToolTipEventArgs e)
    {
        var lines = new[] { Loc.T("Dashboard.ChartTooltip", _metricName), SpanLabel, ReadoutText }
            .Where(line => !string.IsNullOrWhiteSpace(line));
        ToolTip = string.Join(Environment.NewLine, lines);
        base.OnToolTipOpening(e);
    }

    protected override void OnMouseEnter(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseEnter(e);
        InvalidateVisual();
    }

    protected override void OnMouseLeave(System.Windows.Input.MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        InvalidateVisual();
    }

    protected override void OnIsKeyboardFocusWithinChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnIsKeyboardFocusWithinChanged(e);
        InvalidateVisual();
    }

    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (_focusHost is not null)
            _focusHost.IsKeyboardFocusWithinChanged -= OnFocusHostChanged;
        // The metric row is the keyboard stop; its focus shows the readout.
        _focusHost = VisualTreeHelper.GetParent(this) as UIElement;
        if (_focusHost is not null)
            _focusHost.IsKeyboardFocusWithinChanged += OnFocusHostChanged;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 1 || ActualHeight <= CaptionHeight + 1) return;

        DrawCaption(drawingContext);
        var plotTop = CaptionHeight;
        var plotHeight = ActualHeight - plotTop;
        var plotRect = new Rect(.5, plotTop + .5, Math.Max(0, ActualWidth - 1), Math.Max(0, plotHeight - 1));
        var plotBackground = ResourceBrush("InputBack").Clone();
        plotBackground.Opacity = .58;
        var frameBrush = ResourceBrush("PanelBorderStrong").Clone();
        frameBrush.Opacity = .58;
        drawingContext.DrawRoundedRectangle(
            plotBackground,
            new WpfPen(frameBrush, .75),
            plotRect,
            3,
            3);

        var gridBrush = ResourceBrush("PanelBorderStrong").Clone();
        gridBrush.Opacity = .30;
        var gridPen = new WpfPen(gridBrush, .55) { DashStyle = DashStyles.Dot };
        for (var division = 1; division <= 2; division++)
        {
            var y = plotTop + Math.Round(plotHeight * division / 3.0) + 0.5;
            drawingContext.DrawLine(gridPen, new WpfPoint(3, y), new WpfPoint(ActualWidth - 3, y));
        }
        for (var division = 1; division <= 3; division++)
        {
            var x = Math.Round(ActualWidth * division / 4.0) + .5;
            drawingContext.DrawLine(gridPen, new WpfPoint(x, plotTop + 3), new WpfPoint(x, ActualHeight - 3));
        }

        if (_samples.Count == 0) return;
        var range = Range;
        DrawSeries(drawingContext, sample => sample.Primary, ResourceBrush(PrimaryBrushKey), range, plotTop, plotHeight);
        DrawSeries(drawingContext, sample => sample.Secondary, ResourceBrush(SecondaryBrushKey), range, plotTop, plotHeight);
    }

    private void DrawCaption(DrawingContext drawingContext)
    {
        var width = Math.Max(0, ActualWidth - 2);
        var span = SpanLabel;
        var readout = IsReadoutVisible ? ReadoutText : "";
        var spanText = string.IsNullOrEmpty(span) ? null : CaptionText(span, "TextMuted", width);
        var readoutLeft = spanText is null ? 1 : spanText.WidthIncludingTrailingWhitespace + 9;
        if (!string.IsNullOrEmpty(readout) && width - readoutLeft < MinimumSharedReadoutWidth)
        {
            spanText = null;
            readoutLeft = 1;
        }
        if (spanText is not null)
            drawingContext.DrawText(spanText, new WpfPoint(1, 0));
        if (string.IsNullOrEmpty(readout)) return;
        var readoutText = CaptionText(readout, "TextMain", Math.Max(1, width - readoutLeft + 1));
        readoutText.TextAlignment = TextAlignment.Right;
        drawingContext.DrawText(readoutText, new WpfPoint(readoutLeft, 0));
    }

    private FormattedText CaptionText(string text, string brushKey, double maxWidth)
        => new(
            text,
            CultureInfo.CurrentUICulture,
            WpfFlowDirection.LeftToRight,
            CaptionTypeface,
            CaptionFontSize,
            ResourceBrush(brushKey),
            VisualTreeHelper.GetDpi(this).PixelsPerDip)
        {
            MaxTextWidth = maxWidth,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };

    private void DrawSeries(
        DrawingContext drawingContext,
        Func<MetricSparklineSample, double?> selector,
        WpfBrush brush,
        MetricSparklineRange range,
        double plotTop,
        double plotHeight)
    {
        var pen = new WpfPen(brush, 1.45);
        WpfPoint? previous = null;
        WpfPoint? latest = null;
        var plotWidth = Math.Max(0, ActualWidth - PlotInset * 2);
        var innerHeight = Math.Max(0, plotHeight - PlotInset * 2);
        for (var index = 0; index < _samples.Count; index++)
        {
            var value = selector(_samples[index]);
            if (value is null)
            {
                previous = null;
                continue;
            }

            var x = _samples.Count == 1
                ? PlotInset + plotWidth
                : PlotInset + index * plotWidth / (_samples.Count - 1.0);
            var point = new WpfPoint(x, plotTop + PlotInset + innerHeight * (1 - range.Fraction(value.Value)));
            if (previous is { } start)
                drawingContext.DrawLine(pen, start, point);
            previous = point;
            latest = point;
        }

        if (latest is { } marker)
        {
            drawingContext.DrawEllipse(ResourceBrush("InputBack"), null, marker, 3, 3);
            drawingContext.DrawEllipse(brush, null, marker, 1.8, 1.8);
        }
    }

    private string Description()
        => string.Join(", ", new[] { SpanLabel, ReadoutText }.Where(text => !string.IsNullOrWhiteSpace(text)));

    private IEnumerable<double> Values()
        => _samples
            .SelectMany(sample => new[] { sample.Primary, sample.Secondary })
            .Where(value => value is not null)
            .Select(value => value!.Value);

    private void OnFocusHostChanged(object sender, DependencyPropertyChangedEventArgs e)
        => InvalidateVisual();

    private WpfBrush ResourceBrush(string key)
        => TryFindResource(key) as WpfBrush ?? System.Windows.Media.Brushes.SlateGray;

    private static double? Normalize(double? value)
        => value is { } number && double.IsFinite(number) && number >= 0 ? number : null;

    private sealed record MetricSparklineSample(double? Primary, double? Secondary, DateTimeOffset SampledAt);
}
