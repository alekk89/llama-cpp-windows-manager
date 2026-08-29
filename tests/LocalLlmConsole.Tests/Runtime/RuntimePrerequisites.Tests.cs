using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimePrerequisitesTests : ManagerRegressionTestBase
{
    [Fact]
    public void VramAdmissionWarnsOrBlocksConservatively()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(modelPath, new byte[1024 * 1024]);
        var model = new ModelRecord("model", "Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var syclRuntime = runtime with { Name = "SYCL", Backend = RuntimeBackend.Sycl };
        var settings = AppSettings.CreateDefault(root) with { ContextSize = 131072, GpuLayers = AppSettings.DefaultGpuLayers };
        var service = new VramAdmissionService();

        Assert.Equal(VramAdmissionDecision.Warn, service.Assess(model, runtime, settings, null).Decision);
        Assert.Equal(VramAdmissionDecision.Warn, service.Assess(model, syclRuntime, settings, null).Decision);
        Assert.Equal(VramAdmissionDecision.Block, service.Assess(model, runtime, settings, new VramMemorySnapshot(0.1, 24)).Decision);
        Assert.Equal(VramAdmissionDecision.Allow, service.Assess(model, runtime, settings, new VramMemorySnapshot(8, 24)).Decision);
    }

    [Fact]
    public void RuntimeLaunchAdmissionServicePlansInteractiveAndGatewayAdmission()
    {
        var root = CreateTempRoot();
        var modelPath = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(modelPath, new byte[1024 * 1024]);
        var model = new ModelRecord("model", "Big Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var cudaRuntime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", DateTimeOffset.UtcNow);
        var cpuRuntime = cudaRuntime with { Backend = RuntimeBackend.Cpu };
        var settings = AppSettings.CreateDefault(root) with { ContextSize = 131072, GpuLayers = AppSettings.DefaultGpuLayers };
        var service = new RuntimeLaunchAdmissionService(new VramAdmissionService());

        var noRunning = service.Assess(cudaRuntime, model, settings, hasRunningGpuSessions: false, memory: null);
        var cpu = service.Assess(cpuRuntime, model, settings, hasRunningGpuSessions: true, memory: null);
        var warn = service.Assess(cudaRuntime, model, settings, hasRunningGpuSessions: true, memory: null);
        var block = service.Assess(cudaRuntime, model, settings, hasRunningGpuSessions: true, memory: new VramMemorySnapshot(0.1, 24));

        Assert.False(service.RequiresMemoryProbe(hasRunningGpuSessions: false, cudaRuntime));
        Assert.False(service.RequiresMemoryProbe(hasRunningGpuSessions: true, cpuRuntime));
        Assert.True(service.RequiresMemoryProbe(hasRunningGpuSessions: true, cudaRuntime));
        Assert.Equal(RuntimeLaunchAdmissionAction.Allow, noRunning.Action);
        Assert.Equal(RuntimeLaunchAdmissionAction.Allow, cpu.Action);
        Assert.Equal(RuntimeLaunchAdmissionAction.Warn, warn.Action);
        Assert.True(warn.RequiresInteractiveConfirmation);
        Assert.Contains("Load Big Model anyway", warn.InteractiveMessage, StringComparison.Ordinal);
        Assert.Contains("Gateway loading Big Model", warn.GatewayStatusMessage, StringComparison.Ordinal);
        Assert.Equal(RuntimeLaunchAdmissionAction.Block, block.Action);
        Assert.True(block.BlocksLaunch);
        Assert.Contains("Unload another model", block.InteractiveMessage, StringComparison.Ordinal);
        Assert.Contains("Auto-load gateway refused", block.GatewayBlockMessage, StringComparison.Ordinal);
        Assert.Contains("Single active model", block.GatewayBlockMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeLaunchPrerequisiteServiceValidatesWslSyclWindowsSyclAndPorts()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { WslDistro = "Ubuntu-24.04", Port = 8088 };
        var wslSyclRuntime = new RuntimeRecord("wsl-sycl", "WSL SYCL", RuntimeMode.Wsl, RuntimeBackend.Sycl, CreateRuntimeExecutable(root, "wsl", "llama-server"), "{}", DateTimeOffset.UtcNow);
        var nativeSyclRuntime = new RuntimeRecord("native-sycl", "Native SYCL", RuntimeMode.Native, RuntimeBackend.Sycl, CreateRuntimeExecutable(root, "native", "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var validWsl = new WslEnvironmentReport(
            WslExeFound: true,
            WslWorking: true,
            Status: "ready",
            Details: "",
            DefaultDistro: "Ubuntu-24.04",
            RecommendedDistro: "Ubuntu-24.04",
            RecommendedAction: "",
            Distros: [new WslDistroInfo("Ubuntu-24.04", "Running", "2", IsDefault: true, IsUbuntu: true)]);
        static WindowsToolSnapshot WindowsTools(bool syclReady) => new(
            GitInstalled: true,
            GitPath: "git.exe",
            CMakeInstalled: true,
            CMakePath: "cmake.exe",
            MsvcInstalled: true,
            MsvcDetails: "MSVC",
            NvidiaDriverVisible: false,
            NvidiaSmiPath: "",
            CudaToolsInstalled: false,
            CudaDetails: "",
            VulkanToolsInstalled: false,
            VulkanDetails: "",
            SyclToolsInstalled: syclReady,
            SyclDetails: syclReady ? "oneAPI ready" : "oneAPI missing");
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", ""));
        var loopbackProbes = new List<int>();
        var service = new RuntimeLaunchPrerequisiteService(
            _ => Task.FromResult(validWsl),
            () => WindowsTools(syclReady: true),
            runner,
            (port, _) =>
            {
                loopbackProbes.Add(port);
                return Task.FromResult(false);
            },
            () => "wsl.exe");

        await service.EnsureReadyAsync(new RuntimeLaunchPrerequisiteRequest(
            wslSyclRuntime,
            settings,
            (_, _) => Task.FromResult(false)),
            TestContext.Current.CancellationToken);

        Assert.Contains(runner.Commands, command => command.Contains("Ubuntu-24.04"));
        Assert.Equal([8088], loopbackProbes);

        var endpointOccupied = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.EnsureReadyAsync(new RuntimeLaunchPrerequisiteRequest(
                wslSyclRuntime,
                settings with { Port = 8090 },
                (_, _) => Task.FromResult(true)),
                TestContext.Current.CancellationToken));
        Assert.Contains("Port 8090 is already in use", endpointOccupied.Message, StringComparison.Ordinal);

        var wslV1 = new RuntimeLaunchPrerequisiteService(
            _ => Task.FromResult(validWsl with { Distros = [new WslDistroInfo("Ubuntu-24.04", "Stopped", "1", IsDefault: true, IsUbuntu: true)] }),
            () => WindowsTools(syclReady: true),
            runner,
            (_, _) => Task.FromResult(false),
            () => "wsl.exe");
        var wslVersion = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wslV1.EnsureReadyAsync(new RuntimeLaunchPrerequisiteRequest(wslSyclRuntime, settings, (_, _) => Task.FromResult(false)), TestContext.Current.CancellationToken));
        Assert.Contains("require WSL 2", wslVersion.Message, StringComparison.Ordinal);

        var failedPreflight = new RuntimeLaunchPrerequisiteService(
            _ => Task.FromResult(validWsl),
            () => WindowsTools(syclReady: true),
            new ScriptedProcessRunner(_ => new ProcessRunResult(1, "", "sycl-ls missing")),
            (_, _) => Task.FromResult(false),
            () => "wsl.exe");
        var preflight = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failedPreflight.EnsureReadyAsync(new RuntimeLaunchPrerequisiteRequest(wslSyclRuntime, settings, (_, _) => Task.FromResult(false)), TestContext.Current.CancellationToken));
        Assert.Contains("Intel oneAPI/SYCL tools were not ready", preflight.Message, StringComparison.Ordinal);
        Assert.Contains("sycl-ls missing", preflight.Message, StringComparison.Ordinal);

        var nativeMissingSycl = new RuntimeLaunchPrerequisiteService(
            _ => Task.FromResult(validWsl),
            () => WindowsTools(syclReady: false),
            runner,
            (_, _) => Task.FromResult(false),
            () => "wsl.exe");
        var native = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            nativeMissingSycl.EnsureReadyAsync(new RuntimeLaunchPrerequisiteRequest(nativeSyclRuntime, settings, (_, _) => Task.FromResult(false)), TestContext.Current.CancellationToken));
        Assert.Contains("Windows Intel oneAPI/SYCL tools are not ready", native.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeBuildPrerequisiteServiceValidatesWslBackendTools()
    {
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", ""));
        var service = new RuntimeBuildPrerequisiteService(new RuntimeToolPrerequisiteService(
            _ => Task.FromResult(ReadyWslReport()),
            () => WindowsBuildTools(),
            runner,
            () => "wsl.exe"));

        await service.EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Wsl, RuntimeBackend.Cpu, "Ubuntu-24.04"), TestContext.Current.CancellationToken);
        await service.EnsurePackageInstallReadyAsync(new RuntimePackagePreset("pkg", "Package", RuntimeBackend.Cpu, RuntimeMode.Wsl, "source"), "Ubuntu-24.04", TestContext.Current.CancellationToken);

        Assert.Empty(runner.StandardInputs);

        await service.EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Wsl, RuntimeBackend.Cuda, "Ubuntu-24.04"), TestContext.Current.CancellationToken);
        await service.EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Wsl, RuntimeBackend.Vulkan, "Ubuntu-24.04"), TestContext.Current.CancellationToken);
        await service.EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Wsl, RuntimeBackend.Sycl, "Ubuntu-24.04"), TestContext.Current.CancellationToken);

        Assert.Contains(runner.Commands, command => command.Contains("Ubuntu-24.04"));
        Assert.Equal(3, runner.StandardInputs.Count);
        Assert.Contains(WslSetupCommands.CudaToolkitPreflightCommand, runner.StandardInputs);
        Assert.Contains(WslSetupCommands.VulkanToolsPreflightCommand, runner.StandardInputs);
        Assert.Contains(WslSetupCommands.SyclToolsPreflightCommand, runner.StandardInputs);
    }

    [Fact]
    public async Task RuntimeBuildPrerequisiteServiceReportsWslFailures()
    {
        var wslV1 = new RuntimeBuildPrerequisiteService(new RuntimeToolPrerequisiteService(
            _ => Task.FromResult(ReadyWslReport(version: "1")),
            () => WindowsBuildTools(),
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", "")),
            () => "wsl.exe"));

        var wslVersion = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wslV1.EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Wsl, RuntimeBackend.Cpu, "Ubuntu-24.04"), TestContext.Current.CancellationToken));
        Assert.Contains("require WSL 2", wslVersion.Message, StringComparison.Ordinal);

        var failedPreflight = new RuntimeBuildPrerequisiteService(new RuntimeToolPrerequisiteService(
            _ => Task.FromResult(ReadyWslReport()),
            () => WindowsBuildTools(),
            new ScriptedProcessRunner(_ => new ProcessRunResult(1, "", "nvcc missing")),
            () => "wsl.exe"));

        var cuda = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            failedPreflight.EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Wsl, RuntimeBackend.Cuda, "Ubuntu-24.04"), TestContext.Current.CancellationToken));
        Assert.Contains("CUDA Toolkit was not complete", cuda.Message, StringComparison.Ordinal);
        Assert.Contains("nvcc missing", cuda.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RuntimeBuildPrerequisiteServiceValidatesWindowsBuildTools()
    {
        static RuntimeBuildPrerequisiteService Service(WindowsToolSnapshot tools) => new(new RuntimeToolPrerequisiteService(
            _ => Task.FromResult(ReadyWslReport()),
            () => tools,
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", "")),
            () => "wsl.exe"));

        var cpu = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(WindowsBuildTools(cpuReady: false)).EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Native, RuntimeBackend.Cpu, ""), TestContext.Current.CancellationToken));
        Assert.Contains("Windows CPU build tools are not ready", cpu.Message, StringComparison.Ordinal);

        var cuda = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(WindowsBuildTools(cudaReady: false)).EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Native, RuntimeBackend.Cuda, ""), TestContext.Current.CancellationToken));
        Assert.Contains("Windows CUDA Toolkit is not ready", cuda.Message, StringComparison.Ordinal);

        var vulkan = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(WindowsBuildTools(vulkanReady: false)).EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Native, RuntimeBackend.Vulkan, ""), TestContext.Current.CancellationToken));
        Assert.Contains("Windows Vulkan SDK is not ready", vulkan.Message, StringComparison.Ordinal);

        var sycl = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(WindowsBuildTools(syclReady: false)).EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Native, RuntimeBackend.Sycl, ""), TestContext.Current.CancellationToken));
        Assert.Contains("Windows Intel oneAPI/SYCL tools are not ready", sycl.Message, StringComparison.Ordinal);

        await Service(WindowsBuildTools()).EnsureReadyAsync(new RuntimeBuildPrerequisiteRequest(RuntimeMode.Native, RuntimeBackend.Sycl, ""), TestContext.Current.CancellationToken);
    }

}
