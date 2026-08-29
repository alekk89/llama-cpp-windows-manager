using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public static class BenchmarkRuntimeToolAdapter
{
    public static ProcessStartInfo CreateStartInfo(
        RuntimeRecord runtime,
        string wslDistro,
        string benchmarkExecutable,
        IReadOnlyList<string> arguments,
        string processMarker)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(benchmarkExecutable);
        var startInfo = runtime.Mode == RuntimeMode.Wsl
            ? CreateWslStartInfo(runtime, wslDistro, benchmarkExecutable, arguments, processMarker)
            : CreateNativeStartInfo(runtime, benchmarkExecutable, arguments);
        startInfo.UseShellExecute = false;
        startInfo.CreateNoWindow = true;
        startInfo.WindowStyle = ProcessWindowStyle.Hidden;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.StandardOutputEncoding = Encoding.UTF8;
        startInfo.StandardErrorEncoding = Encoding.UTF8;
        return startInfo;
    }

    public static string RuntimeVisiblePath(RuntimeMode mode, string path)
        => mode == RuntimeMode.Wsl ? RuntimePackageWslFileService.WindowsPathToWslPath(path) : path;

    private static ProcessStartInfo CreateNativeStartInfo(
        RuntimeRecord runtime,
        string benchmarkExecutable,
        IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(benchmarkExecutable)
        {
            WorkingDirectory = Path.GetDirectoryName(benchmarkExecutable) ?? Environment.CurrentDirectory
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        if (runtime.Backend == RuntimeBackend.Sycl)
        {
            startInfo.Environment["ONEAPI_DEVICE_SELECTOR"] = "level_zero:gpu";
            startInfo.Environment["ZES_ENABLE_SYSMAN"] = "1";
            startInfo.Environment["SYCL_CACHE_PERSISTENT"] = "1";
            startInfo.Environment["UR_L0_ENABLE_RELAXED_ALLOCATION_LIMITS"] = "1";
            var oneApiPaths = WindowsEnvironmentService.OneApiPathEntries();
            var currentPath = startInfo.Environment.TryGetValue("PATH", out var path)
                ? path
                : Environment.GetEnvironmentVariable("PATH") ?? "";
            if (oneApiPaths.Count > 0)
                startInfo.Environment["PATH"] = string.Join(Path.PathSeparator, oneApiPaths.Append(currentPath).Where(value => !string.IsNullOrWhiteSpace(value)));
        }
        return startInfo;
    }

    private static ProcessStartInfo CreateWslStartInfo(
        RuntimeRecord runtime,
        string wslDistro,
        string benchmarkExecutable,
        IReadOnlyList<string> arguments,
        string processMarker)
    {
        if (string.IsNullOrWhiteSpace(wslDistro))
            throw new InvalidOperationException("The selected WSL runtime requires a distro name.");
        var executable = RuntimeVisiblePath(RuntimeMode.Wsl, benchmarkExecutable);
        var bin = WslDirectoryName(executable);
        var lib = WslSiblingDirectory(bin, "lib");
        var sycl = runtime.Backend == RuntimeBackend.Sycl
            ? "source /opt/intel/oneapi/setvars.sh --force >/dev/null 2>&1 || true; export ONEAPI_DEVICE_SELECTOR=level_zero:gpu; export ZES_ENABLE_SYSMAN=1; export SYCL_CACHE_PERSISTENT=1; export UR_L0_ENABLE_RELAXED_ALLOCATION_LIMITS=1; "
            : "";
        var libraryPath = $"{CommandLineService.BashQuote(bin)}:{CommandLineService.BashQuote(lib)}:\"${{LD_LIBRARY_PATH:-}}\"";
        var marker = string.IsNullOrWhiteSpace(processMarker) ? "" : $" -a {CommandLineService.BashQuote(processMarker)}";
        var command = $"{sycl}export LD_LIBRARY_PATH={libraryPath}; "
            + $"cd {CommandLineService.BashQuote(string.IsNullOrWhiteSpace(bin) ? "/" : bin)}; "
            + $"exec{marker} {CommandLineService.BashQuote(executable)} {string.Join(" ", arguments.Select(CommandLineService.BashQuote))}";
        var startInfo = new ProcessStartInfo(HostExecutableResolver.WslExe());
        foreach (var argument in new[] { "-d", wslDistro, "--", "bash", "-lc", command })
            startInfo.ArgumentList.Add(argument);
        return startInfo;
    }

    private static string WslDirectoryName(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var split = normalized.LastIndexOf('/');
        return split <= 0 ? "" : normalized[..split];
    }

    private static string WslSiblingDirectory(string path, string sibling)
    {
        var parent = WslDirectoryName(path);
        return string.IsNullOrWhiteSpace(parent) ? sibling : $"{parent.TrimEnd('/')}/{sibling}";
    }
}
