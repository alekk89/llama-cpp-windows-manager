using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed class LifetimePageState
{
    private LifetimePageControls? Controls { get; set; }

    public bool IsApplying { get; private set; }

    public LifetimeMetricsSelection Selection
    {
        get
        {
            if (Controls is null) return LifetimeMetricsSelection.Default;
            return new LifetimeMetricsSelection(
                Controls.RangeSelector.SelectedRange,
                (Controls.ModelFilter.SelectedItem as LifetimeMetricFilterOption)?.Id ?? "",
                (Controls.ProfileFilter.SelectedItem as LifetimeMetricFilterOption)?.Id ?? "",
                (Controls.RuntimeFilter.SelectedItem as LifetimeMetricFilterOption)?.Id ?? "",
                Controls.HistoryCalendar.SelectedDates,
                (Controls.CalendarMetric.SelectedItem as LifetimeCalendarMetricOption)?.Metric
                    ?? UsageCalendarMetric.TotalTokens);
        }
    }

    public void Apply(LifetimePageControls controls)
    {
        Controls = controls ?? throw new ArgumentNullException(nameof(controls));
    }

    public void ClearDateSelection()
        => Controls?.HistoryCalendar.ClearSelection();

    public void ApplyPresentation(LifetimeMetricsPresentation presentation)
    {
        ArgumentNullException.ThrowIfNull(presentation);
        if (Controls is null) return;

        IsApplying = true;
        try
        {
            SetFilter(Controls.ModelFilter, presentation.ModelOptions, presentation.Selection.ModelId);
            SetFilter(Controls.ProfileFilter, presentation.ProfileOptions, presentation.Selection.LaunchProfileId);
            SetFilter(Controls.RuntimeFilter, presentation.RuntimeOptions, presentation.Selection.RuntimeId);
            Controls.RangeSelector.SetRange(presentation.Selection.Range);
            Controls.TotalValue.Text = presentation.Total;
            Controls.InputValue.Text = presentation.Input;
            Controls.InputDetail.Text = presentation.InputDetail;
            Controls.InputDetail.ToolTip = presentation.InputDetail;
            Controls.OutputValue.Text = presentation.Output;
            Controls.CacheValue.Text = presentation.CacheHit;
            Controls.CacheDetail.Text = presentation.CacheDetail;
            Controls.CacheDetail.ToolTip = presentation.CacheDetail;
            Controls.Insights.Requests.Text = presentation.Requests;
            Controls.Insights.RequestsDetail.Text = presentation.RequestsDetail;
            Controls.Insights.RequestsDetail.ToolTip = presentation.RequestsDetail;
            Controls.Insights.ActiveDays.Text = presentation.ActiveDays;
            Controls.Insights.AveragePerActiveDay.Text = presentation.AveragePerActiveDay;
            Controls.Insights.PromptRate.Text = presentation.PromptRate;
            Controls.Insights.GenerationRate.Text = presentation.GenerationRate;
            Controls.Insights.PeakDay.Text = presentation.PeakDay;
            Controls.Insights.PeakDayDetail.Text = presentation.PeakDayDetail;
            Controls.Insights.PeakDayDetail.ToolTip = presentation.PeakDayDetail;
            Controls.HistoryNote.Text = presentation.HistoryNote;
            Controls.DateSelectionSummary.Text = presentation.DateSelectionSummary;
            Controls.ClearDateSelectionButton.Visibility = presentation.HasDateSelection
                ? System.Windows.Visibility.Visible
                : System.Windows.Visibility.Collapsed;
            SetCalendarMetric(Controls.CalendarMetric, presentation.Selection.CalendarMetric);
            Controls.HistoryCalendar.Metric = presentation.Selection.CalendarMetric;
            Controls.HistoryCalendar.SetData(presentation.CalendarDays, presentation.Selection.Dates);
            Controls.ResetVisibleButton.IsEnabled = presentation.HasRows;
            Controls.ResetVisibleButton.Content = string.IsNullOrWhiteSpace(presentation.Selection.ModelId)
                ? Loc.T("Lifetime.ResetAll")
                : Loc.T("Lifetime.ResetSelectedModel");
            Controls.ResetVisibleButton.ToolTip = string.IsNullOrWhiteSpace(presentation.Selection.ModelId)
                ? Loc.T("Lifetime.ResetAllTooltip")
                : Loc.T("Lifetime.ResetSelectedModelTooltip");
        }
        finally
        {
            IsApplying = false;
        }
    }

    private static void SetFilter(
        WpfComboBox combo,
        IReadOnlyList<LifetimeMetricFilterOption> options,
        string selectedId)
    {
        combo.ItemsSource = options;
        combo.SelectedItem = options.FirstOrDefault(option => option.Id.Equals(selectedId, StringComparison.OrdinalIgnoreCase))
            ?? options[0];
    }

    private static void SetCalendarMetric(WpfComboBox combo, UsageCalendarMetric selected)
    {
        if (combo.ItemsSource is not IEnumerable<LifetimeCalendarMetricOption> options) return;
        combo.SelectedItem = options.FirstOrDefault(option => option.Metric == selected);
    }
}
