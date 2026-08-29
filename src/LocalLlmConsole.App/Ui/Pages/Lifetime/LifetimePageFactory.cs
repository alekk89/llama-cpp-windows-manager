using System.Collections;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed record LifetimeCalendarMetricOption(UsageCalendarMetric Metric, string Label)
{
    public override string ToString() => Label;
}

public sealed record LifetimePageActions(
    EventHandler RangeChanged,
    SelectionChangedEventHandler FilterChanged,
    EventHandler CalendarSelectionChanged,
    RoutedEventHandler ClearDateSelectionClick,
    RoutedEventHandler ResetLifetimeRowClick,
    RoutedEventHandler ResetVisibleMetricsClick);

public sealed record LifetimePageRequest(
    IEnumerable Rows,
    LifetimeMetricsSelection Selection,
    LifetimePageActions Actions);

public sealed record LifetimePageControls(
    DataGrid MetricsGrid,
    LifetimeRangeSelector RangeSelector,
    WpfComboBox ModelFilter,
    WpfComboBox ProfileFilter,
    WpfComboBox RuntimeFilter,
    TextBlock TotalValue,
    TextBlock InputValue,
    TextBlock InputDetail,
    TextBlock OutputValue,
    TextBlock CacheValue,
    TextBlock CacheDetail,
    TextBlock GpuEnergyValue,
    TextBlock GpuEnergyDetail,
    LifetimeInsightControls Insights,
    TextBlock HistoryNote,
    TextBlock DateSelectionSummary,
    WpfButton ClearDateSelectionButton,
    LifetimeUsageCalendar HistoryCalendar,
    ScrollViewer HistoryScroller,
    WpfComboBox CalendarMetric,
    WpfButton ResetVisibleButton);

public sealed record LifetimePageBuildResult(
    DockPanel Content,
    LifetimePageControls Controls);

public static partial class LifetimePageFactory
{
    public static LifetimePageBuildResult Create(LifetimePageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Rows);
        ArgumentNullException.ThrowIfNull(request.Selection);
        ArgumentNullException.ThrowIfNull(request.Actions);

        var root = new DockPanel();
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new StackPanel { Margin = new Thickness(16) };
        scroll.Content = content;
        root.Children.Add(scroll);

        var toolbar = BuildToolbar(request, out var range, out var model, out var profile, out var runtime, out var resetVisible);
        content.Children.Add(toolbar);
        content.Children.Add(BuildSummary(
            out var total,
            out var input,
            out var inputDetail,
            out var output,
            out var cache,
            out var cacheDetail,
            out var gpuEnergy,
            out var gpuEnergyDetail));
        content.Children.Add(LifetimeInsightsPanel.Create(out var insights));

        var calendar = new LifetimeUsageCalendar();
        calendar.SelectionChanged += request.Actions.CalendarSelectionChanged;
        var calendarMetric = BuildCalendarMetricSelector(request.Selection.CalendarMetric);
        calendar.Metric = request.Selection.CalendarMetric;
        calendarMetric.SelectionChanged += (_, _) =>
        {
            if (calendarMetric.SelectedItem is LifetimeCalendarMetricOption option)
                calendar.Metric = option.Metric;
        };
        var calendarScroller = new ScrollViewer
        {
            Content = calendar,
            Height = 164,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            CanContentScroll = false
        };
        var historyNote = new TextBlock
        {
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0)
        };
        content.Children.Add(BuildCalendarSection(
            calendarScroller,
            calendarMetric,
            historyNote,
            request.Actions.ClearDateSelectionClick,
            out var dateSelectionSummary,
            out var clearDateSelection));

        var metricsGrid = BuildModelGrid(request);
        content.Children.Add(PageSectionFactory.GridSection(Loc.T("Lifetime.ModelBreakdownTitle"), metricsGrid));

        return new LifetimePageBuildResult(
            root,
            new LifetimePageControls(
                metricsGrid,
                range,
                model,
                profile,
                runtime,
                total,
                input,
                inputDetail,
                output,
                cache,
                cacheDetail,
                gpuEnergy,
                gpuEnergyDetail,
                insights,
                historyNote,
                dateSelectionSummary,
                clearDateSelection,
                calendar,
                calendarScroller,
                calendarMetric,
                resetVisible));
    }

    private static Border BuildToolbar(
        LifetimePageRequest request,
        out LifetimeRangeSelector range,
        out WpfComboBox model,
        out WpfComboBox profile,
        out WpfComboBox runtime,
        out WpfButton reset)
    {
        var panel = new Grid();
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        for (var index = 0; index < 3; index++)
            panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        panel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        range = BuildRangeSelector(request.Selection.Range, panel);
        model = Filter(Loc.T("Lifetime.Filter.Model"), 1, panel, 190);
        profile = Filter(Loc.T("Lifetime.Filter.Profile"), 2, panel, 170);
        runtime = Filter(Loc.T("Lifetime.Filter.Runtime"), 3, panel, 170);
        reset = new WpfButton
        {
            Content = Loc.T("Lifetime.ResetVisible"),
            ToolTip = Loc.T("Lifetime.ResetVisibleTooltip"),
            MinWidth = 108,
            Margin = new Thickness(12, 19, 0, 0),
            VerticalAlignment = VerticalAlignment.Top
        };
        VisualRole.SetButtonRole(reset, VisualRole.Danger);
        reset.Click += request.Actions.ResetVisibleMetricsClick;
        Grid.SetColumn(reset, 5);
        panel.Children.Add(reset);

        range.SelectionChanged += request.Actions.RangeChanged;
        model.SelectionChanged += request.Actions.FilterChanged;
        profile.SelectionChanged += request.Actions.FilterChanged;
        runtime.SelectionChanged += request.Actions.FilterChanged;

        LifetimePageResponsiveCoordinator.ConfigureToolbar(panel, range, model, profile, runtime, reset);

        return new Border
        {
            Style = (Style)WpfApplication.Current.Resources["Panel"],
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 0, 10),
            Child = panel
        };
    }

    private static LifetimeRangeSelector BuildRangeSelector(UsageMetricsRange selectedRange, Grid parent)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 0, 10, 0) };
        stack.Children.Add(FilterLabel(Loc.T("Lifetime.Filter.Range")));
        var selector = new LifetimeRangeSelector();
        selector.SetRange(selectedRange);
        stack.Children.Add(selector);
        Grid.SetColumn(stack, 0);
        parent.Children.Add(stack);
        return selector;
    }

    private static WpfComboBox Filter(string label, int column, Grid parent, double width)
    {
        var stack = new StackPanel { Margin = new Thickness(column == 0 ? 0 : 6, 0, column == 3 ? 0 : 6, 0) };
        var text = FilterLabel(label);
        var combo = new WpfComboBox
        {
            Width = width,
            MinWidth = 110,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        AutomationProperties.SetName(combo, label);
        stack.Children.Add(text);
        stack.Children.Add(combo);
        Grid.SetColumn(stack, column);
        parent.Children.Add(stack);
        return combo;
    }

    private static TextBlock FilterLabel(string label)
        => new()
        {
            Text = label,
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMuted"),
            Margin = new Thickness(2, 0, 0, 4)
        };

    private static Grid BuildSummary(
        out TextBlock total,
        out TextBlock input,
        out TextBlock inputDetail,
        out TextBlock output,
        out TextBlock cache,
        out TextBlock cacheDetail,
        out TextBlock gpuEnergy,
        out TextBlock gpuEnergyDetail)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2) };
        for (var index = 0; index < 5; index++)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(SummaryCard(Loc.T("Lifetime.Summary.Total"), 0, out total, out _, 5));
        grid.Children.Add(SummaryCard(Loc.T("Lifetime.Summary.Input"), 1, out input, out inputDetail, 5));
        grid.Children.Add(SummaryCard(Loc.T("Lifetime.Summary.Output"), 2, out output, out _, 5));
        grid.Children.Add(SummaryCard(Loc.T("Lifetime.Summary.CacheHit"), 3, out cache, out cacheDetail, 5));
        grid.Children.Add(SummaryCard(Loc.T("Lifetime.Summary.GpuEnergy"), 4, out gpuEnergy, out gpuEnergyDetail, 5));
        return grid;
    }

    private static Border SummaryCard(
        string label,
        int column,
        out TextBlock value,
        out TextBlock detail,
        int columnCount = 4)
    {
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 10.5,
            FontWeight = FontWeights.SemiBold
        });
        value = new TextBlock
        {
            Text = "…",
            Foreground = ResourceBrush("TextMain"),
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Segoe UI"),
            FontWeight = FontWeights.Bold,
            FontSize = 20,
            Margin = new Thickness(0, 5, 0, 2)
        };
        detail = new TextBlock
        {
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 10,
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = Loc.T("Lifetime.Summary.DetailTooltip")
        };
        stack.Children.Add(value);
        stack.Children.Add(detail);
        var card = new Border
        {
            Style = (Style)WpfApplication.Current.Resources["MetricCard"],
            MinHeight = 88,
            Margin = new Thickness(column == 0 ? 0 : 5, 0, column == columnCount - 1 ? 0 : 5, 8),
            Child = stack
        };
        Grid.SetColumn(card, column);
        return card;
    }

    private static DataGrid BuildModelGrid(LifetimePageRequest request)
    {
        var grid = PageSectionFactory.GridFor(
            (Loc.T("Lifetime.Col.Model"), nameof(LifetimeMetricRow.ModelName), 2.2),
            (Loc.T("Lifetime.Col.Requests"), nameof(LifetimeMetricRow.Requests), .65),
            (Loc.T("Lifetime.Col.Input"), nameof(LifetimeMetricRow.InputTokens), .8),
            (Loc.T("Lifetime.Col.Cached"), nameof(LifetimeMetricRow.CachedTokens), .8),
            (Loc.T("Lifetime.Col.Output"), nameof(LifetimeMetricRow.OutputTokens), .8),
            (Loc.T("Lifetime.Col.Total"), nameof(LifetimeMetricRow.TotalTokens), .8),
            (Loc.T("Lifetime.Col.Share"), nameof(LifetimeMetricRow.Share), .65),
            (Loc.T("Lifetime.Col.GenerationRate"), nameof(LifetimeMetricRow.GenerationRate), .75));
        PageSectionFactory.AddButtonColumn(grid, Loc.T("Lifetime.ResetButton"), nameof(LifetimeMetricRow.ResetAction), nameof(LifetimeMetricRow.CanReset), request.Actions.ResetLifetimeRowClick, .55, tooltipBinding: nameof(LifetimeMetricRow.ResetToolTip), visualRole: VisualRole.Danger);
        grid.ItemsSource = request.Rows;
        grid.MinHeight = 120;
        grid.MaxHeight = 260;
        DataGridRowContextMenu.Attach(
            grid,
            new DataGridRowContextAction(
                _ => Loc.T("Lifetime.ResetButton"),
                row => row is LifetimeMetricRow { CanReset: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.ResetLifetimeRowClick, row),
                ToolTip: row => ((LifetimeMetricRow)row).ResetToolTip));
        return grid;
    }

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
