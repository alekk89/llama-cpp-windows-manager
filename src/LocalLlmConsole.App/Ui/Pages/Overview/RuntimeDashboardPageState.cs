using System.Windows.Controls;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class RuntimeDashboardPageState
{
    public Grid? ModelMetric { get; private set; }

    public Grid? GpuMetric { get; private set; }

    public Grid? KvCacheMetric { get; private set; }

    public Grid? TokensMetric { get; private set; }

    public TextBlock? TokensLastKnown { get; private set; }

    public Grid? MtpTokensMetric { get; private set; }

    public Grid? SlotsMetric { get; private set; }

    public MetricSparkline? TokensGraph { get; private set; }

    public MetricSparkline? MtpTokensGraph { get; private set; }

    public MetricSparkline? KvCacheGraph { get; private set; }

    public WpfTextBox? RuntimeLogBox { get; private set; }

    public DataGrid? RuntimeMetricsGrid { get; private set; }

    public WpfProgressBar? ModelProgress { get; private set; }

    public void Apply(OverviewPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        ModelMetric = controls.RuntimeDashboardModel;
        GpuMetric = controls.RuntimeDashboardGpu;
        KvCacheMetric = controls.RuntimeDashboardKvCache;
        TokensMetric = controls.RuntimeDashboardTokens;
        TokensLastKnown = controls.RuntimeDashboardTokensLastKnown;
        MtpTokensMetric = controls.RuntimeDashboardMtpTokens;
        SlotsMetric = controls.RuntimeDashboardSlots;
        TokensGraph = controls.RuntimeDashboardTokensGraph;
        MtpTokensGraph = controls.RuntimeDashboardMtpTokensGraph;
        KvCacheGraph = controls.RuntimeDashboardKvCacheGraph;
        RuntimeLogBox = controls.RuntimeLogBox;
        RuntimeMetricsGrid = controls.RuntimeMetricsGrid;
        ModelProgress = null;
    }

    public void SetRuntimeLogText(string text, bool followTail)
        => TextBoxTailPresenter.SetText(RuntimeLogBox, text, followTail);
}
