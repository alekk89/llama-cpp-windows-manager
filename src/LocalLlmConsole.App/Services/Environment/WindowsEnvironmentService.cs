namespace LocalLlmConsole.Services;

public sealed record WindowsToolSnapshot(
    bool GitInstalled,
    string GitPath,
    bool CMakeInstalled,
    string CMakePath,
    bool MsvcInstalled,
    string MsvcDetails,
    bool NvidiaDriverVisible,
    string NvidiaSmiPath,
    bool CudaToolsInstalled,
    string CudaDetails,
    bool VulkanToolsInstalled,
    string VulkanDetails,
    bool SyclToolsInstalled = false,
    string SyclDetails = "SYCL unknown",
    bool IntelGpuVisible = false)
{
    public bool CpuToolsInstalled => GitInstalled && CMakeInstalled && MsvcInstalled;
}

public sealed class WindowsEnvironmentService
{
    public WindowsToolSnapshot Detect()
    {
        var git = FindHostExecutable(HostExecutableResolver.GitExe, "git.exe");
        var cmake = FindHostExecutable(HostExecutableResolver.CMakeExe, "cmake.exe");
        var msvc = DetectMsvcToolchain();
        var nvidiaSmi = FindNvidiaSmi();
        var cuda = DetectCudaToolkit();
        var vulkan = DetectVulkanToolkit();
        var sycl = DetectSyclToolkit();

        return new WindowsToolSnapshot(
            !string.IsNullOrWhiteSpace(git),
            git,
            !string.IsNullOrWhiteSpace(cmake),
            cmake,
            !string.IsNullOrWhiteSpace(msvc),
            msvc,
            !string.IsNullOrWhiteSpace(nvidiaSmi),
            nvidiaSmi,
            cuda.Ready,
            cuda.Details,
            vulkan.Ready,
            vulkan.Details,
            sycl.Ready,
            sycl.Details,
            sycl.IntelGpuVisible);
    }

    public static string Status(WindowsToolSnapshot tools)
    {
        if (tools.CpuToolsInstalled && (tools.CudaToolsInstalled || tools.VulkanToolsInstalled || tools.SyclToolsInstalled))
            return "Windows GPU build tools ready";
        if (tools.CpuToolsInstalled)
            return "Windows CPU build tools ready";
        return "Windows build tools incomplete";
    }

    public static string ToolSummary(WindowsToolSnapshot tools)
    {
        var parts = new[]
        {
            tools.CpuToolsInstalled ? "CPU ready" : "CPU tools missing",
            tools.CudaToolsInstalled ? "CUDA ready" : "CUDA tools missing",
            tools.VulkanToolsInstalled ? "Vulkan ready" : "Vulkan tools missing",
            tools.SyclToolsInstalled ? "SYCL ready" : "SYCL tools missing"
        };
        return string.Join(" | ", parts);
    }

    public static string CpuToolsActionLabel(WindowsToolSnapshot tools)
        => tools.CpuToolsInstalled ? "Repair CPU Tools" : "Install CPU Tools";

    public static string CudaToolsActionLabel(WindowsToolSnapshot tools)
        => tools.CudaToolsInstalled ? "Repair CUDA" : "Install CUDA";

    public static string VulkanToolsActionLabel(WindowsToolSnapshot tools)
        => tools.VulkanToolsInstalled ? "Repair Vulkan" : "Install Vulkan";

    public static string SyclToolsActionLabel(WindowsToolSnapshot tools)
        => tools.SyclToolsInstalled ? "Repair oneAPI" : "Install oneAPI";

    public static string CpuDetails(WindowsToolSnapshot tools)
    {
        var details = new List<string>();
        details.Add(tools.GitInstalled ? $"Git: {tools.GitPath}" : "Git missing");
        details.Add(tools.CMakeInstalled ? $"CMake: {tools.CMakePath}" : "CMake missing");
        details.Add(tools.MsvcInstalled ? tools.MsvcDetails : "MSVC C++ build tools missing");
        return string.Join(Environment.NewLine, details);
    }

    private static (bool Ready, string Details) DetectCudaToolkit()
    {
        var cudaRoot = Environment.GetEnvironmentVariable("CUDA_PATH") ?? "";
        var extraDirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(cudaRoot))
            extraDirs.Add(Path.Combine(cudaRoot, "bin"));

        foreach (var root in CudaInstallRoots())
            extraDirs.Add(Path.Combine(root, "bin"));

        var nvcc = FindExecutable("nvcc.exe", extraDirs);
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(cudaRoot)) details.Add($"CUDA_PATH: {cudaRoot}");
        details.Add(string.IsNullOrWhiteSpace(nvcc) ? "nvcc.exe missing" : $"nvcc: {nvcc}");
        var ready = !string.IsNullOrWhiteSpace(nvcc);
        return (ready, string.Join(Environment.NewLine, details));
    }

    private static (bool Ready, string Details) DetectVulkanToolkit()
    {
        var sdkRoot = Environment.GetEnvironmentVariable("VULKAN_SDK") ?? "";
        var extraDirs = string.IsNullOrWhiteSpace(sdkRoot)
            ? Array.Empty<string>()
            : new[] { Path.Combine(sdkRoot, "Bin") };
        var vulkanInfo = FindExecutable("vulkaninfo.exe", extraDirs);
        var glslc = FindExecutable("glslc.exe", extraDirs);
        var details = new List<string>();
        details.Add(string.IsNullOrWhiteSpace(sdkRoot) ? "VULKAN_SDK missing" : $"VULKAN_SDK: {sdkRoot}");
        details.Add(string.IsNullOrWhiteSpace(vulkanInfo) ? "vulkaninfo.exe missing" : $"vulkaninfo: {vulkanInfo}");
        details.Add(string.IsNullOrWhiteSpace(glslc) ? "glslc.exe missing" : $"glslc: {glslc}");
        var ready = !string.IsNullOrWhiteSpace(sdkRoot)
            && Directory.Exists(sdkRoot)
            && !string.IsNullOrWhiteSpace(vulkanInfo)
            && !string.IsNullOrWhiteSpace(glslc);
        return (ready, string.Join(Environment.NewLine, details));
    }

    private static (bool Ready, string Details, bool IntelGpuVisible) DetectSyclToolkit()
    {
        var oneApiRoot = Environment.GetEnvironmentVariable("ONEAPI_ROOT") ?? "";
        var extraDirs = OneApiPathEntries();
        var icx = FindExecutable("icx.exe", extraDirs);
        var icpx = FindExecutable("icpx.exe", extraDirs);
        var syclLs = FindExecutable("sycl-ls.exe", extraDirs);
        var details = new List<string>();
        if (!string.IsNullOrWhiteSpace(oneApiRoot)) details.Add($"ONEAPI_ROOT: {oneApiRoot}");
        details.Add(string.IsNullOrWhiteSpace(icx) ? "icx.exe missing" : $"icx: {icx}");
        if (!string.IsNullOrWhiteSpace(icpx)) details.Add($"icpx: {icpx}");
        details.Add(string.IsNullOrWhiteSpace(syclLs) ? "sycl-ls.exe missing" : $"sycl-ls: {syclLs}");

        var deviceLine = "";
        if (!string.IsNullOrWhiteSpace(syclLs))
        {
            var probe = RunTool(syclLs, [], TimeSpan.FromSeconds(4));
            deviceLine = GpuStatusService.FirstSyclGpuLine(probe.Output);
            if (!string.IsNullOrWhiteSpace(probe.Error))
                details.Add($"sycl-ls error: {FirstLine(probe.Error)}");
        }

        var intelGpu = IsIntelGpuLine(deviceLine);
        if (!string.IsNullOrWhiteSpace(deviceLine))
            details.Add($"GPU: {deviceLine}");
        else
            details.Add("No level_zero GPU device reported by sycl-ls");

        var ready = !string.IsNullOrWhiteSpace(icx)
            && !string.IsNullOrWhiteSpace(syclLs)
            && !string.IsNullOrWhiteSpace(deviceLine);
        return (ready, string.Join(Environment.NewLine, details), intelGpu);
    }

    private static string DetectMsvcToolchain()
    {
        var cl = FindExecutable("cl.exe");
        if (!string.IsNullOrWhiteSpace(cl)) return $"MSVC cl.exe: {cl}";

        var vswhere = VsWherePath();
        if (string.IsNullOrWhiteSpace(vswhere)) return "";
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(vswhere)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (var arg in new[] { "-latest", "-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-property", "installationPath" })
                process.StartInfo.ArgumentList.Add(arg);
            if (!process.Start()) return "";
            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(5000) || process.ExitCode != 0 || string.IsNullOrWhiteSpace(output)) return "";
            var msvcRoot = Path.Combine(output, "VC", "Tools", "MSVC");
            return Directory.Exists(msvcRoot)
                ? $"Visual Studio C++ tools: {output}"
                : "";
        }
        catch
        {
            return "";
        }
    }

    private static string VsWherePath()
    {
        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (string.IsNullOrWhiteSpace(programFilesX86)) return "";
        var path = Path.Combine(programFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");
        return File.Exists(path) ? path : "";
    }

    private static string FindNvidiaSmi()
    {
        try
        {
            return HostExecutableResolver.NvidiaSmiExe();
        }
        catch
        {
            return "";
        }
    }

    private static string FindHostExecutable(Func<string> resolver, string executableName)
    {
        try
        {
            return resolver();
        }
        catch
        {
            return FindExecutable(executableName);
        }
    }

    private static IEnumerable<string> CudaInstallRoots()
    {
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles)) yield break;
        var cudaRoot = Path.Combine(programFiles, "NVIDIA GPU Computing Toolkit", "CUDA");
        if (!Directory.Exists(cudaRoot)) yield break;
        foreach (var directory in Directory.EnumerateDirectories(cudaRoot).OrderByDescending(directory => Path.GetFileName(directory) ?? "", StringComparer.OrdinalIgnoreCase))
            yield return directory;
    }

    public static IReadOnlyList<string> OneApiPathEntries()
    {
        var roots = OneApiInstallRoots().ToArray();
        var relativeDirs = new[]
        {
            Path.Combine("compiler", "latest", "bin"),
            Path.Combine("compiler", "latest", "windows", "bin"),
            Path.Combine("mkl", "latest", "bin"),
            Path.Combine("dnnl", "latest", "bin"),
            Path.Combine("tbb", "latest", "bin")
        };
        return roots
            .SelectMany(root => relativeDirs.Select(relative => Path.Combine(root, relative)))
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> OneApiInstallRoots()
    {
        var envRoot = Environment.GetEnvironmentVariable("ONEAPI_ROOT");
        if (!string.IsNullOrWhiteSpace(envRoot) && Directory.Exists(envRoot))
            yield return Path.GetFullPath(envRoot);

        foreach (var baseRoot in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles)
        })
        {
            if (string.IsNullOrWhiteSpace(baseRoot)) continue;
            var root = Path.Combine(baseRoot, "Intel", "oneAPI");
            if (Directory.Exists(root)) yield return Path.GetFullPath(root);
        }
    }

    private static string FindExecutable(string executableName, IEnumerable<string>? extraDirectories = null)
    {
        foreach (var directory in (extraDirectories ?? Array.Empty<string>()).Concat(PathEntries()))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory, executableName);
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return "";
    }

    private static bool IsIntelGpuLine(string line)
        => line.Contains("intel", StringComparison.OrdinalIgnoreCase)
            || line.Contains("arc", StringComparison.OrdinalIgnoreCase);

    private static string FirstLine(string text)
        => (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";

    private static (int ExitCode, string Output, string Error) RunTool(string fileName, IReadOnlyList<string> args, TimeSpan timeout)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                }
            };
            foreach (var arg in args) process.StartInfo.ArgumentList.Add(arg);
            if (!process.Start()) return (-1, "", "Failed to start tool.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit((int)timeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); }
                catch (Exception ex) { Trace.TraceWarning($"Could not kill timed-out Windows tool probe: {ex.Message}"); }
                return (-1, "", "Timed out.");
            }

            return (process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
        }
        catch (Exception ex)
        {
            return (-1, "", ex.Message);
        }
    }

    private static IEnumerable<string> PathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expanded)) continue;
            if (Directory.Exists(expanded)) yield return Path.GetFullPath(expanded);
        }
    }
}
