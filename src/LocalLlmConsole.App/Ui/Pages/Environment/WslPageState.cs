using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed class WslPageState
{
    public Grid? StatusMetric { get; private set; }

    public Grid? SelectedMetric { get; private set; }

    public Grid? InfoMetric { get; private set; }

    public Grid? ToolsMetric { get; private set; }

    public DataGrid? DistroGrid { get; private set; }

    public WslDistroRow? SelectedDistroRow => DistroGrid?.SelectedItem as WslDistroRow;

    public IEnumerable<WpfButton?> HelpButtons =>
    [
        InstallButton,
        CheckUpdatesButton,
        InstallUbuntuButton,
        CheckUbuntuUpdatesButton,
        InstallBuildToolsButton,
        InstallCudaToolkitButton,
        InstallVulkanToolsButton,
        InstallSyclRuntimeButton,
        InstallSyclOneApiButton
    ];

    private WpfButton? InstallButton { get; set; }

    private WpfButton? CheckUpdatesButton { get; set; }

    private WpfButton? DeleteButton { get; set; }

    private WpfButton? InstallUbuntuButton { get; set; }

    private WpfButton? CheckUbuntuUpdatesButton { get; set; }

    private WpfButton? DeleteUbuntuButton { get; set; }

    private WpfButton? InstallBuildToolsButton { get; set; }

    private WpfButton? DeleteBuildToolsButton { get; set; }

    private WpfButton? InstallCudaToolkitButton { get; set; }

    private WpfButton? DeleteCudaToolkitButton { get; set; }

    private WpfButton? InstallVulkanToolsButton { get; set; }

    private WpfButton? DeleteVulkanToolsButton { get; set; }

    private WpfButton? InstallSyclRuntimeButton { get; set; }

    private WpfButton? DeleteSyclRuntimeButton { get; set; }

    private WpfButton? InstallSyclOneApiButton { get; set; }

    private WpfButton? DeleteSyclOneApiButton { get; set; }

    public void Apply(WslPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        StatusMetric = controls.StatusMetric;
        SelectedMetric = controls.SelectedMetric;
        InfoMetric = controls.InfoMetric;
        ToolsMetric = controls.ToolsMetric;
        DistroGrid = controls.DistroGrid;
        InstallButton = controls.InstallButton;
        CheckUpdatesButton = controls.CheckUpdatesButton;
        DeleteButton = controls.DeleteButton;
        InstallUbuntuButton = controls.InstallUbuntuButton;
        CheckUbuntuUpdatesButton = controls.CheckUbuntuUpdatesButton;
        DeleteUbuntuButton = controls.DeleteUbuntuButton;
        InstallBuildToolsButton = controls.InstallBuildToolsButton;
        DeleteBuildToolsButton = controls.DeleteBuildToolsButton;
        InstallCudaToolkitButton = controls.InstallCudaToolkitButton;
        DeleteCudaToolkitButton = controls.DeleteCudaToolkitButton;
        InstallVulkanToolsButton = controls.InstallVulkanToolsButton;
        DeleteVulkanToolsButton = controls.DeleteVulkanToolsButton;
        InstallSyclRuntimeButton = controls.InstallSyclRuntimeButton;
        DeleteSyclRuntimeButton = controls.DeleteSyclRuntimeButton;
        InstallSyclOneApiButton = controls.InstallSyclOneApiButton;
        DeleteSyclOneApiButton = controls.DeleteSyclOneApiButton;
    }

    public void ReleaseView()
    {
        StatusMetric = null;
        SelectedMetric = null;
        InfoMetric = null;
        ToolsMetric = null;
        DistroGrid = null;
        InstallButton = null;
        CheckUpdatesButton = null;
        DeleteButton = null;
        InstallUbuntuButton = null;
        CheckUbuntuUpdatesButton = null;
        DeleteUbuntuButton = null;
        InstallBuildToolsButton = null;
        DeleteBuildToolsButton = null;
        InstallCudaToolkitButton = null;
        DeleteCudaToolkitButton = null;
        InstallVulkanToolsButton = null;
        DeleteVulkanToolsButton = null;
        InstallSyclRuntimeButton = null;
        DeleteSyclRuntimeButton = null;
        InstallSyclOneApiButton = null;
        DeleteSyclOneApiButton = null;
    }

    public void ApplyActionState(WslEnvironmentReport report, bool hasUbuntu, WslToolSnapshot tools)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(tools);

        SetButtonVisibility(InstallButton, !report.WslExeFound);
        SetButtonVisibility(CheckUpdatesButton, report.WslExeFound);
        SetButtonVisibility(DeleteButton, report.WslExeFound);
        SetButtonVisibility(InstallUbuntuButton, report.WslExeFound && !hasUbuntu);
        SetButtonVisibility(InstallBuildToolsButton, report.WslExeFound && hasUbuntu);
        SetButtonVisibility(InstallCudaToolkitButton, report.WslExeFound && hasUbuntu);
        SetButtonVisibility(InstallVulkanToolsButton, report.WslExeFound && hasUbuntu);
        SetButtonVisibility(InstallSyclRuntimeButton, report.WslExeFound && hasUbuntu);
        SetButtonVisibility(InstallSyclOneApiButton, report.WslExeFound && hasUbuntu);
        SetButtonVisibility(CheckUbuntuUpdatesButton, report.WslExeFound && hasUbuntu);
        SetButtonVisibility(DeleteUbuntuButton, report.WslExeFound && hasUbuntu);
        SetButtonVisibility(DeleteBuildToolsButton, report.WslExeFound && hasUbuntu && tools.CpuToolsInstalled);
        SetButtonVisibility(DeleteCudaToolkitButton, report.WslExeFound && hasUbuntu && tools.CudaToolsInstalled);
        SetButtonVisibility(DeleteVulkanToolsButton, report.WslExeFound && hasUbuntu && tools.VulkanToolsInstalled);
        SetButtonVisibility(DeleteSyclRuntimeButton, report.WslExeFound && hasUbuntu && tools.SyclToolsInstalled);
        SetButtonVisibility(DeleteSyclOneApiButton, report.WslExeFound && hasUbuntu && tools.SyclToolsInstalled);

        if (InstallBuildToolsButton is not null)
            InstallBuildToolsButton.Content = WslEnvironmentService.CpuToolsActionLabel(tools);
        if (InstallCudaToolkitButton is not null)
            InstallCudaToolkitButton.Content = WslEnvironmentService.CudaToolsActionLabel(tools);
        if (InstallVulkanToolsButton is not null)
            InstallVulkanToolsButton.Content = WslEnvironmentService.VulkanToolsActionLabel(tools);
        if (InstallSyclRuntimeButton is not null)
            InstallSyclRuntimeButton.Content = tools.SyclToolsInstalled ? "Update Intel GPU" : "Install Intel GPU";
        if (InstallSyclOneApiButton is not null)
            InstallSyclOneApiButton.Content = WslEnvironmentService.SyclToolsActionLabel(tools);
    }

    private static void SetButtonVisibility(WpfButton? button, bool visible)
    {
        if (button is null) return;
        button.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }
}
