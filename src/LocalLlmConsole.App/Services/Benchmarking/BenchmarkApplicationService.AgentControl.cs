using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed record BenchmarkComparisonSummary(
    int MatchedWorkloads,
    int ImprovedWorkloads,
    int DegradedWorkloads,
    int UnchangedWorkloads,
    int EnvironmentMismatchWorkloads,
    double AveragePercentChange);

public sealed record BenchmarkRunComparison(
    string BaselineRunId,
    string BaselineName,
    string CandidateRunId,
    string CandidateName,
    BenchmarkComparisonSummary Summary,
    IReadOnlyList<BenchmarkComparisonRow> Rows);

public sealed partial class BenchmarkApplicationService
{
    public async Task<IReadOnlyList<BenchmarkRuntimeCapability>> RuntimeCapabilitiesAsync(
        string runtimeIdentifier = "",
        string wslDistro = "",
        CancellationToken cancellationToken = default)
    {
        var runtimes = await _store.ListRuntimesAsync();
        IReadOnlyList<RuntimeRecord> selected = string.IsNullOrWhiteSpace(runtimeIdentifier)
            ? runtimes
            : runtimes.Where(runtime =>
                    runtime.Id.Equals(runtimeIdentifier, StringComparison.OrdinalIgnoreCase)
                    || runtime.Name.Equals(runtimeIdentifier, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        if (selected.Count == 0 && !string.IsNullOrWhiteSpace(runtimeIdentifier))
            throw new KeyNotFoundException($"Runtime '{runtimeIdentifier}' was not found.");

        var capabilities = new List<BenchmarkRuntimeCapability>(selected.Count);
        foreach (var runtime in selected)
            capabilities.Add(await _capabilities.ProbeAsync(runtime, wslDistro, cancellationToken));
        return capabilities;
    }

    public async Task<BenchmarkRunComparison> CompareAsync(
        string baselineRunId,
        string candidateRunId,
        bool includePartialAttempts = false,
        CancellationToken cancellationToken = default)
    {
        if (baselineRunId.Equals(candidateRunId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Baseline and candidate benchmark runs must be different.");
        var baseline = await InspectAsync(baselineRunId, cancellationToken);
        var candidate = await InspectAsync(candidateRunId, cancellationToken);
        var baselineRows = await BenchmarkExportService.LoadAllAsync(_store, baselineRunId, includePartialAttempts, cancellationToken);
        var candidateRows = await BenchmarkExportService.LoadAllAsync(_store, candidateRunId, includePartialAttempts, cancellationToken);
        var rows = BenchmarkComparisonService.Compare(baselineRows, candidateRows, includePartialAttempts);
        const double unchangedTolerancePercent = 0.01;
        var summary = new BenchmarkComparisonSummary(
            rows.Count,
            rows.Count(row => row.PercentChange > unchangedTolerancePercent),
            rows.Count(row => row.PercentChange < -unchangedTolerancePercent),
            rows.Count(row => Math.Abs(row.PercentChange) <= unchangedTolerancePercent),
            rows.Count(row => !row.EnvironmentMatches),
            rows.Count == 0 ? 0 : rows.Average(row => row.PercentChange));
        return new BenchmarkRunComparison(
            baselineRunId,
            baseline.Payload.Plan.Name,
            candidateRunId,
            candidate.Payload.Plan.Name,
            summary,
            rows);
    }

    public async Task<BenchmarkRunSnapshot> DeleteAsync(
        string jobId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        if (!confirmed)
            throw new InvalidOperationException("Deleting a benchmark run requires explicit confirmation because its persisted results are also deleted.");
        var snapshot = await InspectAsync(jobId, cancellationToken);
        if (snapshot.Job.Status is JobStatus.Queued or JobStatus.Running or JobStatus.Paused)
            throw new InvalidOperationException("Active benchmark runs cannot be deleted. Cancel the run first.");
        await _store.DeleteJobAsync(snapshot.Job.Id);
        return snapshot;
    }
}
