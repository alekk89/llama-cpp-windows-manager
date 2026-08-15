using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed class LifetimePageState
{
    private DataGrid? MetricsGrid { get; set; }

    public void Apply(LifetimePageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        MetricsGrid = controls.MetricsGrid;
    }

}
