using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class RuntimeDashboardPageState
{
    public OverviewDashboardController? DashboardController { get; private set; }

    public WpfTextBox? RuntimeLogBox { get; private set; }

    public DataGrid? RuntimeMetricsGrid { get; private set; }

    public bool IsAvailable => DashboardController is not null;

    public IDisposable DeferDashboardUpdates()
        => DashboardController?.DeferUpdates() ?? EmptyScope.Instance;

    public void Apply(OverviewPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        DashboardController = controls.DashboardController;
        RuntimeLogBox = controls.RuntimeLogBox;
        RuntimeMetricsGrid = controls.RuntimeMetricsGrid;
    }

    public void SetMetricValue(string metricId, string value)
        => DashboardController?.SetMetricValue(metricId, value);

    public void ApplyMetricSummary(RuntimeMetricSummaryPresentation summary)
        => DashboardController?.ApplyMetricSummary(summary);

    public void ApplyHardwareSummary(string summary)
        => DashboardController?.ApplyHardwareSummary(summary);

    public Task ApplyHardwareSummaryAsync(HostHardwareSnapshot snapshot)
        => DashboardController?.ApplyHardwareSummaryAsync(snapshot) ?? Task.CompletedTask;

    public void ApplyObservedGpuEnergy(ObservedGpuEnergySnapshot? snapshot)
        => DashboardController?.ApplyObservedGpuEnergy(snapshot);

    public void SetRuntimeLogText(
        string text,
        bool followTail,
        bool forceFollowTail = false,
        bool forceTop = false)
    {
        var changed = TextBoxTailPresenter.SetText(RuntimeLogBox, text, followTail, forceFollowTail);
        if (!changed || !forceTop || RuntimeLogBox is null) return;
        RuntimeLogBox.CaretIndex = 0;
        RuntimeLogBox.ScrollToHome();
    }

    private sealed class EmptyScope : IDisposable
    {
        public static EmptyScope Instance { get; } = new();

        public void Dispose()
        {
        }
    }
}
