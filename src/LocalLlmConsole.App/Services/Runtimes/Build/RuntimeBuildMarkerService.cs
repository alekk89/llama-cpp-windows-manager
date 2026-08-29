namespace LocalLlmConsole.Services;

public sealed class RuntimeBuildMarkerService
{
    private readonly IProcessRunner _processRunner;
    private readonly object _gate = new();
    private readonly HashSet<string> _activeMarkers = new(StringComparer.OrdinalIgnoreCase);

    public RuntimeBuildMarkerService(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public int ActiveMarkerCount
    {
        get
        {
            lock (_gate)
                return _activeMarkers.Count;
        }
    }

    public void Register(string marker)
    {
        if (string.IsNullOrWhiteSpace(marker)) return;
        lock (_gate)
            _activeMarkers.Add(marker);
    }

    public void Unregister(string marker)
    {
        if (string.IsNullOrWhiteSpace(marker)) return;
        lock (_gate)
            _activeMarkers.Remove(marker);
    }

    public async Task CleanupActiveAsync(string distro)
    {
        string[] markers;
        lock (_gate)
            markers = _activeMarkers.ToArray();

        foreach (var marker in markers)
            await CleanupAsync(distro, marker);
    }

    public async Task CleanupInterruptedJobsAsync(IEnumerable<JobRecord> jobs, string defaultDistro)
    {
        foreach (var job in jobs)
        {
            if (!string.Equals(job.Kind, "runtime-build", StringComparison.OrdinalIgnoreCase)
                || job.Status != JobStatus.Interrupted)
                continue;

            try
            {
                var payload = RuntimeBuildJobService.ParsePayload(job.PayloadJson);
                if (string.IsNullOrWhiteSpace(payload?.ProcessMarker)) continue;
                var distro = string.IsNullOrWhiteSpace(payload.WslDistro) ? defaultDistro : payload.WslDistro;
                await CleanupAsync(distro, payload.ProcessMarker);
            }
            catch
            {
                // Stale job payloads are best-effort recovery only.
            }
        }
    }

    public async Task CleanupAsync(string distro, string marker)
    {
        if (string.IsNullOrWhiteSpace(distro) || string.IsNullOrWhiteSpace(marker)) return;
        try
        {
            var psi = new ProcessStartInfo(HostExecutableResolver.WslExe())
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            foreach (var arg in new[] { "-d", distro, "--", "bash", "-lc", CommandLineService.WslKillByEnvironmentMarkerCommand(marker) })
                psi.ArgumentList.Add(arg);
            _ = await _processRunner.RunAsync(psi, TimeSpan.FromSeconds(10));
        }
        catch
        {
            // Best-effort cleanup for cancelled WSL build jobs.
        }
    }
}
