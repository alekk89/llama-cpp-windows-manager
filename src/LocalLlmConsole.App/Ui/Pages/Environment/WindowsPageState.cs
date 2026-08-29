using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed class WindowsPageState
{
    public Grid? StatusMetric { get; private set; }

    public Grid? CpuMetric { get; private set; }

    public Grid? CudaMetric { get; private set; }

    public Grid? VulkanMetric { get; private set; }

    public Grid? SyclMetric { get; private set; }

    public DataGrid? ToolsGrid { get; private set; }

    public WpfButton? InstallCpuToolsButton { get; private set; }

    public WpfButton? InstallCudaToolkitButton { get; private set; }

    public WpfButton? InstallVulkanToolsButton { get; private set; }

    public WpfButton? InstallSyclToolsButton { get; private set; }

    public IEnumerable<WpfButton?> HelpButtons =>
    [
        InstallCpuToolsButton,
        InstallCudaToolkitButton,
        InstallVulkanToolsButton,
        InstallSyclToolsButton
    ];

    public void Apply(WindowsPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        StatusMetric = controls.StatusMetric;
        CpuMetric = controls.CpuMetric;
        CudaMetric = controls.CudaMetric;
        VulkanMetric = controls.VulkanMetric;
        SyclMetric = controls.SyclMetric;
        ToolsGrid = controls.ToolsGrid;
        InstallCpuToolsButton = controls.InstallCpuToolsButton;
        InstallCudaToolkitButton = controls.InstallCudaToolkitButton;
        InstallVulkanToolsButton = controls.InstallVulkanToolsButton;
        InstallSyclToolsButton = controls.InstallSyclToolsButton;
    }

    public void ReleaseView()
    {
        StatusMetric = null;
        CpuMetric = null;
        CudaMetric = null;
        VulkanMetric = null;
        SyclMetric = null;
        ToolsGrid = null;
        InstallCpuToolsButton = null;
        InstallCudaToolkitButton = null;
        InstallVulkanToolsButton = null;
        InstallSyclToolsButton = null;
    }
}
