namespace LocalLlmConsole.Services;

public sealed record RuntimeBuildExecutionRequest(
    RuntimeBuildPreset Preset,
    AppSettings Settings,
    RuntimeBuildPlan Plan,
    RuntimeSourceEntry? Source,
    string LogPath,
    bool Update,
    CancellationToken CancellationToken = default);

public sealed record RuntimeBuildExecutionResult(RuntimeRecord Runtime, string CleanupMessage, string StatusMessage);

public sealed class RuntimeBuildExecutionService
{
    private readonly string _workspaceRoot;
    private readonly IProcessRunner _processRunner;
    private readonly RuntimeRegistryService _runtimes;
    private readonly RuntimeBuildMarkerService _markers;

    public RuntimeBuildExecutionService(
        string workspaceRoot,
        IProcessRunner processRunner,
        RuntimeRegistryService runtimes,
        RuntimeBuildMarkerService markers)
    {
        _workspaceRoot = string.IsNullOrWhiteSpace(workspaceRoot)
            ? throw new ArgumentException("Workspace root is required.", nameof(workspaceRoot))
            : workspaceRoot;
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _runtimes = runtimes ?? throw new ArgumentNullException(nameof(runtimes));
        _markers = markers ?? throw new ArgumentNullException(nameof(markers));
    }

    public async Task<RuntimeBuildExecutionResult> ExecuteAsync(RuntimeBuildExecutionRequest request)
    {
        var script = await EnsureBuildToolScriptAsync(request.CancellationToken);
        await RunBuildToolAsync(script, request);
        await RuntimeBuildJobService.StampManagedMetadataAsync(request.Plan.InstallDir, request.Preset, request.Update);
        var runtime = await _runtimes.RegisterFolderAsync(request.Plan.InstallDir);
        var cleanupMessage = request.Source is null
            ? ""
            : await TryDeleteRuntimeSourceAfterSuccessfulBuildAsync(request.Source, request.Settings, request.LogPath, request.CancellationToken);
        var status = $"{request.Preset.Label} installed as {Path.GetFileName(request.Plan.InstallDir)}.{cleanupMessage}";
        return new RuntimeBuildExecutionResult(runtime, cleanupMessage, status);
    }

    public async Task<string> EnsureBuildToolScriptAsync(CancellationToken cancellationToken = default)
    {
        var assembly = typeof(RuntimeBuildExecutionService).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith(".Build-LlamaCppRuntime.ps1", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(resourceName))
            throw new InvalidOperationException("Build tool resource was not packaged.");

        var toolsDir = Path.Combine(_workspaceRoot, "tools");
        Directory.CreateDirectory(toolsDir);
        var scriptPath = Path.Combine(toolsDir, "Build-LlamaCppRuntime.ps1");
        var tempPath = scriptPath + ".tmp";
        await using (var resource = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("Build tool resource could not be opened."))
        await using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await resource.CopyToAsync(output, cancellationToken);
        }

        File.Move(tempPath, scriptPath, overwrite: true);
        return scriptPath;
    }

    private async Task RunBuildToolAsync(string script, RuntimeBuildExecutionRequest request)
    {
        var mode = RuntimeBuildCatalogService.BuildMode(request.Preset);
        var psi = RuntimeBuildToolService.CreateBuildProcessStartInfo(
            HostExecutableResolver.WindowsPowerShellExe(),
            script,
            request.Plan.SourceDir,
            request.Plan.BuildDir,
            request.Plan.InstallDir,
            request.Preset,
            mode,
            request.Settings.WslDistro,
            request.Plan.ProcessMarker,
            mode == RuntimeMode.Wsl ? HostExecutableResolver.WslExe() : "",
            mode == RuntimeMode.Native ? HostExecutableResolver.GitExe() : "",
            mode == RuntimeMode.Native ? HostExecutableResolver.CMakeExe() : "",
            request.Source is not null);

        if (mode == RuntimeMode.Wsl)
            _markers.Register(request.Plan.ProcessMarker);
        try
        {
            var result = await _processRunner.RunAsync(psi, TimeSpan.FromHours(6), request.CancellationToken);
            await BoundedLogFile.AppendAsync(request.LogPath, result.Output + Environment.NewLine + result.Error, MaxLogBytes(request.Settings));
            if (result.ExitCode != 0)
                throw new InvalidOperationException(string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error);
        }
        catch
        {
            if (mode == RuntimeMode.Wsl)
                await _markers.CleanupAsync(request.Settings.WslDistro, request.Plan.ProcessMarker);
            throw;
        }
        finally
        {
            if (mode == RuntimeMode.Wsl)
                _markers.Unregister(request.Plan.ProcessMarker);
        }
    }

    private static async Task<string> TryDeleteRuntimeSourceAfterSuccessfulBuildAsync(
        RuntimeSourceEntry source,
        AppSettings settings,
        string logPath,
        CancellationToken cancellationToken)
    {
        if (!settings.DeleteRuntimeSourceAfterSuccessfulBuild) return "";
        if (string.IsNullOrWhiteSpace(source.SourceDir) || !Directory.Exists(source.SourceDir))
            return " Downloaded source was already absent.";

        try
        {
            await RuntimeFileService.DeleteSafeRuntimeFolderAsync(
                settings.RuntimeRoot,
                source.SourceDir,
                cancellationToken);
            var message = $"Deleted downloaded source after successful build: {source.SourceDir}";
            await BoundedLogFile.AppendAsync(logPath, $"[{DateTimeOffset.Now:O}] Completed: {message}{Environment.NewLine}", MaxLogBytes(settings));
            return " Downloaded source deleted.";
        }
        catch (Exception ex)
        {
            var message = $"Downloaded source cleanup failed: {ex.Message}";
            await BoundedLogFile.AppendAsync(logPath, $"[{DateTimeOffset.Now:O}] Warning: {message}{Environment.NewLine}", MaxLogBytes(settings));
            return $" {message}";
        }
    }

    private static long MaxLogBytes(AppSettings settings)
        => BoundedLogFile.MegabytesToBytes(settings.MaxLogFileSizeMb);
}
