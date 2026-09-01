using System.Collections.Concurrent;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed class ProfileFitCapabilityService
{
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(15);
    private readonly IProcessRunner _processRunner;
    private readonly ConcurrentDictionary<string, ProfileFitRuntimeCapability> _cache = new(StringComparer.OrdinalIgnoreCase);

    public ProfileFitCapabilityService(IProcessRunner processRunner)
        => _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));

    public async Task<ProfileFitRuntimeCapability> ProbeAsync(
        RuntimeRecord runtime,
        string wslDistro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var executable = ResolveExecutable(runtime);
        var key = $"{runtime.Id}|{runtime.UpdatedAt.UtcTicks}|{wslDistro}|{executable}";
        if (_cache.TryGetValue(key, out var cached)) return cached;
        if (runtime.Mode == RuntimeMode.Native && !File.Exists(executable))
            return Cache(key, Missing(runtime, executable));
        try
        {
            var start = BenchmarkRuntimeToolAdapter.CreateStartInfo(runtime, wslDistro, executable, ["--help"], "");
            var result = await _processRunner.RunAsync(start, ProbeTimeout, cancellationToken);
            var help = $"{result.Output}\n{result.Error}";
            if (result.ExitCode != 0 || !help.Contains("--fit-target", StringComparison.OrdinalIgnoreCase)
                                     || !help.Contains("--fit-ctx", StringComparison.OrdinalIgnoreCase))
                return Cache(key, new ProfileFitRuntimeCapability(runtime.Id, false, executable,
                    $"The selected runtime's llama-fit-params is missing or incompatible (exit code {result.ExitCode})."));
            return Cache(key, new ProfileFitRuntimeCapability(runtime.Id, true, executable, ""));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Cache(key, new ProfileFitRuntimeCapability(runtime.Id, false, executable, ex.Message));
        }
    }

    public static string ResolveExecutable(RuntimeRecord runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var server = Path.GetFullPath(runtime.ExecutablePath);
        var bin = Path.GetDirectoryName(server) ?? throw new InvalidOperationException("The runtime executable has no parent directory.");
        var root = Path.GetFileName(bin).Equals("bin", StringComparison.OrdinalIgnoreCase)
            ? Path.GetDirectoryName(bin) ?? bin
            : bin;
        var name = runtime.Mode == RuntimeMode.Native ? "llama-fit-params.exe" : "llama-fit-params";
        var candidate = Path.Combine(bin, name);
        var contained = PathContainmentGuard.ResolveDescendant(root, candidate, "llama-fit-params must remain inside the selected runtime.");
        PathContainmentGuard.RejectReparsePointAncestors(contained, true, "llama-fit-params cannot be reached through a reparse point.");
        return contained.Target;
    }

    private ProfileFitRuntimeCapability Cache(string key, ProfileFitRuntimeCapability value)
    {
        if (_cache.Count >= 32) _cache.Clear();
        _cache[key] = value;
        return value;
    }

    private static ProfileFitRuntimeCapability Missing(RuntimeRecord runtime, string executable)
        => new(runtime.Id, false, executable, "This runtime does not provide llama-fit-params beside llama-server.");
}
