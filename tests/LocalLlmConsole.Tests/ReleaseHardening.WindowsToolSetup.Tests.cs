using LocalLlmConsole.Models;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{


    [Fact]
    public void WindowsToolSetupWorkflowServiceBuildsInstallPlans()
    {
        var service = new WindowsToolSetupWorkflowService(
            new VisibleCommandLaunchService(_ => { }),
            () => new WindowsToolSnapshot(false, "", false, "", false, "", false, "", false, "", false, ""));

        var cpu = service.Plan(WindowsToolSetupAction.Cpu);
        var cuda = service.Plan(WindowsToolSetupAction.Cuda);
        var vulkan = service.Plan(WindowsToolSetupAction.Vulkan);
        var sycl = service.Plan(WindowsToolSetupAction.Sycl);

        Assert.Equal("Install Windows CPU tools", cpu.Title);
        Assert.Contains(WindowsSetupCommands.GitWingetId, cpu.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.VisualStudioBuildToolsWingetId, cpu.PowerShellScript, StringComparison.Ordinal);
        Assert.True(cpu.Elevated);
        Assert.Contains("CPU tool setup started", cpu.StartedStatus, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.CudaWingetId, cuda.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains("NVIDIA CUDA Toolkit", cuda.ConfirmationMessage, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.VulkanSdkWingetId, vulkan.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains(WindowsSetupCommands.OneApiBaseToolkitWingetId, sycl.PowerShellScript, StringComparison.Ordinal);
        Assert.Contains("Level Zero GPU", sycl.ConfirmationMessage, StringComparison.Ordinal);
    }


    [Fact]
    public void WindowsToolSetupApplicationServiceOwnsConfirmExecuteAndStatus()
    {
        var plan = new WindowsToolSetupPlan(
            WindowsToolSetupAction.Cpu,
            "Install CPU",
            "Install tools?",
            "Write-Host test",
            Elevated: true,
            "Started CPU tools.");
        var calls = new List<string>();
        var confirm = false;
        var service = new WindowsToolSetupApplicationService(
            action =>
            {
                calls.Add($"plan:{action}");
                return plan;
            },
            executedPlan => calls.Add($"execute:{executedPlan.Action}:{executedPlan.Elevated}"));
        WindowsToolSetupApplicationActions Actions()
            => new(
                confirmation =>
                {
                    calls.Add($"confirm:{confirmation.Title}");
                    return confirm;
                },
                status => calls.Add($"status:{status}"));

        var cancelled = service.Run(WindowsToolSetupAction.Cpu, Actions());

        confirm = true;

        var started = service.Run(WindowsToolSetupAction.Cpu, Actions());

        Assert.Equal(ToolSetupApplicationOutcome.Cancelled, cancelled);
        Assert.Equal(ToolSetupApplicationOutcome.Started, started);
        Assert.Equal([
            "plan:Cpu",
            "confirm:Install CPU",
            "plan:Cpu",
            "confirm:Install CPU",
            "execute:Cpu:True",
            "status:Started CPU tools."
        ], calls);
    }

    [Fact]
    public async Task WindowsToolSetupApplicationServiceOwnsRefreshSequence()
    {
        var snapshot = new WindowsToolSnapshot(
            GitInstalled: true,
            GitPath: "git.exe",
            CMakeInstalled: true,
            CMakePath: "cmake.exe",
            MsvcInstalled: true,
            MsvcDetails: "MSVC ready",
            NvidiaDriverVisible: false,
            NvidiaSmiPath: "",
            CudaToolsInstalled: false,
            CudaDetails: "CUDA missing",
            VulkanToolsInstalled: false,
            VulkanDetails: "Vulkan missing",
            SyclToolsInstalled: false,
            SyclDetails: "oneAPI missing");
        var calls = new List<string>();
        var service = new WindowsToolSetupApplicationService(
            _ => throw new InvalidOperationException("Not used by refresh."),
            _ => throw new InvalidOperationException("Not used by refresh."));

        var result = await service.RefreshAsync(new WindowsToolRefreshApplicationActions(
            async (label, action) =>
            {
                calls.Add($"busy:{label}");
                await action();
            },
            () =>
            {
                calls.Add("detect");
                return Task.FromResult(snapshot);
            },
            tools => calls.Add($"store:{tools.GitPath}"),
            tools => calls.Add($"populate:{tools.CpuToolsInstalled}"),
            status => calls.Add($"status:{status}")));

        Assert.Equal(snapshot, result);
        Assert.Equal([
            "busy:Detecting Windows build tools...",
            "detect",
            "store:git.exe",
            "populate:True",
            "status:Windows CPU build tools ready"
        ], calls);
    }


    [Fact]
    public async Task WindowsToolSetupWorkflowServiceRefreshesDetectedTools()
    {
        var snapshot = new WindowsToolSnapshot(
            GitInstalled: true,
            GitPath: "git.exe",
            CMakeInstalled: true,
            CMakePath: "cmake.exe",
            MsvcInstalled: true,
            MsvcDetails: "MSVC ready",
            NvidiaDriverVisible: true,
            NvidiaSmiPath: "nvidia-smi.exe",
            CudaToolsInstalled: true,
            CudaDetails: "CUDA ready",
            VulkanToolsInstalled: false,
            VulkanDetails: "Vulkan missing",
            SyclToolsInstalled: true,
            SyclDetails: "oneAPI ready");
        var calls = 0;
        var service = new WindowsToolSetupWorkflowService(new VisibleCommandLaunchService(_ => { }), () =>
        {
            calls++;
            return snapshot;
        });

        var detected = await service.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal(snapshot, detected);
        Assert.Equal(1, calls);
        Assert.True(detected.CpuToolsInstalled);
        Assert.True(detected.CudaToolsInstalled);
        Assert.False(detected.VulkanToolsInstalled);
        Assert.True(detected.SyclToolsInstalled);
    }


}
