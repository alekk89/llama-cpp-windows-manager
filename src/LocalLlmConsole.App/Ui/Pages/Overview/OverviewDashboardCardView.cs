using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfCursors = System.Windows.Input.Cursors;

namespace LocalLlmConsole;

[Flags]
public enum OverviewDashboardResizeEdge
{
    Left = 1,
    Top = 2,
    Right = 4,
    Bottom = 8
}

public sealed partial class OverviewDashboardCardView
{
    public const double ContentMinimumWidth = 180;
    public const double ResizeHitThickness = 8;

    private readonly Dictionary<string, OverviewDashboardMetricRowView> _rows = new(StringComparer.Ordinal);
    private readonly Dictionary<string, MetricSparkline> _graphs = new(StringComparer.Ordinal);
    private readonly Grid _content;
    private readonly StackPanel _values;
    private bool _interactionPreview;
    private bool _resizeHover;
    private bool _resizeEnabled = true;
    private long _contentGeometryVersion;
    private long _measuredContentGeometryVersion = -1;
    private double _lastMeasuredWidth = double.NaN;

    public OverviewDashboardCardView(
        OverviewDashboardCardLayout layout,
        IReadOnlyList<OverviewDashboardMetricDefinition> definitions)
    {
        Layout = layout;
        var byId = definitions.ToDictionary(item => item.Id, StringComparer.Ordinal);
        Root = new Border
        {
            Style = (Style)WpfApplication.Current.Resources["TelemetryMetricCard"],
            ClipToBounds = true,
            Tag = this,
            Margin = new Thickness(0),
            Cursor = WpfCursors.SizeAll
        };

        _content = new Grid();
        _values = new StackPanel();
        if (!string.IsNullOrWhiteSpace(layout.Title))
        {
            _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _content.Children.Add(new TextBlock
            {
                Text = layout.Title,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = ResourceBrush("TextMain"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 0, 4, 7),
                ToolTip = layout.Title
            });
        }
        _content.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        for (var metricIndex = 0; metricIndex < layout.MetricIds.Count; metricIndex++)
        {
            var metricId = layout.MetricIds[metricIndex];
            var definition = byId[metricId];
            MetricSparkline? graph = null;
            if ((layout.ChartMetricIds ?? (string.IsNullOrWhiteSpace(layout.ChartMetricId)
                    ? []
                    : [layout.ChartMetricId]))
                .Contains(metricId, StringComparer.Ordinal) && definition.Chartable)
            {
                graph = new MetricSparkline
                {
                    Height = 30,
                    Margin = new Thickness(4, 2, 4, 1),
                    PrimaryBrushKey = definition.PrimaryBrushKey,
                    SecondaryBrushKey = definition.SecondaryBrushKey,
                    FixedMaximum = definition.FixedMaximum,
                    ToolTip = Loc.T("Dashboard.ChartTooltip", definition.DisplayName)
                };
                _graphs[metricId] = graph;
            }

            var row = new OverviewDashboardMetricRowView(
                definition,
                graph,
                showSeparator: metricIndex < layout.MetricIds.Count - 1,
                alternateSurface: metricIndex % 2 == 1);
            _rows[definition.Id] = row;
            _values.Children.Add(row.Root);
        }
        Grid.SetRow(_values, string.IsNullOrWhiteSpace(layout.Title) ? 0 : 1);
        _content.Children.Add(_values);
        ConfigureMetricReorderDrag();

        Root.Child = _content;
        UpdateMinimumSize(ContentMinimumWidth);
    }

    public OverviewDashboardCardLayout Layout { get; }
    public Border Root { get; }
    public FrameworkElement DragSurface => Root;
    public IReadOnlyDictionary<string, MetricSparkline> Graphs => _graphs;
    public MetricSparkline? Graph => _graphs.Values.FirstOrDefault();
    public IReadOnlyCollection<string> MetricIds => _rows.Keys;
    public IReadOnlyList<string> CurrentMetricOrder => _values.Children
        .OfType<Grid>()
        .Select(root => ((OverviewDashboardMetricRowView)root.Tag).MetricId)
        .ToArray();
    public double MinimumWidth { get; private set; } = ContentMinimumWidth;
    public double MinimumHeight { get; private set; } = OverviewDashboardLayoutPolicy.MinimumCardHeight;

    public bool ContainsMetricRow(DependencyObject? source)
        => source is not null && _rows.Values.Any(row => row.Root.IsAncestorOf(source) || ReferenceEquals(row.Root, source));

    public void SetReorderMode(bool active)
    {
        if (_reordering == active) return;
        if (!active)
            EndMetricDrag();
        _reordering = active;
        Root.Cursor = active ? WpfCursors.Arrow : WpfCursors.SizeAll;
        UpdateReorderRows(active);
        InvalidateMinimumSize();
    }

    private void UpdateReorderRows(bool active)
    {
        var orderedRows = _values.Children.OfType<Grid>()
            .Select(root => (OverviewDashboardMetricRowView)root.Tag)
            .ToArray();
        for (var index = 0; index < orderedRows.Length; index++)
        {
            var row = orderedRows[index];
            row.SetReorderMode(active);
            row.SetVisualPosition(index, index < orderedRows.Length - 1);
        }
    }

    public void SetInteractionPreview(bool active)
    {
        if (_interactionPreview == active) return;
        _interactionPreview = active;
        RefreshInteractionBorder();
    }

    public void SetResizeHover(bool active)
    {
        if (_resizeHover == active) return;
        _resizeHover = active;
        RefreshInteractionBorder();
    }

    public void SetResizeEnabled(bool enabled)
    {
        _resizeEnabled = enabled;
        if (!enabled)
            SetResizeHover(false);
        ResetPointer();
    }

    public OverviewDashboardResizeEdge ResizeEdgeAt(System.Windows.Point point)
    {
        if (!_resizeEnabled) return 0;
        var width = Root.ActualWidth > 0 ? Root.ActualWidth : Root.Width;
        var height = Root.ActualHeight > 0 ? Root.ActualHeight : Root.Height;
        if (!double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return 0;
        var edge = (OverviewDashboardResizeEdge)0;
        if (point.X >= 0 && point.X <= ResizeHitThickness)
            edge |= OverviewDashboardResizeEdge.Left;
        else if (point.X <= width && point.X >= width - ResizeHitThickness)
            edge |= OverviewDashboardResizeEdge.Right;
        if (point.Y >= 0 && point.Y <= ResizeHitThickness)
            edge |= OverviewDashboardResizeEdge.Top;
        else if (point.Y <= height && point.Y >= height - ResizeHitThickness)
            edge |= OverviewDashboardResizeEdge.Bottom;
        return edge;
    }

    public OverviewDashboardResizeEdge UpdatePointer(System.Windows.Point point)
    {
        var edge = ResizeEdgeAt(point);
        Root.Cursor = CursorFor(edge);
        SetResizeHover(edge != 0);
        return edge;
    }

    public void ResetPointer()
    {
        Root.Cursor = WpfCursors.SizeAll;
        SetResizeHover(false);
    }

    public bool Apply(IReadOnlyDictionary<string, OverviewDashboardMetricReading> readings, bool pushGraph)
    {
        var geometryChanged = false;
        foreach (var metricId in Layout.MetricIds)
        {
            readings.TryGetValue(metricId, out var reading);
            geometryChanged |= _rows[metricId].Apply(reading);
        }

        var visibleRows = Layout.MetricIds.Select(metricId => _rows[metricId])
            .Where(row => row.IsAvailable)
            .ToArray();
        for (var index = 0; index < visibleRows.Length; index++)
            geometryChanged |= visibleRows[index].SetVisualPosition(index, index < visibleRows.Length - 1);
        var visibility = visibleRows.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (Root.Visibility != visibility)
        {
            Root.Visibility = visibility;
            geometryChanged = true;
        }

        if (geometryChanged)
            InvalidateMinimumSize();

        if (pushGraph)
        {
            foreach (var (metricId, graph) in _graphs)
            {
                if (_rows[metricId].IsAvailable && readings.TryGetValue(metricId, out var graphReading))
                    graph.Push(graphReading.RuntimeKey, graphReading.Primary, graphReading.Secondary);
            }
        }
        return geometryChanged;
    }

    public void UpdateMinimumSize(double proposedWidth)
    {
        MinimumWidth = ContentMinimumWidth;
        var measuredWidth = double.IsFinite(proposedWidth)
            ? Math.Max(MinimumWidth, proposedWidth)
            : MinimumWidth;
        if (_measuredContentGeometryVersion == _contentGeometryVersion
            && SameLength(_lastMeasuredWidth, measuredWidth))
            return;
        var horizontalChrome = Root.Padding.Left + Root.Padding.Right
                               + Root.BorderThickness.Left + Root.BorderThickness.Right;
        var verticalChrome = Root.Padding.Top + Root.Padding.Bottom
                             + Root.BorderThickness.Top + Root.BorderThickness.Bottom;
        _content.Measure(new System.Windows.Size(Math.Max(1, measuredWidth - horizontalChrome), double.PositiveInfinity));
        MinimumHeight = Math.Max(
            OverviewDashboardLayoutPolicy.MinimumCardHeight,
            Math.Ceiling(_content.DesiredSize.Height + verticalChrome));
        _lastMeasuredWidth = measuredWidth;
        _measuredContentGeometryVersion = _contentGeometryVersion;
    }

    private void InvalidateMinimumSize()
        => _contentGeometryVersion++;

    private static bool SameLength(double first, double second)
        => double.IsFinite(first) && double.IsFinite(second) && Math.Abs(first - second) < .1;

    private void RefreshInteractionBorder()
    {
        if (_interactionPreview || _resizeHover)
        {
            Root.SetResourceReference(Border.BorderBrushProperty, "AccentBlue");
            Root.BorderThickness = new Thickness(_interactionPreview ? 2 : 1);
            return;
        }

        Root.ClearValue(Border.BorderBrushProperty);
        Root.ClearValue(Border.BorderThicknessProperty);
    }

    public static double Height(OverviewDashboardCardHeight height)
        => OverviewDashboardLayoutPolicy.CardHeight(height);

    private static System.Windows.Input.Cursor CursorFor(OverviewDashboardResizeEdge edge)
        => edge switch
        {
            OverviewDashboardResizeEdge.Left or OverviewDashboardResizeEdge.Right => WpfCursors.SizeWE,
            OverviewDashboardResizeEdge.Top or OverviewDashboardResizeEdge.Bottom => WpfCursors.SizeNS,
            OverviewDashboardResizeEdge.Left | OverviewDashboardResizeEdge.Top
                or OverviewDashboardResizeEdge.Right | OverviewDashboardResizeEdge.Bottom => WpfCursors.SizeNWSE,
            OverviewDashboardResizeEdge.Right | OverviewDashboardResizeEdge.Top
                or OverviewDashboardResizeEdge.Left | OverviewDashboardResizeEdge.Bottom => WpfCursors.SizeNESW,
            _ => WpfCursors.SizeAll
        };

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
