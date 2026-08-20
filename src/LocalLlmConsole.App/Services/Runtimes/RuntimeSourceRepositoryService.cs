namespace LocalLlmConsole.Services;

public sealed record RuntimeSourceDownloadRequest(
    RuntimeBuildPreset Preset,
    AppSettings Settings,
    string LogPath,
    long MaxLogBytes,
    CancellationToken CancellationToken = default);

public sealed record RuntimeSourceDownloadResult(RuntimeSourceEntry Source, string StatusMessage);

public sealed record RuntimeSourceVersion(string Commit, string Path);

public sealed record RuntimeSourceUpdateCheck(bool IsInstalled, bool HasUpdate, string LocalCommit, string RemoteCommit);

public sealed class RuntimeSourceRepositoryService
{
    private readonly IProcessRunner _processRunner;

    public RuntimeSourceRepositoryService(IProcessRunner processRunner)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    public async Task<RuntimeSourceDownloadResult> DownloadAsync(RuntimeSourceDownloadRequest request)
    {
        var sourceDir = RuntimeBuildCatalogService.SourceDir(request.Settings.RuntimeRoot, request.Preset);
        Directory.CreateDirectory(RuntimeBuildCatalogService.SourceRoot(request.Settings.RuntimeRoot));
        await CloneOrUpdateAsync(new RuntimeSourceRepositoryRequest(
            request.Preset,
            request.Settings.RuntimeRoot,
            sourceDir,
            request.LogPath,
            request.MaxLogBytes,
            request.CancellationToken));

        var commit = await ReadHeadCommitAsync(sourceDir, request.CancellationToken);
        var source = new RuntimeSourceEntry(
            request.Preset.Id,
            request.Preset.Label,
            request.Preset.RepoUrl,
            request.Preset.Branch,
            request.Preset.Cuda,
            sourceDir,
            commit,
            DateTimeOffset.UtcNow,
            RuntimeBuildCatalogService.BackendKey(request.Preset),
            RuntimeBuildCatalogService.BuildMode(request.Preset));

        await File.WriteAllTextAsync(
            RuntimeBuildCatalogService.SourceMetadataPath(sourceDir),
            JsonSerializer.Serialize(source, new JsonSerializerOptions { WriteIndented = true }),
            request.CancellationToken);

        return new RuntimeSourceDownloadResult(
            source,
            $"{request.Preset.Label} downloaded at {RuntimeMetadataService.ShortCommit(commit)}. It now appears under Installed local builds.");
    }

    public async Task CloneOrUpdateAsync(RuntimeSourceRepositoryRequest request)
    {
        if (!await Task.Run(
                () => RuntimeFileService.IsSafeRuntimeFolder(request.RuntimeRoot, request.SourceDir),
                request.CancellationToken))
            throw new InvalidOperationException("Refusing to write runtime source outside the configured runtimes folder.");

        if (Directory.Exists(request.SourceDir))
        {
            if (await TryPrepareExistingRuntimeSourceAsync(request))
            {
                await RunGitCommandAsync(request.SourceDir, request.LogPath, request.MaxLogBytes, request.CancellationToken, "fetch", "--all", "--tags");
                if (!string.IsNullOrWhiteSpace(request.Preset.Branch))
                    await RunGitCommandAsync(request.SourceDir, request.LogPath, request.MaxLogBytes, request.CancellationToken, "checkout", request.Preset.Branch);
                await RunGitCommandAsync(request.SourceDir, request.LogPath, request.MaxLogBytes, request.CancellationToken, "pull", "--ff-only");
                return;
            }

            await RuntimeBuildJobService.AppendRecoveryLogAsync(request.LogPath, $"Discarding incomplete runtime source folder before reclone: {request.SourceDir}", request.MaxLogBytes);
            await RuntimeFileService.DeleteSafeRuntimeFolderAsync(
                request.RuntimeRoot,
                request.SourceDir,
                request.CancellationToken);
        }

        Directory.CreateDirectory(request.RuntimeRoot);
        var args = new List<string> { "clone", "--depth", "1" };
        if (!string.IsNullOrWhiteSpace(request.Preset.Branch))
            args.AddRange(["--branch", request.Preset.Branch]);
        args.AddRange([request.Preset.RepoUrl, request.SourceDir]);
        try
        {
            await RunGitCommandAsync(request.RuntimeRoot, request.LogPath, request.MaxLogBytes, request.CancellationToken, args.ToArray());
        }
        catch (Exception ex)
        {
            await RuntimeBuildJobService.AppendRecoveryLogAsync(request.LogPath, $"Runtime source clone failed and the incomplete folder will be removed before the next retry: {ex.Message}", request.MaxLogBytes);
            await TryDeleteIncompleteRuntimeSourceAsync(request);
            throw;
        }
    }

    public static RuntimeSourceVersion LatestLocalVersion(
        RuntimeBuildPreset preset,
        IEnumerable<RuntimeSourceEntry> sources,
        IEnumerable<RuntimeRecord> runtimes)
    {
        var source = sources
            .Where(candidate => string.Equals(candidate.PresetId, preset.Id, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(candidate => candidate.DownloadedAt)
            .FirstOrDefault();
        if (source is not null)
            return new RuntimeSourceVersion(RuntimeBuildCatalogService.SourceCommit(source), source.SourceDir);

        var runtime = LatestInstalledRuntime(preset, runtimes);
        return runtime is null
            ? new RuntimeSourceVersion("", "")
            : new RuntimeSourceVersion(RuntimeMetadataService.Commit(runtime), RuntimeMetadataService.Folder(runtime));
    }

    public static RuntimeRecord? LatestInstalledRuntime(RuntimeBuildPreset preset, IEnumerable<RuntimeRecord> runtimes)
        => runtimes
            .Where(runtime => RuntimeAvailabilityService.IsAvailable(runtime)
                && string.Equals(RuntimeMetadataService.ManagedPresetId(runtime), preset.Id, StringComparison.OrdinalIgnoreCase))
            .GroupBy(runtime => RuntimeMetadataService.Folder(runtime), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(runtime => runtime.UpdatedAt).First())
            .OrderByDescending(runtime => runtime.UpdatedAt)
            .FirstOrDefault();

    public async Task<RuntimeSourceUpdateCheck> CheckUpdateAsync(RuntimeBuildPreset preset, RuntimeRecord? installed, CancellationToken cancellationToken = default)
    {
        if (installed is null)
            return new RuntimeSourceUpdateCheck(false, true, "", await RemoteCommitAsync(preset, cancellationToken));

        var localCommit = RuntimeMetadataService.Commit(installed);
        var remoteCommit = await RemoteCommitAsync(preset, cancellationToken);
        return new RuntimeSourceUpdateCheck(true, !RuntimeMetadataService.CommitsMatch(localCommit, remoteCommit), localCommit, remoteCommit);
    }

    public async Task<string> RemoteCommitAsync(RuntimeBuildPreset preset, CancellationToken cancellationToken = default)
    {
        foreach (var remoteRef in RuntimeBuildCatalogService.RemoteRefs(preset))
        {
            var commit = await RunGitLsRemoteAsync(preset.RepoUrl, remoteRef, cancellationToken);
            if (!string.IsNullOrWhiteSpace(commit)) return commit;
        }

        throw new InvalidOperationException($"Could not check remote updates for {preset.Label}.");
    }

    public async Task<string> ReadHeadCommitAsync(string sourceDir, CancellationToken cancellationToken = default)
    {
        var output = await RunGitCommandAsync(sourceDir, null, 0, cancellationToken, "rev-parse", "--short=12", "HEAD");
        return output.Trim();
    }

    private async Task<bool> TryPrepareExistingRuntimeSourceAsync(RuntimeSourceRepositoryRequest request)
    {
        if (!Directory.Exists(Path.Combine(request.SourceDir, ".git")) && !File.Exists(Path.Combine(request.SourceDir, ".git")))
            return false;

        try
        {
            var output = await RunGitCommandAsync(request.SourceDir, null, 0, request.CancellationToken, "rev-parse", "--is-inside-work-tree");
            if (!string.Equals(output.Trim(), "true", StringComparison.OrdinalIgnoreCase))
                return false;

            var status = await RunGitCommandAsync(request.SourceDir, null, 0, request.CancellationToken, "status", "--porcelain");
            if (string.IsNullOrWhiteSpace(status)) return true;

            await RuntimeBuildJobService.AppendRecoveryLogAsync(request.LogPath, "Existing runtime source checkout is incomplete or dirty and will be repaired before updating.", request.MaxLogBytes);
            await RunGitCommandAsync(request.SourceDir, request.LogPath, request.MaxLogBytes, request.CancellationToken, "checkout", "--force", "HEAD");
            status = await RunGitCommandAsync(request.SourceDir, null, 0, request.CancellationToken, "status", "--porcelain");
            return string.IsNullOrWhiteSpace(status);
        }
        catch (Exception ex)
        {
            await RuntimeBuildJobService.AppendRecoveryLogAsync(request.LogPath, $"Existing runtime source is not a valid git repository and will be recloned: {ex.Message}", request.MaxLogBytes);
            return false;
        }
    }

    private async Task<string> RunGitCommandAsync(string workingDirectory, string? logPath, long maxLogBytes, CancellationToken cancellationToken, params string[] args)
    {
        var psi = new ProcessStartInfo(HostExecutableResolver.GitExe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Directory.Exists(workingDirectory) ? workingDirectory : Environment.CurrentDirectory
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("core.longpaths=true");
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var result = await _processRunner.RunAsync(psi, TimeSpan.FromMinutes(60), cancellationToken);
        if (!string.IsNullOrWhiteSpace(logPath))
        {
            await BoundedLogFile.AppendAsync(logPath, $"> git -c core.longpaths=true {string.Join(' ', args.Select(RuntimeBuildJobService.RedactCommandArgument))}{Environment.NewLine}{result.Output}{result.Error}{Environment.NewLine}", maxLogBytes);
        }
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        return result.Output;
    }

    private async Task<string> RunGitLsRemoteAsync(string repoUrl, string remoteRef, CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(HostExecutableResolver.GitExe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        psi.ArgumentList.Add("ls-remote");
        psi.ArgumentList.Add(repoUrl);
        psi.ArgumentList.Add(remoteRef);
        var result = await _processRunner.RunAsync(psi, TimeSpan.FromMinutes(5), cancellationToken);
        if (result.ExitCode != 0) throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        return RuntimeBuildCatalogService.FirstLsRemoteCommit(result.Output);
    }

    private async Task TryDeleteIncompleteRuntimeSourceAsync(RuntimeSourceRepositoryRequest request)
    {
        try
        {
            if (Directory.Exists(request.SourceDir))
            {
                await RuntimeFileService.DeleteSafeRuntimeFolderAsync(
                    request.RuntimeRoot,
                    request.SourceDir,
                    CancellationToken.None);
            }
        }
        catch (Exception cleanupEx)
        {
            await RuntimeBuildJobService.AppendRecoveryLogAsync(request.LogPath, $"Incomplete runtime source cleanup failed: {cleanupEx.Message}", request.MaxLogBytes);
        }
    }
}

public sealed record RuntimeSourceRepositoryRequest(
    RuntimeBuildPreset Preset,
    string RuntimeRoot,
    string SourceDir,
    string LogPath,
    long MaxLogBytes,
    CancellationToken CancellationToken = default);
