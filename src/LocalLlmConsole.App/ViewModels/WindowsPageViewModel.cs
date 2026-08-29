using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class WindowsPageViewModel
{
    public ObservableCollection<WindowsToolRow> Rows { get; } = new();

    public void ReplaceToolRows(WindowsToolSnapshot tools)
    {
        Rows.Clear();
        Rows.Add(new WindowsToolRow
        {
            Toolchain = "CPU tools",
            Status = tools.CpuToolsInstalled ? "Ready" : "Incomplete",
            Details = WindowsEnvironmentService.CpuDetails(tools)
        });
        Rows.Add(new WindowsToolRow
        {
            Toolchain = "CUDA tools",
            Status = tools.CudaToolsInstalled ? "Ready" : "Incomplete",
            Details = tools.CudaDetails,
            Driver = tools.NvidiaDriverVisible ? $"NVIDIA driver visible: {tools.NvidiaSmiPath}" : "NVIDIA driver not detected by nvidia-smi"
        });
        Rows.Add(new WindowsToolRow
        {
            Toolchain = "Vulkan tools",
            Status = tools.VulkanToolsInstalled ? "Ready" : "Incomplete",
            Details = tools.VulkanDetails
        });
        Rows.Add(new WindowsToolRow
        {
            Toolchain = "Intel oneAPI",
            Status = tools.SyclToolsInstalled ? "Ready" : "Incomplete",
            Details = tools.SyclDetails,
            Driver = tools.IntelGpuVisible ? "Intel GPU visible to sycl-ls" : "Intel GPU not detected by sycl-ls"
        });
    }
}
