using System.Windows;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;

namespace LocalLlmConsole;

/// <summary>A small, dependency-free time-series plot intended for overview metric cards.</summary>
public sealed class MetricSparkline : FrameworkElement
{
    private const int MaximumSamples = 60;
    private readonly List<MetricSparklineSample> _samples = [];
    private string _seriesKey = "";

    public string PrimaryBrushKey { get; init; } = "AccentBlue";

    public string SecondaryBrushKey { get; init; } = "Accent";

    public double? FixedMaximum { get; init; }

    public int SampleCount => _samples.Count;

    public void Push(string seriesKey, double? primary, double? secondary = null)
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
        }

        _samples.Add(new MetricSparklineSample(Normalize(primary), Normalize(secondary)));
        if (_samples.Count > MaximumSamples)
            _samples.RemoveRange(0, _samples.Count - MaximumSamples);
        InvalidateVisual();
    }

    public void Clear()
    {
        _seriesKey = "";
        _samples.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        if (ActualWidth <= 1 || ActualHeight <= 1) return;

        var plotRect = new Rect(.5, .5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1));
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
            var y = Math.Round(ActualHeight * division / 3.0) + 0.5;
            drawingContext.DrawLine(gridPen, new WpfPoint(3, y), new WpfPoint(ActualWidth - 3, y));
        }
        for (var division = 1; division <= 3; division++)
        {
            var x = Math.Round(ActualWidth * division / 4.0) + .5;
            drawingContext.DrawLine(gridPen, new WpfPoint(x, 3), new WpfPoint(x, ActualHeight - 3));
        }

        if (_samples.Count == 0) return;
        var observedMaximum = _samples
            .SelectMany(sample => new[] { sample.Primary, sample.Secondary })
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .DefaultIfEmpty(0)
            .Max();
        var maximum = FixedMaximum is > 0
            ? FixedMaximum.Value
            : Math.Max(1, observedMaximum * 1.08);

        DrawSeries(drawingContext, sample => sample.Primary, ResourceBrush(PrimaryBrushKey), maximum);
        DrawSeries(drawingContext, sample => sample.Secondary, ResourceBrush(SecondaryBrushKey), maximum);
    }

    private void DrawSeries(
        DrawingContext drawingContext,
        Func<MetricSparklineSample, double?> selector,
        WpfBrush brush,
        double maximum)
    {
        var pen = new WpfPen(brush, 1.45);
        WpfPoint? previous = null;
        WpfPoint? latest = null;
        for (var index = 0; index < _samples.Count; index++)
        {
            var value = selector(_samples[index]);
            if (value is null)
            {
                previous = null;
                continue;
            }

            const double plotInset = 3;
            var plotWidth = Math.Max(0, ActualWidth - plotInset * 2);
            var x = _samples.Count == 1
                ? plotInset + plotWidth
                : plotInset + index * plotWidth / (_samples.Count - 1.0);
            var fraction = Math.Clamp(value.Value / maximum, 0, 1);
            var plotHeight = Math.Max(0, ActualHeight - plotInset * 2);
            var point = new WpfPoint(x, plotInset + plotHeight * (1 - fraction));
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

    private WpfBrush ResourceBrush(string key)
        => TryFindResource(key) as WpfBrush ?? System.Windows.Media.Brushes.SlateGray;

    private static double? Normalize(double? value)
        => value is { } number && double.IsFinite(number) && number >= 0 ? number : null;

    private sealed record MetricSparklineSample(double? Primary, double? Secondary);
}
