namespace LocalLlmConsole.Services;

public sealed record WslRuntimeStopRequest(
    AppSettings Settings,
    string ExecutablePath,
    string ProcessMarker,
    string LogPath = "",
    long MaxLogBytes = 0);

public sealed record WslRuntimeStopResult(
    bool StopRequested,
    bool VerifiedStopped,
    int ExitCode,
    string Output,
    string Error);

public sealed class WslRuntimeStopService
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);
    private readonly IProcessRunner _processRunner;
    private readonly Func<string> _wslExe;

    public WslRuntimeStopService(
        IProcessRunner processRunner,
        Func<string>? wslExe = null)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _wslExe = wslExe ?? HostExecutableResolver.WslExe;
    }

    public async Task<WslRuntimeStopResult> StopAsync(WslRuntimeStopRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Settings.WslDistro))
            return new WslRuntimeStopResult(false, true, 0, "", "");

        try
        {
            var command = BuildStopCommand(request.ExecutablePath, request.Settings.Port, request.ProcessMarker);
            if (string.IsNullOrWhiteSpace(command))
                return new WslRuntimeStopResult(false, true, 0, "", "");

            var result = await _processRunner.RunAsync(
                    BuildStopStartInfo(_wslExe(), request.Settings.WslDistro, command),
                    StopTimeout,
                    cancellationToken);
            await LogStopResultAsync(request, result);
            return new WslRuntimeStopResult(true, result.ExitCode == 0, result.ExitCode, result.Output, result.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            await LogStopExceptionAsync(request, ex);
            return new WslRuntimeStopResult(true, false, -1, "", ex.Message);
        }
    }

    public async Task<WslRuntimeStopResult> StopByMarkerAsync(
        string distro,
        string processMarker,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(distro))
            return new WslRuntimeStopResult(false, false, -1, "", "A WSL distro is required to stop the process.");
        if (string.IsNullOrWhiteSpace(processMarker))
            return new WslRuntimeStopResult(false, false, -1, "", "A WSL process marker is required.");
        try
        {
            var result = await _processRunner.RunAsync(
                BuildStopStartInfo(_wslExe(), distro, WslKillByMarkerCommand(processMarker)),
                StopTimeout,
                cancellationToken);
            return new WslRuntimeStopResult(true, result.ExitCode == 0, result.ExitCode, result.Output, result.Error);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WslRuntimeStopResult(true, false, -1, "", ex.Message);
        }
    }

    private static async Task LogStopResultAsync(WslRuntimeStopRequest request, ProcessRunResult result)
    {
        if (string.IsNullOrWhiteSpace(request.LogPath) || request.MaxLogBytes <= 0) return;
        var text = string.Concat(result.Output, result.Error);
        if (!string.IsNullOrWhiteSpace(text))
            await BoundedLogFile.AppendAsync(request.LogPath, text + Environment.NewLine, request.MaxLogBytes);
        if (result.ExitCode != 0)
            await RuntimeBuildJobService.AppendJobLogAsync(request.LogPath, JobStatus.Running, $"Warning: WSL runtime cleanup could not verify shutdown. Exit code {result.ExitCode}.", request.MaxLogBytes);
    }

    private static async Task LogStopExceptionAsync(WslRuntimeStopRequest request, Exception ex)
    {
        if (string.IsNullOrWhiteSpace(request.LogPath) || request.MaxLogBytes <= 0) return;
        await RuntimeBuildJobService.AppendJobLogAsync(request.LogPath, JobStatus.Running, $"Warning: WSL runtime cleanup failed: {ex.Message}", request.MaxLogBytes);
    }

    public static ProcessStartInfo BuildStopStartInfo(string wslExe, string distro, string command)
    {
        var psi = new ProcessStartInfo(wslExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var arg in new[] { "-d", distro, "--", "bash", "-lc", command })
            psi.ArgumentList.Add(arg);
        return psi;
    }

    public static string BuildStopCommand(string executablePath, int port, string processMarker)
        => !string.IsNullOrWhiteSpace(processMarker)
            ? WslKillByMarkerCommand(processMarker)
            : WslKillByExecutableAndPortCommand(executablePath, port);

    public static string WslKillByExecutableAndPortCommand(string executablePath, int port)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return "";
        var executable = executablePath.Replace('\\', '/');
        return string.Join(" ", new[]
        {
            "for pid in $(pgrep -f -- llama-server 2>/dev/null); do",
            "cmd=$(tr '\\0' ' ' < /proc/$pid/cmdline 2>/dev/null || true);",
            $"case \"$cmd\" in *{CommandLineService.BashQuote(executable)}*\"--port\"*{CommandLineService.BashQuote(port.ToString(CultureInfo.InvariantCulture))}*) kill \"$pid\" 2>/dev/null || true;; esac;",
            "done;",
            "sleep 0.2;",
            "remaining=0;",
            "for cmdline in /proc/[0-9]*/cmdline; do",
            "test -r \"$cmdline\" || continue;",
            "cmd=$(tr '\\0' ' ' < \"$cmdline\" 2>/dev/null || true);",
            $"case \"$cmd\" in *{CommandLineService.BashQuote(executable)}*\"--port\"*{CommandLineService.BashQuote(port.ToString(CultureInfo.InvariantCulture))}*) remaining=1;; esac;",
            "done;",
            "exit \"$remaining\""
        });
    }

    public static string WslKillByMarkerCommand(string processMarker)
    {
        var marker = CommandLineService.BashQuote(processMarker);
        return string.Join(" ", new[]
        {
            $"marker={marker};",
            "for cmdline in /proc/[0-9]*/cmdline; do",
            "test -r \"$cmdline\" || continue;",
            "cmd=$(tr '\\0' ' ' < \"$cmdline\" 2>/dev/null || true);",
            "case \"$cmd\" in *\"$marker\"*) pid=${cmdline#/proc/}; pid=${pid%/cmdline}; kill \"$pid\" 2>/dev/null || true;; esac;",
            "done;",
            "sleep 0.2;",
            "remaining=0;",
            "for cmdline in /proc/[0-9]*/cmdline; do",
            "test -r \"$cmdline\" || continue;",
            "cmd=$(tr '\\0' ' ' < \"$cmdline\" 2>/dev/null || true);",
            "case \"$cmd\" in *\"$marker\"*) remaining=1;; esac;",
            "done;",
            "exit \"$remaining\""
        });
    }
}
