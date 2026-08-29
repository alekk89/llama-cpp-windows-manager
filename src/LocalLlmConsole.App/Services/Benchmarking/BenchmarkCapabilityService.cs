using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Collections.Concurrent;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed record BenchmarkRuntimeCapability(
    string RuntimeId,
    bool IsAvailable,
    string BenchmarkExecutablePath,
    IReadOnlySet<string> SupportedOptions,
    IReadOnlyList<string> AvailableDevices,
    string HelpFingerprint,
    string DeviceProbeWarning,
    string Error);

public sealed partial class BenchmarkCapabilityService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private readonly IProcessRunner _processRunner;
    private readonly ConcurrentDictionary<string, BenchmarkRuntimeCapability> _cache = new(StringComparer.OrdinalIgnoreCase);

    public BenchmarkCapabilityService(IProcessRunner processRunner)
        => _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<BenchmarkRuntimeCapability> ProbeAsync(
        RuntimeRecord runtime,
        string wslDistro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var executable = ResolveBenchmarkExecutable(runtime);
        var cacheKey = CacheKey(runtime, wslDistro, executable);
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;
        if (runtime.Mode == RuntimeMode.Native && !File.Exists(executable))
            return Cache(cacheKey, Unavailable(runtime.Id, executable, "This runtime does not contain llama-bench beside llama-server."));
        try
        {
            if (runtime.Mode == RuntimeMode.Wsl)
            {
                var accessibility = await CheckWslPathAsync(wslDistro, executable, requireExecutable: true, cancellationToken);
                if (!string.IsNullOrWhiteSpace(accessibility))
                    return Cache(cacheKey, Unavailable(runtime.Id, executable, accessibility));
            }
            var startInfo = BenchmarkRuntimeToolAdapter.CreateStartInfo(runtime, wslDistro, executable, ["--help"], "");
            var probe = await _processRunner.RunAsync(startInfo, ProbeTimeout, cancellationToken);
            var help = string.Join(Environment.NewLine, new[] { probe.Output, probe.Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
            if (probe.ExitCode != 0 || string.IsNullOrWhiteSpace(help))
                return Cache(cacheKey, Unavailable(runtime.Id, executable, $"llama-bench --help failed with exit code {probe.ExitCode}: {CommandLineService.FirstNonBlankLine(probe.Error)}"));
            var options = OptionPattern().Matches(help).Select(match => match.Value).ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!options.Contains("--model") || !options.Contains("--output"))
                return Cache(cacheKey, new BenchmarkRuntimeCapability(runtime.Id, false, executable, options, [], Hash(help), "", "The executable help did not identify a compatible llama-bench command surface."));
            var (devices, deviceWarning) = await ProbeDevicesAsync(runtime, wslDistro, executable, options, cancellationToken);
            return Cache(cacheKey, new BenchmarkRuntimeCapability(runtime.Id, true, executable, options, devices, Hash(help), deviceWarning, ""));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Cache(cacheKey, Unavailable(runtime.Id, executable, ex.Message));
        }
    }

    public async Task<string> ValidateModelPathAsync(
        RuntimeRecord runtime,
        string wslDistro,
        string modelPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        if (runtime.Mode == RuntimeMode.Wsl)
            return await CheckWslPathAsync(wslDistro, modelPath, requireExecutable: false, cancellationToken);
        try
        {
            await using var stream = new FileStream(modelPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1, FileOptions.Asynchronous);
            return stream.CanRead ? "" : $"Model file is not readable: {modelPath}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return $"Model file is not readable: {modelPath} ({ex.Message})";
        }
    }

    public static string ResolveBenchmarkExecutable(RuntimeRecord runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var server = Path.GetFullPath(runtime.ExecutablePath);
        var bin = Path.GetDirectoryName(server) ?? throw new InvalidOperationException("The runtime executable has no parent directory.");
        var root = Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(bin) ?? bin
            : bin;
        var name = runtime.Mode == RuntimeMode.Native ? "llama-bench.exe" : "llama-bench";
        var candidate = Path.Combine(bin, name);
        var contained = PathContainmentGuard.ResolveDescendant(root, candidate, "llama-bench must remain inside the selected runtime.");
        PathContainmentGuard.RejectReparsePointAncestors(contained, includeExistingTarget: true, "llama-bench cannot be reached through a reparse point.");
        return contained.Target;
    }

    private BenchmarkRuntimeCapability Cache(string key, BenchmarkRuntimeCapability capability)
    {
        if (_cache.Count >= 32) _cache.Clear();
        _cache[key] = capability;
        return capability;
    }

    private async Task<(IReadOnlyList<string> Devices, string Warning)> ProbeDevicesAsync(
        RuntimeRecord runtime,
        string wslDistro,
        string executable,
        IReadOnlySet<string> supportedOptions,
        CancellationToken cancellationToken)
    {
        if (!supportedOptions.Contains("--list-devices")) return ([], "This llama-bench build does not advertise --list-devices.");
        try
        {
            var startInfo = BenchmarkRuntimeToolAdapter.CreateStartInfo(runtime, wslDistro, executable, ["--list-devices"], "");
            var probe = await _processRunner.RunAsync(startInfo, ProbeTimeout, cancellationToken);
            if (probe.ExitCode != 0)
                return ([], $"llama-bench --list-devices exited with code {probe.ExitCode}.");
            var output = string.Join(Environment.NewLine, probe.Output, probe.Error);
            var devices = DevicePattern().Matches(output)
                .Select(match => match.Groups[1].Value)
                .Where(device => DeviceIdentifierPattern().IsMatch(device))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return (devices, devices.Length == 0 ? "llama-bench did not report any named devices." : "");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return ([], $"Could not list llama-bench devices: {ex.Message}");
        }
    }

    private static BenchmarkRuntimeCapability Unavailable(string runtimeId, string executable, string error)
        => new(runtimeId, false, executable, new HashSet<string>(), [], "", "", error);

    private async Task<string> CheckWslPathAsync(
        string distro,
        string windowsPath,
        bool requireExecutable,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(distro)) return "The selected WSL runtime does not identify a distro.";
        string visiblePath;
        try { visiblePath = BenchmarkRuntimeToolAdapter.RuntimeVisiblePath(RuntimeMode.Wsl, windowsPath); }
        catch (Exception ex) { return $"The path cannot be represented in WSL: {ex.Message}"; }
        var quoted = CommandLineService.BashQuote(visiblePath);
        var checks = requireExecutable ? $"test -f {quoted} && test -r {quoted} && test -x {quoted}" : $"test -f {quoted} && test -r {quoted}";
        var startInfo = new ProcessStartInfo(HostExecutableResolver.WslExe());
        foreach (var argument in new[] { "-d", distro, "--", "bash", "-lc", checks }) startInfo.ArgumentList.Add(argument);
        try
        {
            var result = await _processRunner.RunAsync(startInfo, ProbeTimeout, cancellationToken);
            if (result.ExitCode == 0) return "";
            var kind = requireExecutable ? "llama-bench is not a readable executable" : "model file is not readable";
            return $"The {kind} inside WSL distro '{distro}': {visiblePath}";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Could not validate '{visiblePath}' inside WSL distro '{distro}': {ex.Message}";
        }
    }

    private static string CacheKey(RuntimeRecord runtime, string distro, string executable)
    {
        long lastWrite = 0, length = 0;
        try
        {
            var info = new FileInfo(executable);
            lastWrite = info.LastWriteTimeUtc.Ticks;
            length = info.Length;
        }
        catch { }
        return $"{runtime.Id}|{runtime.Mode}|{distro}|{executable}|{length}|{lastWrite}";
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    [GeneratedRegex(@"(?<!\S)--?[a-zA-Z][a-zA-Z0-9-]*", RegexOptions.CultureInvariant)]
    private static partial Regex OptionPattern();

    [GeneratedRegex(@"(?m)^\s*([a-zA-Z][a-zA-Z0-9_.-]*)\s*:", RegexOptions.CultureInvariant)]
    private static partial Regex DevicePattern();

    [GeneratedRegex(@"^(?:CUDA|Vulkan|SYCL|Metal|OpenCL|HIP|MUSA|RPC)\d+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DeviceIdentifierPattern();
}
