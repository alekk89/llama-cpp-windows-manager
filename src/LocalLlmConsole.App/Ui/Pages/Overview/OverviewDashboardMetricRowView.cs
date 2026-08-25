using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfPanel = System.Windows.Controls.Panel;

namespace LocalLlmConsole;

/// <summary>
/// A self-labeling semantic metric row. Value, unit, detail, and optional chart
/// have fixed positions and wrap instead of disappearing when space is limited.
/// </summary>
public sealed class OverviewDashboardMetricRowView
{
    private const double LabelFontSize = 10.25;
    private const double MeasurementValueFontSize = 14.5;
    private const double TextValueFontSize = 13;
    private const double UnitFontSize = 9;
    private const double DetailFontSize = 9.5;
    private const double UnavailableValueFontSize = 11.75;
    private static readonly WpfFontFamily MeasurementFont = new("Cascadia Mono, Consolas, Segoe UI");
    private readonly TextBlock _value;
    private readonly TextBlock _unit;
    private readonly ColumnDefinition _unitColumn;
    private readonly TextBlock _detail;
    private readonly Grid _valueLine;
    private readonly TextBlock _dragHandle;
    private readonly Border _separator;
    private readonly RowDefinition _separatorRow;
    private readonly double _valueFontSize;
    private readonly bool _requiresObservedValue;
    private bool _reordering;
    private bool _alternateSurface;
    private bool _hovered;
    private bool _dragActive;
    private int _visualIndex = -1;
    private bool _separatorVisible;
    private MetricPresentationState? _lastPresentation;

    public OverviewDashboardMetricRowView(
        OverviewDashboardMetricDefinition definition,
        MetricSparkline? graph,
        bool showSeparator,
        bool alternateSurface = false)
    {
        MetricId = definition.Id;
        _requiresObservedValue = definition.RequiresObservedValue;
        Graph = graph;
        _alternateSurface = alternateSurface;
        Root = new Grid
        {
            Tag = this,
            ToolTip = definition.Tooltip,
            Margin = new Thickness(-4, 0, -4, 0),
            Background = WpfBrushes.Transparent,
            SnapsToDevicePixels = true
        };
        Root.MouseEnter += (_, _) =>
        {
            _hovered = true;
            RefreshSurface();
        };
        Root.MouseLeave += (_, _) =>
        {
            _hovered = false;
            RefreshSurface();
        };
        ToolTipService.SetShowDuration(Root, 20000);
        Root.ColumnDefinitions.Add(new ColumnDefinition());
        Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        Root.Children.Add(new TextBlock
        {
            Text = definition.DisplayName,
            FontSize = LabelFontSize,
            FontWeight = FontWeights.Medium,
            Foreground = ResourceBrush("TextSoft"),
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 1, 8, 1)
        });

        _valueLine = new Grid
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 4, 0)
        };
        _valueLine.ColumnDefinitions.Add(new ColumnDefinition());
        _unitColumn = new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 36 };
        _valueLine.ColumnDefinitions.Add(_unitColumn);
        _valueFontSize = definition.Presentation is OverviewDashboardMetricPresentation.Status
            or OverviewDashboardMetricPresentation.Text
            ? TextValueFontSize
            : MeasurementValueFontSize;
        _value = new TextBlock
        {
            FontSize = _valueFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain"),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Right,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
        };
        if (IsMeasurement(definition.Presentation))
            _value.FontFamily = MeasurementFont;
        Typography.SetNumeralAlignment(_value, FontNumeralAlignment.Tabular);
        _unit = new TextBlock
        {
            FontSize = UnitFontSize,
            FontFamily = MeasurementFont,
            Foreground = ResourceBrush("TextMuted"),
            Margin = new Thickness(4, 0, 0, 2),
            VerticalAlignment = VerticalAlignment.Bottom,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        Typography.SetNumeralAlignment(_unit, FontNumeralAlignment.Tabular);
        _valueLine.Children.Add(_value);
        Grid.SetColumn(_unit, 1);
        _valueLine.Children.Add(_unit);
        Grid.SetColumn(_valueLine, 1);
        Root.Children.Add(_valueLine);

        _dragHandle = new TextBlock
        {
            Text = "⠿",
            FontSize = 15,
            Foreground = ResourceBrush("TextMuted"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 2, 0),
            ToolTip = Loc.T("Dashboard.Reorder"),
            Visibility = Visibility.Collapsed
        };
        Grid.SetColumn(_dragHandle, 1);
        Root.Children.Add(_dragHandle);

        _detail = new TextBlock
        {
            FontSize = DetailFontSize,
            Foreground = ResourceBrush("TextMuted"),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 11.5,
            Margin = new Thickness(4, 1, 4, 0),
            Visibility = Visibility.Collapsed
        };
        Grid.SetRow(_detail, 1);
        Grid.SetColumnSpan(_detail, 2);
        Root.Children.Add(_detail);

        var nextRow = 2;
        if (graph is not null)
        {
            Root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(graph, nextRow++);
            Grid.SetColumnSpan(graph, 2);
            Root.Children.Add(graph);
        }

        _separatorRow = new RowDefinition { Height = showSeparator ? new GridLength(7) : new GridLength(0) };
        Root.RowDefinitions.Add(_separatorRow);
        _separator = new Border
        {
            Height = 1,
            Background = ResourceBrush("PanelBorder"),
            Opacity = .58,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false,
            Visibility = showSeparator ? Visibility.Visible : Visibility.Collapsed
        };
        Grid.SetRow(_separator, nextRow);
        Grid.SetColumnSpan(_separator, 2);
        Root.Children.Add(_separator);
        _separatorVisible = showSeparator;
        RefreshSurface();
    }

    public string MetricId { get; }
    public Grid Root { get; }
    public MetricSparkline? Graph { get; }
    public bool IsAvailable { get; private set; } = true;

    public void SetReorderMode(bool active)
    {
        if (_reordering == active) return;
        _reordering = active;
        _valueLine.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        _dragHandle.Visibility = active ? Visibility.Visible : Visibility.Collapsed;
        _detail.Visibility = active || string.IsNullOrWhiteSpace(_detail.Text)
            ? Visibility.Collapsed
            : Visibility.Visible;
        if (Graph is not null)
            Graph.Visibility = active ? Visibility.Collapsed : Visibility.Visible;
        Root.Cursor = active ? System.Windows.Input.Cursors.SizeNS : null;
    }

    public void SetDragState(bool active)
    {
        if (_dragActive == active) return;
        _dragActive = active;
        Root.Opacity = active ? .76 : 1;
        _dragHandle.SetResourceReference(TextBlock.ForegroundProperty, active ? "AccentBlue" : "TextMuted");
        RefreshSurface();
    }

    public bool SetVisualPosition(int index, bool showSeparator)
    {
        if (_visualIndex == index && _separatorVisible == showSeparator) return false;
        _visualIndex = index;
        _alternateSurface = index % 2 == 1;
        SetSeparatorVisible(showSeparator);
        RefreshSurface();
        return true;
    }

    public void SetSeparatorVisible(bool visible)
    {
        if (_separatorVisible == visible) return;
        _separatorVisible = visible;
        _separator.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        _separatorRow.Height = visible ? new GridLength(7) : new GridLength(0);
    }

    public bool Apply(OverviewDashboardMetricReading? reading)
    {
        var available = !_requiresObservedValue
                        || (reading?.Primary is { } primary && double.IsFinite(primary));
        var unavailable = reading is null || string.IsNullOrWhiteSpace(reading.Value);
        var presentation = new MetricPresentationState(
            available,
            unavailable,
            unavailable ? Loc.T("Dashboard.ValueUnavailable") : reading!.Value,
            reading?.Unit ?? "",
            reading?.Detail ?? "");
        if (presentation == _lastPresentation) return false;

        _lastPresentation = presentation;
        IsAvailable = available;
        Root.Visibility = available ? Visibility.Visible : Visibility.Collapsed;
        if (!available) return true;

        _value.Text = presentation.Value;
        _value.FontSize = unavailable ? UnavailableValueFontSize : _valueFontSize;
        _value.FontWeight = unavailable ? FontWeights.Medium : FontWeights.SemiBold;
        _value.Foreground = ResourceBrush(unavailable ? "TextMuted" : "TextMain");
        _unit.Text = presentation.Unit;
        _unit.Visibility = string.IsNullOrWhiteSpace(presentation.Unit) ? Visibility.Collapsed : Visibility.Visible;
        _unitColumn.Width = _unit.Visibility == Visibility.Visible
            ? GridLength.Auto
            : new GridLength(0);
        _detail.Text = presentation.Detail;
        _detail.Visibility = _reordering || string.IsNullOrWhiteSpace(presentation.Detail)
            ? Visibility.Collapsed
            : Visibility.Visible;
        return true;
    }

    private void RefreshSurface()
    {
        if (_dragActive)
            Root.SetResourceReference(WpfPanel.BackgroundProperty, "ControlHover");
        else if (_hovered)
            Root.SetResourceReference(WpfPanel.BackgroundProperty, "InfoSoft");
        else if (_alternateSurface)
            Root.SetResourceReference(WpfPanel.BackgroundProperty, "GridRowAlt");
        else
            Root.Background = WpfBrushes.Transparent;
    }

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];

    private static bool IsMeasurement(OverviewDashboardMetricPresentation presentation)
        => presentation is not (OverviewDashboardMetricPresentation.Status
            or OverviewDashboardMetricPresentation.Text);

    private sealed record MetricPresentationState(
        bool Available,
        bool Unavailable,
        string Value,
        string Unit,
        string Detail);
}
