
namespace LocalLlmConsole.Services;

public sealed record WslDistroInfo(
    string Name,
    string State,
    string Version,
    bool IsDefault,
    bool IsUbuntu);

public sealed record WslEnvironmentReport(
    bool WslExeFound,
    bool WslWorking,
    string Status,
    string Details,
    string DefaultDistro,
    string RecommendedDistro,
    string RecommendedAction,
    IReadOnlyList<WslDistroInfo> Distros);

public sealed record WslToolSnapshot(
    bool CpuToolsInstalled,
    bool CudaToolsInstalled,
    bool VulkanToolsInstalled,
    string CpuSummary,
    string CudaSummary,
    string VulkanSummary,
    bool SyclToolsInstalled = false,
    string SyclSummary = "SYCL unknown");

public sealed class WslEnvironmentService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(4);

    public async Task<WslEnvironmentReport> DetectAsync(CancellationToken cancellationToken = default)
    {
        if (!WslExeExists())
        {
            return WslNotInstalledReport("wsl.exe was not found on this Windows installation.");
        }

        var statusTask = RunWslAsync(["--status"], cancellationToken);
        var listTask = RunWslAsync(["-l", "-v"], cancellationToken);
        await Task.WhenAll(statusTask, listTask);

        var status = await statusTask;
        var list = await listTask;
        if (LooksLikeWslNotInstalled(FirstText(status.Error, list.Error, status.Output, list.Output)))
            return WslNotInstalledReport(FirstText(status.Error, list.Error, status.Output, list.Output));

        var distros = ParseDistroList(list.Output).ToArray();
        var defaultDistro = distros.FirstOrDefault(distro => distro.IsDefault)?.Name
            ?? ParseDefaultDistro(status.Output)
            ?? "";
        var recommended = RecommendDistro(defaultDistro, distros);

        if (status.ExitCode != 0 && list.ExitCode != 0)
        {
            return new WslEnvironmentReport(
                WslExeFound: true,
                WslWorking: false,
                Status: "WSL installed but not ready",
                Details: FirstText(status.Error, list.Error, status.Output, list.Output, "Windows reported an error while checking WSL."),
                DefaultDistro: defaultDistro,
                RecommendedDistro: recommended,
                RecommendedAction: "Run WSL update or install Ubuntu, then restart the app if Windows asks for a reboot.",
                Distros: distros);
        }

        if (distros.Length == 0)
        {
            return new WslEnvironmentReport(
                WslExeFound: true,
                WslWorking: true,
                Status: "WSL installed, no Linux distro",
                Details: CleanOutput(status.Output),
                DefaultDistro: "",
                RecommendedDistro: "Ubuntu-24.04",
                RecommendedAction: "Install Ubuntu 24.04 or another Ubuntu distro before building or launching WSL runtimes.",
                Distros: distros);
        }

        var nonUbuntu = distros.All(distro => !distro.IsUbuntu);
        var action = nonUbuntu
            ? "A non-Ubuntu distro is installed. It can be selected for existing runtimes, but Ubuntu is recommended for app-guided setup."
            : "Use the detected Ubuntu distro, or install build tools inside it if runtime builds fail.";

        return new WslEnvironmentReport(
            WslExeFound: true,
            WslWorking: true,
            Status: "WSL ready",
            Details: CleanOutput(status.Output),
            DefaultDistro: defaultDistro,
            RecommendedDistro: recommended,
            RecommendedAction: action,
            Distros: distros);
    }

    public static IReadOnlyList<WslDistroInfo> ParseDistroList(string raw)
    {
        var rows = new List<WslDistroInfo>();
        foreach (var rawLine in CleanOutput(raw).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimEnd();
            if (string.IsNullOrWhiteSpace(line) || line.Contains("NAME", StringComparison.OrdinalIgnoreCase)) continue;

            var isDefault = line.TrimStart().StartsWith("*", StringComparison.Ordinal);
            line = isDefault ? line.TrimStart()[1..].TrimStart() : line.TrimStart();
            var match = Regex.Match(line, @"^(?<name>.+?)\s{2,}(?<state>\S+)\s+(?<version>\d+)\s*$");
            if (!match.Success) continue;

            var name = match.Groups["name"].Value.Trim();
            if (IsDockerDistro(name)) continue;
            rows.Add(new WslDistroInfo(
                name,
                match.Groups["state"].Value.Trim(),
                match.Groups["version"].Value.Trim(),
                isDefault,
                name.Contains("ubuntu", StringComparison.OrdinalIgnoreCase)));
        }

        return rows;
    }

    public static Dictionary<string, string> ParseKeyValueLines(string text)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf('=');
            if (index <= 0) continue;
            result[line[..index].Trim()] = line[(index + 1)..].Trim();
        }

        return result;
    }

    public static WslToolSnapshot UnknownToolSnapshot()
        => new(false, false, false, "CPU tools unknown", "CUDA unknown", "Vulkan unknown", false, "SYCL unknown");

    public static WslToolSnapshot ParseToolProbeOutput(string output)
    {
        var values = ParseKeyValueLines(output);
        var cpu = values.TryGetValue("CPU", out var cpuValue) && cpuValue == "1";
        var cuda = values.TryGetValue("CUDA", out var cudaValue) && cudaValue == "1";
        var vulkan = values.TryGetValue("VULKAN", out var vulkanValue) && vulkanValue == "1";
        var sycl = values.TryGetValue("SYCL", out var syclValue) && syclValue == "1";
        return new WslToolSnapshot(
            cpu,
            cuda,
            vulkan,
            values.GetValueOrDefault("CPU_SUMMARY", cpu ? "CPU OK" : "CPU missing"),
            values.GetValueOrDefault("CUDA_SUMMARY", cuda ? "CUDA OK" : "CUDA missing"),
            values.GetValueOrDefault("VULKAN_SUMMARY", vulkan ? "Vulkan OK" : "Vulkan missing"),
            sycl,
            values.GetValueOrDefault("SYCL_SUMMARY", sycl ? "SYCL OK" : "SYCL missing"));
    }

    public static string ToolSummary(WslToolSnapshot tools) => $"{tools.CpuSummary} | {tools.CudaSummary} | {tools.VulkanSummary} | {tools.SyclSummary}";

    public static string CpuToolsActionLabel(WslToolSnapshot tools)
        => tools.CpuToolsInstalled ? "Update CPU Tools" : "Install CPU Tools";

    public static string CudaToolsActionLabel(WslToolSnapshot tools)
        => tools.CudaToolsInstalled ? "Update CUDA" : "Install CUDA";

    public static string VulkanToolsActionLabel(WslToolSnapshot tools)
        => tools.VulkanToolsInstalled ? "Update Vulkan" : "Install Vulkan";

    public static string SyclToolsActionLabel(WslToolSnapshot tools)
        => tools.SyclToolsInstalled ? "Update oneAPI" : "Install oneAPI";

    public static bool LooksLikeWslNotInstalled(string text)
    {
        var normalized = CleanOutput(text).ToLowerInvariant();
        return normalized.Contains("windows subsystem for linux is not installed", StringComparison.Ordinal)
            || normalized.Contains("windows subsystem for linux has not been installed", StringComparison.Ordinal)
            || normalized.Contains("windows subsystem for linux has not been enabled", StringComparison.Ordinal)
            || normalized.Contains("wsl optional component is not enabled", StringComparison.Ordinal)
            || normalized.Contains("optional component is not enabled", StringComparison.Ordinal);
    }

    public static string CudaToolkitIncompleteMessage(string distroName, string detail)
    {
        var suffix = string.IsNullOrWhiteSpace(detail) ? "" : $"{Environment.NewLine}{Environment.NewLine}{detail}";
        return $"CUDA Toolkit was not complete inside WSL distro '{distroName}'. Install CPU Tools covers CPU builds only. Use WSL Linux > Install CUDA, or install the NVIDIA CUDA Toolkit/runtime development packages inside Ubuntu/WSL manually, then retry. NVIDIA's Windows display driver alone is not enough for a CUDA build.{suffix}";
    }

    public static string VulkanToolsIncompleteMessage(string distroName, string detail)
    {
        var suffix = string.IsNullOrWhiteSpace(detail) ? "" : $"{Environment.NewLine}{Environment.NewLine}{detail}";
        return $"Vulkan build tools were not ready inside WSL distro '{distroName}'. Use WSL Linux > Install Vulkan, or install Ubuntu packages libvulkan-dev, glslc, spirv-headers, vulkan-tools, and a usable Vulkan driver/device inside WSL, then retry.{suffix}";
    }

    public static string SyclToolsIncompleteMessage(string distroName, string detail)
    {
        var suffix = string.IsNullOrWhiteSpace(detail) ? "" : $"{Environment.NewLine}{Environment.NewLine}{detail}";
        return $"Intel oneAPI/SYCL tools were not ready inside WSL distro '{distroName}'. Use WSL Linux > Install Intel GPU Runtime and Install oneAPI, or install Intel Level Zero GPU runtime packages plus Intel oneAPI DPC++/MKL/DNNL packages manually, then retry.{suffix}";
    }

    public static string SelectedUbuntuDistroName(WslEnvironmentReport report, string configuredDistro)
    {
        if (!string.IsNullOrWhiteSpace(configuredDistro)
            && report.Distros.Any(distro => distro.Name.Equals(configuredDistro, StringComparison.OrdinalIgnoreCase) && distro.IsUbuntu))
            return configuredDistro;
        return report.Distros.FirstOrDefault(distro => distro.IsUbuntu)?.Name ?? "";
    }

    public static string SelectedDistroSummary(WslEnvironmentReport report, string configuredDistro)
    {
        var selected = report.Distros.FirstOrDefault(distro => distro.Name.Equals(configuredDistro, StringComparison.OrdinalIgnoreCase));
        if (selected is null) return string.IsNullOrWhiteSpace(configuredDistro) ? "None" : $"{configuredDistro} (missing)";
        return $"{selected.Name} | WSL {selected.Version} | {selected.State}";
    }

    public static string InstalledDistroSummary(WslEnvironmentReport report)
    {
        if (!report.WslExeFound) return "wsl.exe missing";
        var ubuntuCount = report.Distros.Count(distro => distro.IsUbuntu);
        return report.Distros.Count == 0
            ? "No distros installed"
            : $"{report.Distros.Count} distro(s), {ubuntuCount} Ubuntu";
    }

    private static string? ParseDefaultDistro(string raw)
    {
        foreach (var line in CleanOutput(raw).Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var index = line.IndexOf(':');
            if (index < 0) continue;
            var key = line[..index].Trim();
            if (!key.Equals("Default Distribution", StringComparison.OrdinalIgnoreCase)) continue;
            var value = line[(index + 1)..].Trim();
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }

        return null;
    }

    private static string RecommendDistro(string defaultDistro, IReadOnlyList<WslDistroInfo> distros)
    {
        if (!string.IsNullOrWhiteSpace(defaultDistro)
            && distros.Any(distro => distro.Name.Equals(defaultDistro, StringComparison.OrdinalIgnoreCase) && distro.IsUbuntu))
            return defaultDistro;

        if (!string.IsNullOrWhiteSpace(defaultDistro)
            && defaultDistro.Contains("ubuntu", StringComparison.OrdinalIgnoreCase)
            && distros.Any(distro => distro.Name.Equals(defaultDistro, StringComparison.OrdinalIgnoreCase)))
            return defaultDistro;

        var preferred = new[] { "Ubuntu-24.04", "Ubuntu-22.04", "Ubuntu" };
        foreach (var name in preferred)
        {
            var match = distros.FirstOrDefault(distro => distro.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match is not null) return match.Name;
        }

        return distros.FirstOrDefault(distro => distro.IsUbuntu)?.Name
            ?? (!string.IsNullOrWhiteSpace(defaultDistro)
                && distros.Any(distro => distro.Name.Equals(defaultDistro, StringComparison.OrdinalIgnoreCase))
                    ? defaultDistro
                    : null)
            ?? distros.FirstOrDefault()?.Name
            ?? "Ubuntu-24.04";
    }

    private static bool WslExeExists()
    {
        try
        {
            var wsl = HostExecutableResolver.WslExe();
            return Path.IsPathFullyQualified(wsl) && File.Exists(wsl);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsDockerDistro(string name)
        => name.Equals("docker-desktop", StringComparison.OrdinalIgnoreCase)
            || name.Equals("docker-desktop-data", StringComparison.OrdinalIgnoreCase);

    private static async Task<(int ExitCode, string Output, string Error)> RunWslAsync(string[] args, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = new CancellationTokenSource(ProbeTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(HostExecutableResolver.WslExe())
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                }
            };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            if (!process.Start()) return (-1, "", "Failed to start wsl.exe.");

            var outputTask = process.StandardOutput.ReadToEndAsync(linked.Token);
            var errorTask = process.StandardError.ReadToEndAsync(linked.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                try
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    Trace.TraceWarning($"Could not kill timed-out WSL probe: {ex.Message}");
                }
                return (-1, "", "Timed out while checking WSL.");
            }
            return (process.ExitCode, CleanOutput(await outputTask), CleanOutput(await errorTask));
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private static WslEnvironmentReport WslNotInstalledReport(string details)
        => new(
            WslExeFound: false,
            WslWorking: false,
            Status: "WSL not installed",
            Details: string.IsNullOrWhiteSpace(CleanOutput(details))
                ? "Windows Subsystem for Linux is not installed."
                : CleanOutput(details),
            DefaultDistro: "",
            RecommendedDistro: "Ubuntu-24.04",
            RecommendedAction: "Install WSL with Ubuntu 24.04.",
            Distros: []);

    private static string CleanOutput(string value)
        => (value ?? "").Replace("\0", "").Trim();

    private static string FirstText(params string[] values)
        => values.Select(CleanOutput).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? "";
}
