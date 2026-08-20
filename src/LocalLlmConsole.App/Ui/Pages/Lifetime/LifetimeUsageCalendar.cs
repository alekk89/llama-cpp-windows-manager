using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Media;
using WpfBrush = System.Windows.Media.Brush;
using WpfCursors = System.Windows.Input.Cursors;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfPen = System.Windows.Media.Pen;
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;
using WpfSize = System.Windows.Size;

namespace LocalLlmConsole;

public enum UsageCalendarMetric
{
    TotalTokens,
    InputTokens,
    GeneratedTokens,
    CachedPromptTokens,
    Requests
}

/// <summary>Dependency-free, week-based daily activity calendar for persisted token usage.</summary>
public sealed class LifetimeUsageCalendar : FrameworkElement
{
    private const double CellSize = 14;
    private const double CellGap = 3;
    private const double LeftInset = 34;
    private const double RightInset = 8;
    private const double TopInset = 25;
    private readonly List<UsageMetricDay> _days = [];
    private readonly List<(WpfRect Bounds, int Index)> _hitAreas = [];
    private readonly HashSet<DateOnly> _selectedDates = [];
    private readonly UsageDateSelectionService _selectionService = new();
    private DateOnly _gridStart;
    private DateOnly? _selectionAnchor;
    private int _keyboardIndex = -1;
    private UsageCalendarMetric _metric;

    public LifetimeUsageCalendar()
    {
        Focusable = true;
        AutomationProperties.SetName(this, Loc.T("Lifetime.Chart.AutomationName"));
        ToolTip = Loc.T("Lifetime.Chart.Tooltip");
    }

    public int DayCount => _days.Count;

    public int VisibleWeekCount => CalculateVisibleWeekCount();

    public IReadOnlyList<DateOnly> SelectedDates => _selectedDates.Order().ToArray();

    public DateOnly? SelectionAnchor => _selectionAnchor;

    public UsageCalendarMetric Metric
    {
        get => _metric;
        set
        {
            if (_metric == value) return;
            _metric = value;
            InvalidateVisual();
        }
    }

    public event EventHandler? SelectionChanged;

    public void SetData(IReadOnlyList<UsageMetricDay> days, IReadOnlyList<DateOnly>? selectedDates = null)
    {
        ArgumentNullException.ThrowIfNull(days);
        _days.Clear();
        _days.AddRange(days.OrderBy(day => day.Date));
        _keyboardIndex = _days.FindLastIndex(day => day.IsTracked);
        if (_keyboardIndex < 0 && _days.Count > 0) _keyboardIndex = _days.Count - 1;
        _gridStart = _days.Count == 0 ? default : StartOfWeek(_days[0].Date);
        SetSelection(selectedDates ?? SelectedDates, raiseEvent: false);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void SetSelection(IReadOnlyList<DateOnly> dates, bool raiseEvent = false)
    {
        ArgumentNullException.ThrowIfNull(dates);
        var selectable = _days.Where(day => day.IsTracked).Select(day => day.Date).ToHashSet();
        _selectedDates.Clear();
        _selectedDates.UnionWith(dates.Where(selectable.Contains));
        if (_selectedDates.Count == 0)
            _selectionAnchor = null;
        else if (_selectionAnchor is null || !_selectedDates.Contains(_selectionAnchor.Value))
            _selectionAnchor = _selectedDates.Max();
        AutomationProperties.SetHelpText(this, AccessibleSummary());
        InvalidateVisual();
        if (raiseEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection(bool raiseEvent = false)
    {
        if (_selectedDates.Count == 0) return;
        _selectedDates.Clear();
        _selectionAnchor = null;
        AutomationProperties.SetHelpText(this, AccessibleSummary());
        InvalidateVisual();
        if (raiseEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool ApplySelection(DateOnly date, UsageDateSelectionMode mode, bool raiseEvent = true)
    {
        var selectable = _days.Where(day => day.IsTracked).Select(day => day.Date).ToHashSet();
        if (!selectable.Contains(date)) return false;
        var current = new UsageDateSelection(SelectedDates, _selectionAnchor);
        var result = _selectionService.Apply(current, date, mode, selectable);
        var changed = !result.Dates.SequenceEqual(SelectedDates) || result.Anchor != _selectionAnchor;
        if (!changed) return false;
        _selectedDates.Clear();
        _selectedDates.UnionWith(result.Dates);
        _selectionAnchor = result.Anchor;
        AutomationProperties.SetHelpText(this, AccessibleSummary());
        InvalidateVisual();
        if (raiseEvent) SelectionChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    protected override WpfSize MeasureOverride(WpfSize availableSize)
    {
        var desiredWidth = double.IsFinite(availableSize.Width)
            ? Math.Max(260, availableSize.Width)
            : 940;
        return new WpfSize(desiredWidth, 158);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        _hitAreas.Clear();
        if (ActualWidth < 100 || ActualHeight < 90) return;
        if (_days.Count == 0)
        {
            DrawCenteredText(drawingContext, Loc.T("Lifetime.Chart.NoData"));
            return;
        }

        DrawWeekdayLabels(drawingContext);
        DrawMonthLabels(drawingContext);
        var maximum = Math.Max(1, _days.Max(day => MetricValue(day.Totals)));
        var visibleStart = VisibleGridStart;
        for (var index = 0; index < _days.Count; index++)
        {
            var day = _days[index];
            if (day.Date < visibleStart) continue;
            var bounds = DayBounds(day.Date);
            var fill = day.IsTracked
                ? IntensityBrush(MetricValue(day.Totals), maximum)
                : UntrackedBrush();
            drawingContext.DrawRoundedRectangle(fill, null, bounds, 3, 3);
            if (day.Date == DateOnly.FromDateTime(DateTime.Today))
                drawingContext.DrawRoundedRectangle(null, new WpfPen(ResourceBrush("TextMain"), 1), bounds, 3, 3);
            if (_selectedDates.Contains(day.Date))
                drawingContext.DrawRoundedRectangle(null, new WpfPen(ResourceBrush("Accent"), 2.5), Inflate(bounds, 1.5), 4, 4);
            if (index == _keyboardIndex && IsKeyboardFocusWithin)
                drawingContext.DrawRoundedRectangle(null, new WpfPen(ResourceBrush("FocusRing"), 2), Inflate(bounds, 2), 4, 4);
            _hitAreas.Add((bounds, index));
        }
    }

    protected override void OnMouseMove(WpfMouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        var hit = _hitAreas.FirstOrDefault(area => area.Bounds.Contains(point));
        if (!hit.Bounds.IsEmpty)
        {
            var day = _days[hit.Index];
            ToolTip = DayTooltip(day);
            Cursor = day.IsTracked ? WpfCursors.Hand : WpfCursors.Arrow;
        }
    }

    protected override void OnMouseLeave(WpfMouseEventArgs e)
    {
        base.OnMouseLeave(e);
        ToolTip = Loc.T("Lifetime.Chart.Tooltip");
        Cursor = WpfCursors.Arrow;
    }

    protected override void OnMouseLeftButtonDown(WpfMouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        var point = e.GetPosition(this);
        var hit = _hitAreas.FirstOrDefault(area => area.Bounds.Contains(point));
        if (hit.Bounds.IsEmpty || !_days[hit.Index].IsTracked) return;
        Focus();
        _keyboardIndex = hit.Index;
        ApplySelection(_days[hit.Index].Date, SelectionMode(Keyboard.Modifiers));
        e.Handled = true;
    }

    protected override void OnKeyDown(WpfKeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_days.Count == 0) return;
        if (e.Key is Key.Enter or Key.Space)
        {
            ApplySelection(_days[_keyboardIndex].Date, SelectionMode(Keyboard.Modifiers));
            e.Handled = true;
            return;
        }

        var offset = e.Key switch
        {
            Key.Left => -7,
            Key.Right => 7,
            Key.Up => -1,
            Key.Down => 1,
            _ => 0
        };
        if (offset == 0) return;
        _keyboardIndex = Math.Clamp(_keyboardIndex + offset, 0, _days.Count - 1);
        ToolTip = DayTooltip(_days[_keyboardIndex]);
        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawWeekdayLabels(DrawingContext context)
    {
        var labels = CultureInfo.CurrentCulture.DateTimeFormat.AbbreviatedDayNames;
        foreach (var row in new[] { 0, 2, 4, 6 })
        {
            var day = (row + (int)DayOfWeek.Monday) % 7;
            DrawText(context, labels[day], 9.5, ResourceBrush("TextMuted"), new WpfPoint(CalendarLeft - LeftInset, TopInset + row * (CellSize + CellGap) + 1));
        }
    }

    private void DrawMonthLabels(DrawingContext context)
    {
        var lastRight = double.NegativeInfinity;
        var visibleStart = VisibleGridStart > _days[0].Date ? VisibleGridStart : _days[0].Date;
        var visibleEnd = _days[^1].Date;
        for (var week = 0; week < VisibleWeekCount; week++)
        {
            var firstDay = VisibleGridStart.AddDays(week * 7);
            var firstVisibleDay = firstDay < visibleStart ? visibleStart : firstDay;
            var lastWeekDay = firstDay.AddDays(6);
            var lastVisibleDay = lastWeekDay > visibleEnd ? visibleEnd : lastWeekDay;
            var monthDay = Enumerable.Range(0, lastVisibleDay.DayNumber - firstVisibleDay.DayNumber + 1)
                .Select(firstVisibleDay.AddDays)
                .FirstOrDefault(day => day.Day == 1);
            if (monthDay == default && week != 0) continue;
            var labelDate = monthDay == default ? firstVisibleDay : monthDay;
            var x = CalendarLeft + week * (CellSize + CellGap);
            if (x < lastRight + 8) continue;
            var formatted = Formatted(labelDate.ToString("MMM", CultureInfo.CurrentCulture), 9.5, ResourceBrush("TextMuted"));
            context.DrawText(formatted, new WpfPoint(x, 2));
            lastRight = x + formatted.Width;
        }
    }

    private WpfRect DayBounds(DateOnly date)
    {
        var days = date.DayNumber - VisibleGridStart.DayNumber;
        var week = days / 7;
        var row = (days % 7 + 7) % 7;
        return new WpfRect(
            CalendarLeft + week * (CellSize + CellGap),
            TopInset + row * (CellSize + CellGap),
            CellSize,
            CellSize);
    }

    private WpfBrush IntensityBrush(long value, long maximum)
    {
        if (value <= 0)
        {
            var empty = ResourceBrush("PanelBorderStrong").Clone();
            empty.Opacity = .32;
            return empty;
        }
        var ratio = Math.Log10(value + 1d) / Math.Log10(maximum + 1d);
        var brush = ResourceBrush("AccentBlue").Clone();
        brush.Opacity = ratio switch
        {
            < .25 => .28,
            < .5 => .48,
            < .75 => .72,
            _ => 1
        };
        return brush;
    }

    private WpfBrush UntrackedBrush()
    {
        var brush = ResourceBrush("PanelBorderStrong").Clone();
        brush.Opacity = .12;
        return brush;
    }

    private int WeekCount()
    {
        if (_days.Count == 0) return 0;
        return (_days[^1].Date.DayNumber - _gridStart.DayNumber) / 7 + 1;
    }

    private double CalendarLeft => LeftInset;

    private int CalculateVisibleWeekCount()
    {
        var availableWidth = Math.Max(CellSize + CellGap, ActualWidth - LeftInset - RightInset);
        return Math.Clamp((int)Math.Floor(availableWidth / (CellSize + CellGap)), 1, Math.Max(WeekCount(), 1));
    }

    private DateOnly VisibleGridStart
        => _days.Count == 0
            ? default
            : StartOfWeek(_days[^1].Date).AddDays(-(VisibleWeekCount - 1) * 7);

    private void DrawCenteredText(DrawingContext context, string text)
    {
        var formatted = Formatted(text, 12, ResourceBrush("TextMuted"));
        context.DrawText(formatted, new WpfPoint((ActualWidth - formatted.Width) / 2, (ActualHeight - formatted.Height) / 2));
    }

    private void DrawText(DrawingContext context, string text, double size, WpfBrush brush, WpfPoint point)
        => context.DrawText(Formatted(text, size, brush), point);

    private FormattedText Formatted(string text, double size, WpfBrush brush)
        => new(
            text,
            CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight,
            new Typeface("Segoe UI"),
            size,
            brush,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

    private string AccessibleSummary()
        => _days.Count == 0
            ? Loc.T("Lifetime.Chart.NoData")
            : Loc.T("Lifetime.Chart.SelectionHelp") + " "
              + string.Join("; ", _days.Where(day => day.IsTracked).TakeLast(31).Select(DayTooltip));

    private static string DayTooltip(UsageMetricDay day)
    {
        if (!day.IsTracked)
            return day.Date > DateOnly.FromDateTime(DateTime.Today)
                ? Loc.T("Lifetime.Chart.FutureDay", day.Date.ToString("D", CultureInfo.CurrentCulture))
                : Loc.T("Lifetime.Chart.UntrackedDay", day.Date.ToString("D", CultureInfo.CurrentCulture));
        var tokens = Loc.T(
            "Lifetime.Chart.DayTooltip",
            day.Date.ToString("D", CultureInfo.CurrentCulture),
            day.Totals.InputTokens.ToString("N0"),
            day.Totals.CachedPromptTokens.ToString("N0"),
            day.Totals.GeneratedTokens.ToString("N0"),
            day.Totals.TotalTokens.ToString("N0"),
            day.Totals.CacheHitRate?.ToString("P1") ?? Loc.T("Lifetime.NotAvailable"));
        return day.Totals.RequestCounterObserved
            ? tokens + " " + Loc.T(
                "Lifetime.Chart.RequestsTooltip",
                day.Totals.RequestCount.ToString("N0"),
                day.Totals.FailedRequestCount.ToString("N0"))
            : tokens;
    }

    private long MetricValue(UsageMetricTotals totals)
        => Metric switch
        {
            UsageCalendarMetric.InputTokens => totals.InputTokens,
            UsageCalendarMetric.GeneratedTokens => totals.GeneratedTokens,
            UsageCalendarMetric.CachedPromptTokens => totals.CachedPromptTokens,
            UsageCalendarMetric.Requests => totals.RequestCounterObserved ? totals.RequestCount : 0,
            _ => totals.TotalTokens
        };

    private static UsageDateSelectionMode SelectionMode(ModifierKeys modifiers)
        => modifiers.HasFlag(ModifierKeys.Shift)
            ? modifiers.HasFlag(ModifierKeys.Control)
                ? UsageDateSelectionMode.AddRange
                : UsageDateSelectionMode.Range
            : modifiers.HasFlag(ModifierKeys.Control)
                ? UsageDateSelectionMode.Toggle
                : UsageDateSelectionMode.Replace;

    private WpfBrush ResourceBrush(string key)
        => TryFindResource(key) as WpfBrush ?? System.Windows.Media.Brushes.SlateGray;

    private static DateOnly StartOfWeek(DateOnly date)
    {
        var offset = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-offset);
    }

    private static WpfRect Inflate(WpfRect bounds, double amount)
        => new(bounds.X - amount, bounds.Y - amount, bounds.Width + amount * 2, bounds.Height + amount * 2);
}
