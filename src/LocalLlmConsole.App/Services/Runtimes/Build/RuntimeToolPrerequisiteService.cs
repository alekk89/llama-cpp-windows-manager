namespace LocalLlmConsole.Services;

public delegate Task<WslEnvironmentReport> WslEnvironmentReportReader(CancellationToken cancellationToken);

public delegate WindowsToolSnapshot WindowsToolSnapshotReader();

public sealed class RuntimeToolPrerequisiteService
{
    private readonly WslEnvironmentReportReader _readWslReportAsync;
    private readonly WindowsToolSnapshotReader _readWindowsTools;
    private readonly IProcessRunner _processRunner;
    private readonly Func<string> _wslExecutablePath;

    public RuntimeToolPrerequisiteService(
        WslEnvironmentService wslEnvironment,
        WindowsEnvironmentService windowsEnvironment,
        IProcessRunner processRunner)
        : this(
            cancellationToken => wslEnvironment.DetectAsync(cancellationToken),
            windowsEnvironment.Detect,
            processRunner,
            HostExecutableResolver.WslExe)
    {
    }

    public RuntimeToolPrerequisiteService(
        WslEnvironmentReportReader readWslReportAsync,
        WindowsToolSnapshotReader readWindowsTools,
        IProcessRunner processRunner,
        Func<string>? wslExecutablePath = null)
    {
        _readWslReportAsync = readWslReportAsync ?? throw new ArgumentNullException(nameof(readWslReportAsync));
        _readWindowsTools = readWindowsTools ?? throw new ArgumentNullException(nameof(readWindowsTools));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _wslExecutablePath = wslExecutablePath ?? HostExecutableResolver.WslExe;
    }

    public async Task EnsureWslDistroReadyAsync(string distroName, CancellationToken cancellationToken = default)
    {
        var report = await _readWslReportAsync(cancellationToken);
        if (!report.WslExeFound)
            throw new InvalidOperationException("WSL is not installed or wsl.exe was not found.");
        if (!report.WslWorking)
            throw new InvalidOperationException($"WSL is installed but not ready: {report.Details}");

        var distro = report.Distros.FirstOrDefault(item => item.Name.Equals(distroName, StringComparison.OrdinalIgnoreCase));
        if (distro is null)
            throw new InvalidOperationException($"WSL distro '{distroName}' is not installed. Select an installed Ubuntu distro or install Ubuntu first.");
        if (!string.Equals(distro.Version, "2", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"WSL distro '{distroName}' is version {distro.Version}. llama.cpp runtimes require WSL 2.");
    }

    public async Task EnsureWslBuildToolsReadyAsync(RuntimeBackend backend, string distroName, CancellationToken cancellationToken = default)
    {
        switch (backend)
        {
            case RuntimeBackend.Cpu:
                return;
            case RuntimeBackend.Cuda:
                await EnsureWslPreflightAsync(distroName, WslSetupCommands.CudaToolkitPreflightCommand, WslEnvironmentService.CudaToolkitIncompleteMessage, cancellationToken);
                return;
            case RuntimeBackend.Vulkan:
                await EnsureWslPreflightAsync(distroName, WslSetupCommands.VulkanToolsPreflightCommand, WslEnvironmentService.VulkanToolsIncompleteMessage, cancellationToken);
                return;
            case RuntimeBackend.Sycl:
                await EnsureWslSyclToolsReadyAsync(distroName, cancellationToken);
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unsupported runtime backend.");
        }
    }

    public async Task EnsureWslSyclToolsReadyAsync(string distroName, CancellationToken cancellationToken = default)
        => await EnsureWslPreflightAsync(distroName, WslSetupCommands.SyclToolsPreflightCommand, WslEnvironmentService.SyclToolsIncompleteMessage, cancellationToken);

    public void EnsureWindowsBuildToolsReady(RuntimeBackend backend)
    {
        var tools = _readWindowsTools();
        if (!tools.CpuToolsInstalled)
            throw new InvalidOperationException($"Windows CPU build tools are not ready. Open Windows and install CPU Tools first.{Environment.NewLine}{WindowsEnvironmentService.CpuDetails(tools)}");
        if (backend == RuntimeBackend.Cuda && !tools.CudaToolsInstalled)
            throw new InvalidOperationException($"Windows CUDA Toolkit is not ready. Open Windows and install CUDA first.{Environment.NewLine}{tools.CudaDetails}");
        if (backend == RuntimeBackend.Vulkan && !tools.VulkanToolsInstalled)
            throw new InvalidOperationException($"Windows Vulkan SDK is not ready. Open Windows and install Vulkan first.{Environment.NewLine}{tools.VulkanDetails}");
        if (backend == RuntimeBackend.Sycl && !tools.SyclToolsInstalled)
            throw new InvalidOperationException($"Windows Intel oneAPI/SYCL tools are not ready. Open Windows and install oneAPI first.{Environment.NewLine}{tools.SyclDetails}");
    }

    public void EnsureWindowsSyclToolsReady()
    {
        var tools = _readWindowsTools();
        if (!tools.SyclToolsInstalled)
            throw new InvalidOperationException($"Windows Intel oneAPI/SYCL tools are not ready. Open Windows and install oneAPI first.{Environment.NewLine}{tools.SyclDetails}");
    }

    private async Task EnsureWslPreflightAsync(
        string distroName,
        string script,
        Func<string, string, string> incompleteMessage,
        CancellationToken cancellationToken)
    {
        var result = await RunWslPreflightAsync(distroName, script, cancellationToken);
        if (result.ExitCode == 0) return;

        var detail = CommandLineService.FirstNonBlankLine(result.Error);
        if (string.IsNullOrWhiteSpace(detail)) detail = CommandLineService.FirstNonBlankLine(result.Output);
        throw new InvalidOperationException(incompleteMessage(distroName, detail));
    }

    private async Task<ProcessRunResult> RunWslPreflightAsync(string distroName, string script, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(_wslExecutablePath())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-d", distroName, "--", "bash", "-s" })
            psi.ArgumentList.Add(arg);

        return await _processRunner.RunAsync(psi, TimeSpan.FromSeconds(30), cancellationToken, standardInput: script);
    }
}
