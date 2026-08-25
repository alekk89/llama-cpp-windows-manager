namespace LocalLlmConsole.Services;

public sealed partial class GpuStatusProbeService
{
    private readonly object _processProbeGate = new();
    private readonly Dictionary<int, ProcessCpuObservation> _processCpuObservations = [];

    public Task<string> ProcessSummaryAsync(int processId, CancellationToken cancellationToken = default)
    {
        if (processId <= 0) return Task.FromResult("Unavailable");
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var process = Process.GetProcessById(processId);
            process.Refresh();
            var now = Stopwatch.GetTimestamp();
            var totalCpu = process.TotalProcessorTime;
            var memoryGiB = Math.Max(0, process.PrivateMemorySize64) / (1024d * 1024 * 1024);
            double? cpuPercent = null;
            lock (_processProbeGate)
            {
                if (_processCpuObservations.TryGetValue(processId, out var previous))
                {
                    var elapsedSeconds = Stopwatch.GetElapsedTime(previous.Timestamp, now).TotalSeconds;
                    var cpuSeconds = (totalCpu - previous.TotalCpu).TotalSeconds;
                    if (elapsedSeconds > 0 && cpuSeconds >= 0)
                        cpuPercent = Math.Clamp(
                            100 * cpuSeconds / (elapsedSeconds * Math.Max(1, Environment.ProcessorCount)), 0, 100);
                }
                _processCpuObservations[processId] = new(totalCpu, now);
                foreach (var staleProcessId in _processCpuObservations.Keys.Where(id => id != processId).ToArray())
                    _processCpuObservations.Remove(staleProcessId);
            }

            var observations = new List<string>();
            if (cpuPercent is { } cpu && double.IsFinite(cpu)) observations.Add($"{cpu:0.#}% CPU");
            if (double.IsFinite(memoryGiB)) observations.Add($"{memoryGiB:0.00} GiB private RAM");
            return Task.FromResult(observations.Count == 0
                ? "Unavailable"
                : $"Process: {string.Join(" | ", observations)}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            Trace.TraceInformation($"llama-server process summary unavailable: {ex.Message}");
            lock (_processProbeGate) _processCpuObservations.Remove(processId);
            return Task.FromResult("Unavailable");
        }
    }

    private readonly record struct ProcessCpuObservation(TimeSpan TotalCpu, long Timestamp);
}
