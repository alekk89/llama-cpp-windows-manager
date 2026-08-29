namespace LocalLlmConsole.Services;

public sealed class RuntimeLaunchOptionDiscoveryService
{
    private readonly IProcessRunner _processRunner;
    private readonly RuntimeLaunchOptionDiagnosticsService? _diagnostics;
    private readonly Dictionary<string, IReadOnlyList<RuntimeLaunchOptionDefinition>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public RuntimeLaunchOptionDiscoveryService(
        IProcessRunner processRunner,
        RuntimeLaunchOptionDiagnosticsService? diagnostics = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _diagnostics = diagnostics;
    }

    public async Task<IReadOnlyList<RuntimeLaunchOptionDefinition>> DiscoverAsync(
        RuntimeChoice runtime,
        string wslDistro,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        var availability = RuntimeAvailabilityService.Inspect(runtime.ExecutablePath);
        if (!availability.IsAvailable)
        {
            await RecordDiagnosticAsync(runtime, wslDistro, null, "", 0, 0, "unavailable", availability.Reason, cancellationToken);
            throw new InvalidOperationException($"{availability.Reason} Repair or reinstall the runtime to discover its launch settings.");
        }
        var cacheKey = $"{runtime.Id}|{runtime.Mode}|{runtime.ExecutablePath}|{wslDistro}|{LastWrite(runtime)}";
        if (runtime.Mode == RuntimeMode.Native && _cache.TryGetValue(cacheKey, out var cached)) return cached;

        var startInfo = runtime.Mode == RuntimeMode.Wsl
            ? WslStartInfo(runtime.ExecutablePath, wslDistro)
            : NativeStartInfo(runtime.ExecutablePath);
        var result = await _processRunner.RunAsync(startInfo, TimeSpan.FromSeconds(15), cancellationToken);
        var combined = string.Join(Environment.NewLine, new[] { result.Output, result.Error }.Where(value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(combined))
        {
            await RecordDiagnosticAsync(runtime, wslDistro, result.ExitCode, combined, 0, 0, "empty-help", "The runtime returned no help text.", cancellationToken);
            throw new InvalidOperationException($"{runtime.DisplayName} returned no help text (exit code {result.ExitCode}).");
        }

        var discovered = RuntimeLaunchHelpParser.Parse(combined);
        if (discovered.Count == 0)
        {
            const string message = "The runtime help format changed or is unsupported; no recognizable launch options were found.";
            await RecordDiagnosticAsync(runtime, wslDistro, result.ExitCode, combined, 0, 0, "unrecognized-help", message, cancellationToken);
            throw new InvalidOperationException($"{runtime.DisplayName}: {message}");
        }

        var parsed = discovered.Where(RuntimeLaunchOptionPolicy.CanRender).ToArray();
        await RecordDiagnosticAsync(runtime, wslDistro, result.ExitCode, combined, discovered.Count, parsed.Length, "success", "Runtime launch options discovered.", cancellationToken);
        if (runtime.Mode == RuntimeMode.Native) _cache[cacheKey] = parsed;
        return parsed;
    }

    private async Task RecordDiagnosticAsync(
        RuntimeChoice runtime,
        string wslDistro,
        int? exitCode,
        string help,
        int parsedCount,
        int renderedCount,
        string status,
        string message,
        CancellationToken cancellationToken)
    {
        if (_diagnostics is null) return;
        await _diagnostics.RecordAsync(
            runtime,
            wslDistro,
            exitCode,
            help,
            parsedCount,
            renderedCount,
            status,
            message,
            cancellationToken);
    }

    private static ProcessStartInfo NativeStartInfo(string executable)
    {
        var startInfo = new ProcessStartInfo(executable);
        startInfo.ArgumentList.Add("--help");
        return startInfo;
    }

    private static ProcessStartInfo WslStartInfo(string executable, string distro)
    {
        var startInfo = new ProcessStartInfo(HostExecutableResolver.WslExe());
        if (!string.IsNullOrWhiteSpace(distro))
        {
            startInfo.ArgumentList.Add("-d");
            startInfo.ArgumentList.Add(distro);
        }
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add(RuntimePackageWslFileService.WindowsPathToWslPath(executable));
        startInfo.ArgumentList.Add("--help");
        return startInfo;
    }

    private static long LastWrite(RuntimeChoice runtime)
    {
        if (runtime.Mode != RuntimeMode.Native || !File.Exists(runtime.ExecutablePath)) return 0;
        return File.GetLastWriteTimeUtc(runtime.ExecutablePath).Ticks;
    }
}
