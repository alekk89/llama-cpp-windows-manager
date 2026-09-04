using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed record BenchmarkProcessResult(
    int ExitCode,
    bool CancellationRequested,
    bool VerifiedStopped,
    string DiagnosticTail);

public sealed class BenchmarkProcessRunner
{
    private const int MaximumDiagnosticCharacters = 262_144;
    private static readonly TimeSpan OutputDrainTimeout = TimeSpan.FromSeconds(10);
    private readonly WslRuntimeStopService _wslStop;

    public BenchmarkProcessRunner(WslRuntimeStopService wslStop)
        => _wslStop = wslStop ?? throw new ArgumentNullException(nameof(wslStop));

    public async Task<BenchmarkProcessResult> RunAsync(
        RuntimeRecord runtime,
        string wslDistro,
        string executable,
        IReadOnlyList<string> arguments,
        Func<string, Task> onResultLine,
        Action<string>? onDiagnostic,
        CancellationToken cancellationToken)
    {
        var marker = runtime.Mode == RuntimeMode.Wsl ? $"llwm-benchmark-{Guid.NewGuid():N}" : "";
        var startInfo = BenchmarkRuntimeToolAdapter.CreateStartInfo(runtime, wslDistro, executable, arguments, marker);
        using var process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
        using var jobObject = runtime.Mode == RuntimeMode.Native ? new ProcessJobObjectService() : null;
        var diagnostics = new StringBuilder();
        Task? stdout = null;
        Task? stderr = null;
        var started = false;
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Failed to start llama-bench.");
            started = true;
            jobObject?.AssignProcess(process.Handle);
            stdout = ReadLinesAsync(process.StandardOutput, onResultLine);
            stderr = ReadLinesAsync(process.StandardError, line =>
            {
                AppendBounded(diagnostics, line);
                onDiagnostic?.Invoke(line);
                return Task.CompletedTask;
            });
            var streams = Task.WhenAll(stdout, stderr);
            var streamFailure = ObserveFailure(stdout, stderr);
            var processExit = process.WaitForExitAsync();
            var cancellation = Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            var first = await Task.WhenAny(processExit, streamFailure, cancellation);
            if (ReferenceEquals(first, streamFailure))
                throw await streamFailure;
            var cancelled = ReferenceEquals(first, cancellation);
            if (!cancelled)
            {
                await processExit;
                // Descendants can inherit output pipes even after the benchmark parent exits.
                jobObject?.Dispose();
                try { await streams.WaitAsync(OutputDrainTimeout, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { cancelled = true; }
            }
            var verifiedStopped = !cancelled || await StopAsync(runtime, wslDistro, marker, process);
            if (cancelled)
            {
                jobObject?.Dispose();
                verifiedStopped &= await DrainOutputAsync(streams);
            }
            var exitCode = TryExitCode(process);
            return new BenchmarkProcessResult(exitCode, cancelled, verifiedStopped, diagnostics.ToString());
        }
        catch
        {
            if (started) _ = await StopAsync(runtime, wslDistro, marker, process);
            jobObject?.Dispose();
            if (stdout is not null && stderr is not null)
                _ = await DrainOutputAsync(Task.WhenAll(stdout, stderr));
            throw;
        }
    }

    private static Task<bool> DrainOutputAsync(Task streams)
        => BoundedTaskDrain.ObserveWithinAsync(streams, OutputDrainTimeout,
            "Benchmark output did not close within the shutdown interval.",
            "Benchmark output drain failed during shutdown.");

    private async Task<bool> StopAsync(RuntimeRecord runtime, string distro, string marker, Process process)
    {
        var verified = true;
        if (runtime.Mode == RuntimeMode.Wsl)
        {
            var stop = await _wslStop.StopByMarkerAsync(distro, marker, CancellationToken.None);
            verified = stop.VerifiedStopped;
        }
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch
        {
            verified = false;
        }
        try { return verified && process.HasExited; }
        catch { return false; }
    }

    private static async Task ReadLinesAsync(StreamReader reader, Func<string, Task> callback)
    {
        while (await reader.ReadLineAsync() is { } line)
            await callback(line);
    }

    private static Task<Exception> ObserveFailure(params Task[] tasks)
    {
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        foreach (var task in tasks)
        {
            _ = task.ContinueWith(
                completed => failure.TrySetResult(completed.Exception!.GetBaseException()),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
        return failure.Task;
    }

    private static void AppendBounded(StringBuilder buffer, string line)
    {
        buffer.AppendLine(line);
        if (buffer.Length > MaximumDiagnosticCharacters)
            buffer.Remove(0, buffer.Length - MaximumDiagnosticCharacters);
    }

    private static int TryExitCode(Process process)
    {
        try { return process.ExitCode; }
        catch { return -1; }
    }
}
