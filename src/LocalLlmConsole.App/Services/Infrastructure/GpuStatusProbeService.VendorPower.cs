namespace LocalLlmConsole.Services;

public sealed partial class GpuStatusProbeService
{
    public async Task<string> AmdSmiSummaryAsync(CancellationToken cancellationToken = default)
    {
        var executable = _amdSmi.Resolve();
        if (string.IsNullOrWhiteSpace(executable)) return "Unavailable";
        try
        {
            var output = await RunProcessOutputAsync(
                executable,
                ["--rocm-smi"],
                TimeSpan.FromSeconds(3),
                cancellationToken);
            var lines = GpuStatusVendorPowerFormatter.FormatAmdSmi(output);
            return lines.Count == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"AMD SMI summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    public async Task<string> IntelXpuSmiSummaryAsync(CancellationToken cancellationToken = default)
    {
        var executable = _intelXpuSmi.Resolve();
        if (string.IsNullOrWhiteSpace(executable)) return "Unavailable";
        try
        {
            var output = await RunProcessOutputAsync(executable, [], TimeSpan.FromSeconds(3), cancellationToken);
            var lines = GpuStatusVendorPowerFormatter.FormatIntelXpuSmi(output);
            return lines.Count == 0 ? "Unavailable" : string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex)
        {
            Trace.TraceInformation($"Intel XPU-SMI summary unavailable: {ex.Message}");
            return "Unavailable";
        }
    }

    private static string FindWindowsSyclLs()
    {
        foreach (var directory in WindowsEnvironmentService.OneApiPathEntries().Concat(PathEntries()))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var candidate = Path.Combine(directory, "sycl-ls.exe");
            if (File.Exists(candidate)) return Path.GetFullPath(candidate);
        }

        return "";
    }

    private static string FindVendorExecutable(params string[] names)
    {
        foreach (var directory in PathEntries())
            foreach (var name in names)
            {
                var candidate = Path.Combine(directory, name);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
        return "";
    }

    private static IEnumerable<string> PathEntries()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var part in path.Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(part)) continue;
            var expanded = Environment.ExpandEnvironmentVariables(part.Trim().Trim('"'));
            if (!Path.IsPathFullyQualified(expanded) || !Directory.Exists(expanded)) continue;
            yield return Path.GetFullPath(expanded);
        }
    }

    public void InvalidateExecutableCache()
    {
        _windowsSyclLs.Invalidate();
        _nvidiaSmi.Invalidate();
        _windowsPowerShell.Invalidate();
        _amdSmi.Invalidate();
        _intelXpuSmi.Invalidate();
        _supportedNvidiaExtendedFields = null;
    }
}
