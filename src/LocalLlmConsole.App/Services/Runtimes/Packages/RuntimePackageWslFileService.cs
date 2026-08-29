namespace LocalLlmConsole.Services;

public sealed record RuntimePackageWslArchiveRequest(
    string DistroName,
    string ArchivePath,
    string InstallDir,
    string LogPath,
    long MaxLogBytes,
    CancellationToken CancellationToken = default);

public sealed record RuntimePackageWslExecutableRequest(
    RuntimePackagePreset Preset,
    string DistroName,
    string Executable,
    string LogPath,
    long MaxLogBytes,
    CancellationToken CancellationToken = default);

public sealed class RuntimePackageWslFileService
{
    private readonly IProcessRunner _processRunner;
    private readonly Func<string> _wslExecutablePath;

    public RuntimePackageWslFileService(IProcessRunner processRunner)
        : this(processRunner, HostExecutableResolver.WslExe)
    {
    }

    public RuntimePackageWslFileService(IProcessRunner processRunner, Func<string>? wslExecutablePath = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _wslExecutablePath = wslExecutablePath ?? HostExecutableResolver.WslExe;
    }

    public async Task TryPrepareExecutableAsync(RuntimePackageWslExecutableRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Preset);
        if (request.Preset.Mode != RuntimeMode.Wsl || string.IsNullOrWhiteSpace(request.Executable)) return;

        try
        {
            var command = $"chmod +x {CommandLineService.BashQuote(WindowsPathToWslPath(request.Executable))}";
            var result = await RunWslShellCommandAsync(request.DistroName, command, TimeSpan.FromSeconds(15), request.CancellationToken);
            if (!string.IsNullOrWhiteSpace(result.Output) || !string.IsNullOrWhiteSpace(result.Error))
                await BoundedLogFile.AppendAsync(request.LogPath, result.Output + result.Error + Environment.NewLine, request.MaxLogBytes);
        }
        catch (Exception ex)
        {
            await RuntimeBuildJobService.AppendJobLogAsync(request.LogPath, JobStatus.Running, $"Warning: could not chmod WSL runtime executable: {ex.Message}", request.MaxLogBytes);
        }
    }

    public async Task ExtractArchiveAsync(RuntimePackageWslArchiveRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArchiveSafetyService.ValidateTarGzipArchiveEntries(request.ArchivePath, request.InstallDir);

        var installDir = CommandLineService.BashQuote(WindowsPathToWslPath(request.InstallDir));
        var archivePath = CommandLineService.BashQuote(WindowsPathToWslPath(request.ArchivePath));
        var command = $"mkdir -p {installDir} && tar --overwrite -xzf {archivePath} -C {installDir}";
        var result = await RunWslShellCommandAsync(request.DistroName, command, TimeSpan.FromMinutes(10), request.CancellationToken);
        if (!string.IsNullOrWhiteSpace(result.Output) || !string.IsNullOrWhiteSpace(result.Error))
            await BoundedLogFile.AppendAsync(request.LogPath, result.Output + result.Error + Environment.NewLine, request.MaxLogBytes);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"WSL archive extraction failed with exit code {result.ExitCode}: {result.Error}".Trim());
    }

    public static string WindowsPathToWslPath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return value;
        if (value.StartsWith('/')) return value.Replace('\\', '/');
        var full = Path.GetFullPath(value);
        if (full.Length >= 3 && full[1] == ':' && (full[2] == '\\' || full[2] == '/'))
        {
            var drive = char.ToLowerInvariant(full[0]);
            var rest = full[3..].Replace('\\', '/');
            return $"/mnt/{drive}/{rest}";
        }

        return full.Replace('\\', '/');
    }

    private async Task<ProcessRunResult> RunWslShellCommandAsync(
        string distroName,
        string command,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var psi = new ProcessStartInfo(_wslExecutablePath())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var arg in new[] { "-d", distroName, "--", "bash", "-lc", command })
            psi.ArgumentList.Add(arg);

        return await _processRunner.RunAsync(psi, timeout, cancellationToken);
    }
}
