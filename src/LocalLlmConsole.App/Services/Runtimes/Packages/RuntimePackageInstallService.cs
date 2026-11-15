namespace LocalLlmConsole.Services;

public sealed record RuntimePackageInstallProgress(
    JobStatus Status,
    string Action,
    string InstallDir,
    string Message);

public sealed record RuntimePackageInstallRequest(
    RuntimePackagePreset Preset,
    AppSettings Settings,
    string LogPath,
    long MaxLogBytes,
    Func<RuntimePackageInstallProgress, Task> ProgressAsync,
    Func<string, string, string, int, Task>? ExtractWslArchiveAsync = null,
    Func<RuntimePackagePreset, string, string, Task>? PrepareWslExecutableAsync = null,
    CancellationToken CancellationToken = default,
    bool RequireChecksum = true);

public sealed record RuntimePackageInstallResult(
    string InstallDir,
    string RuntimeFolder,
    RuntimePackageUpdateState UpdateState,
    string StatusMessage);

public sealed class RuntimePackageInstallService
{
    private readonly HttpClient _client;
    private readonly RuntimeRegistryService _runtimes;

    public RuntimePackageInstallService(HttpClient client, RuntimeRegistryService runtimes)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
    }

    public async Task<RuntimePackageInstallResult> InstallAsync(RuntimePackageInstallRequest request)
    {
        var installDir = "";
        try
        {
            await ReportAsync(request, "", $"Resolving latest {RuntimePackageSourceCatalog.PackageSourceLabel(request.Preset)} release...");
            var release = await RuntimePackageReleaseClient.FetchLatestReleaseAsync(_client, request.Preset, request.CancellationToken);
            var selection = RuntimePackageAssetSelector.SelectAssets(request.Preset, release, request.Settings.CudaPackagePreference);
            installDir = RuntimePackageInstallFileService.InstallDir(request.Settings.RuntimeRoot, selection);
            var cacheDir = RuntimePackageInstallFileService.DownloadCacheDir(request.Settings.CacheRoot, selection);
            await Task.Run(
                () => PrepareInstallDirectory(request.Settings.RuntimeRoot, installDir),
                request.CancellationToken);
            Directory.CreateDirectory(cacheDir);

            var checksumStatuses = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var asset in selection.AllAssets)
            {
                request.CancellationToken.ThrowIfCancellationRequested();
                var archivePath = Path.Combine(cacheDir, asset.Name);
                var sizeText = asset.SizeBytes > 0 ? $" ({DisplayFormatService.Bytes(asset.SizeBytes)})" : "";
                await ReportAsync(request, installDir, $"Downloading {asset.Name}{sizeText}...");
                checksumStatuses[asset.Name] = await RuntimePackageInstallFileService.DownloadAssetAsync(_client, asset, archivePath, request.RequireChecksum, request.CancellationToken);
                await RuntimeBuildJobService.AppendJobLogAsync(request.LogPath, JobStatus.Running, $"Extracting {asset.Name}...", request.MaxLogBytes);
                if (request.Preset.Mode == RuntimeMode.Wsl && IsTarArchive(archivePath))
                {
                    if (request.ExtractWslArchiveAsync is null)
                        throw new InvalidOperationException("WSL archive extraction is not configured.");
                    // The CUDA runtime library archive has its own top-level folder. Its libraries go
                    // into the "lib" folder beside the executable folder, which WSL launches put on
                    // LD_LIBRARY_PATH, so they take precedence over an older toolkit in the distro.
                    var libraryArchive = RuntimePackageAssetSelector.IsCudaRuntimeLibraryArchive(asset.Name);
                    await request.ExtractWslArchiveAsync(
                        archivePath,
                        libraryArchive ? Path.Combine(installDir, "lib") : installDir,
                        request.LogPath,
                        libraryArchive ? 1 : 0);
                }
                else
                {
                    await Task.Run(
                        () => RuntimePackageInstallFileService.ExtractArchive(archivePath, installDir),
                        request.CancellationToken);
                }
            }

            var executable = RuntimePackageInstallFileService.FindRuntimeExecutable(installDir, request.Preset.Mode);
            var runtimeFolder = RuntimePackageInstallFileService.RuntimeFolderFromExecutable(executable);
            await RuntimePackageInstallFileService.StampManagedMetadataAsync(runtimeFolder, installDir, selection, checksumStatuses, request.CancellationToken);
            if (!string.Equals(runtimeFolder, installDir, StringComparison.OrdinalIgnoreCase))
                await RuntimePackageInstallFileService.StampManagedMetadataAsync(installDir, installDir, selection, checksumStatuses, request.CancellationToken);
            if (request.Preset.Mode == RuntimeMode.Wsl && request.PrepareWslExecutableAsync is not null)
                await request.PrepareWslExecutableAsync(request.Preset, executable, request.LogPath);
            await _runtimes.RegisterFolderAsync(runtimeFolder);

            var updateState = new RuntimePackageUpdateState(
                false,
                selection.ReleaseTag,
                selection.ReleaseTag,
                selection.ReleaseUrl,
                selection.AssetSummary,
                DateTimeOffset.UtcNow,
                $"package:{selection.ReleaseTag}",
                release.TargetCommit);
            var statusMessage = $"{request.Preset.Label} installed from {selection.ReleaseTag}.";
            return new RuntimePackageInstallResult(installDir, runtimeFolder, updateState, statusMessage);
        }
        catch
        {
            if (!string.IsNullOrWhiteSpace(installDir)
                && Directory.Exists(installDir))
            {
                try
                {
                    await RuntimeFileService.DeleteSafeRuntimeFolderAsync(
                        request.Settings.RuntimeRoot,
                        installDir,
                        CancellationToken.None);
                }
                catch (Exception cleanupEx)
                {
                    Trace.TraceWarning($"Runtime package install cleanup failed for {installDir}: {cleanupEx}");
                }
            }
            throw;
        }
    }

    private static void PrepareInstallDirectory(string runtimeRoot, string installDir)
    {
        Directory.CreateDirectory(runtimeRoot);
        if (!Directory.Exists(installDir))
        {
            Directory.CreateDirectory(installDir);
            return;
        }

        if (!RuntimeFileService.IsSafeRuntimeFolder(runtimeRoot, installDir))
            throw new InvalidOperationException("Refusing to replace a runtime package outside the configured runtimes folder.");
        RuntimeFileService.DeleteSafeRuntimeFolder(runtimeRoot, installDir);
        Directory.CreateDirectory(installDir);
    }

    private static Task ReportAsync(RuntimePackageInstallRequest request, string installDir, string message)
        => request.ProgressAsync(new RuntimePackageInstallProgress(JobStatus.Running, "install", installDir, message));

    private static bool IsTarArchive(string path)
    {
        var name = Path.GetFileName(path);
        return name.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".tgz", StringComparison.OrdinalIgnoreCase);
    }
}
