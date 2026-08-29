namespace LocalLlmConsole.Services;

public sealed record RuntimePackageInstallWorkflowRequest(
    RuntimePackagePreset Preset,
    AppSettings Settings,
    long MaxLogBytes,
    CancellationToken CancellationToken = default,
    bool RequireChecksum = true);

public sealed record RuntimePackageInstallWorkflowResult(
    RuntimePackageUpdateState UpdateState,
    string RuntimeFolder,
    string StatusMessage,
    JobRecord Job);

public sealed class RuntimePackageInstallWorkflowService
{
    private readonly RuntimePackageInstallService _installer;
    private readonly RuntimePackageJobService _jobs;
    private readonly RuntimePackageWslFileService _wslFiles;

    public RuntimePackageInstallWorkflowService(
        RuntimePackageInstallService installer,
        RuntimePackageJobService jobs,
        RuntimePackageWslFileService wslFiles)
    {
        _installer = installer ?? throw new ArgumentNullException(nameof(installer));
        _jobs = jobs ?? throw new ArgumentNullException(nameof(jobs));
        _wslFiles = wslFiles ?? throw new ArgumentNullException(nameof(wslFiles));
    }

    public async Task<RuntimePackageInstallWorkflowResult> InstallAsync(RuntimePackageInstallWorkflowRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        Directory.CreateDirectory(request.Settings.RuntimeRoot);
        Directory.CreateDirectory(request.Settings.CacheRoot);
        var job = await _jobs.CreateInstallJobAsync(request.Preset, request.CancellationToken);

        var installDir = "";
        try
        {
            var result = await _installer.InstallAsync(new RuntimePackageInstallRequest(
                request.Preset,
                request.Settings,
                job.LogPath,
                request.MaxLogBytes,
                async progress =>
                {
                    if (!string.IsNullOrWhiteSpace(progress.InstallDir))
                        installDir = progress.InstallDir;
                    await UpdateJobAsync(request, job, progress.Status, progress.Action, progress.InstallDir, progress.Message);
                },
                async (archivePath, installPath, logPath) => await _wslFiles.ExtractArchiveAsync(new RuntimePackageWslArchiveRequest(
                    request.Settings.WslDistro,
                    archivePath,
                    installPath,
                    logPath,
                    request.MaxLogBytes,
                    request.CancellationToken)),
                async (packagePreset, executable, logPath) => await _wslFiles.TryPrepareExecutableAsync(new RuntimePackageWslExecutableRequest(
                    packagePreset,
                    request.Settings.WslDistro,
                    executable,
                    logPath,
                    request.MaxLogBytes,
                    request.CancellationToken)),
                request.CancellationToken,
                RequireChecksum: request.RequireChecksum));
            await UpdateJobAsync(request, job, JobStatus.Completed, request.Preset, "install", result.RuntimeFolder, result.StatusMessage);
            return new RuntimePackageInstallWorkflowResult(result.UpdateState, result.RuntimeFolder, result.StatusMessage, job);
        }
        catch (Exception ex)
        {
            await UpdateJobAsync(request, job, JobStatus.Failed, request.Preset, "install", installDir, ex.Message);
            throw;
        }
    }

    private async Task UpdateJobAsync(
        RuntimePackageInstallWorkflowRequest request,
        JobRecord job,
        JobStatus status,
        string action,
        string installDir,
        string message)
        => await UpdateJobAsync(request, job, status, request.Preset, action, installDir, message);

    private async Task UpdateJobAsync(
        RuntimePackageInstallWorkflowRequest request,
        JobRecord job,
        JobStatus status,
        RuntimePackagePreset preset,
        string action,
        string installDir,
        string message)
    {
        await _jobs.UpdateAsync(
            job,
            status,
            preset,
            action,
            installDir,
            message,
            request.MaxLogBytes,
            request.CancellationToken);
    }
}
