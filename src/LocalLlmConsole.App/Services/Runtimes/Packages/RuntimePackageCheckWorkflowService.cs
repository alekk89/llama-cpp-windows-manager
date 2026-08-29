namespace LocalLlmConsole.Services;

public sealed record RuntimePackageCheckWorkflowRequest(
    RuntimePackagePreset Preset,
    RuntimePackageInventory Inventory,
    string CudaPackagePreference,
    long MaxLogBytes,
    DateTimeOffset? CheckedAt = null,
    CancellationToken CancellationToken = default);

public sealed record RuntimePackageCheckWorkflowResult(
    RuntimePackageCheckResult CheckResult,
    JobRecord Job);

public sealed class RuntimePackageCheckWorkflowService
{
    private readonly RuntimePackageJobService _jobs;
    private readonly RuntimePackageUpdateCheckService _checks;

    public RuntimePackageCheckWorkflowService(
        RuntimePackageJobService jobs,
        RuntimePackageUpdateCheckService checks)
    {
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _checks = checks ?? throw new ArgumentNullException(nameof(checks));
    }

    public async Task<RuntimePackageCheckWorkflowResult> CheckAsync(RuntimePackageCheckWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var job = await _jobs.CreateCheckJobAsync(request.Preset, request.CancellationToken);

        try
        {
            await UpdateJobAsync(request, job, JobStatus.Running, $"Checking {RuntimePackageSourceCatalog.PackageSourceLabel(request.Preset)} release assets...");
            var outcome = await _checks.CheckAsync(new RuntimePackageUpdateCheckRequest(
                request.Preset,
                request.Inventory,
                request.CudaPackagePreference,
                request.CheckedAt ?? DateTimeOffset.UtcNow,
                request.CancellationToken));
            await UpdateJobAsync(request, job, JobStatus.Completed, outcome.Result.Message);
            return new RuntimePackageCheckWorkflowResult(outcome.Result, job);
        }
        catch (Exception ex)
        {
            await UpdateJobAsync(request, job, JobStatus.Failed, ex.Message);
            throw;
        }
    }

    private async Task UpdateJobAsync(
        RuntimePackageCheckWorkflowRequest request,
        JobRecord job,
        JobStatus status,
        string message)
    {
        await _jobs.UpdateAsync(
            job,
            status,
            request.Preset,
            "check",
            "",
            message,
            request.MaxLogBytes,
            request.CancellationToken);
    }
}
