
namespace LocalLlmConsole.Services;

public sealed record ProcessRunResult(int ExitCode, string Output, string Error);

public interface IProcessRunner
{
    Task<ProcessRunResult> RunAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken = default, string? standardInput = null);
}

public sealed class TrackedProcessRunner : IProcessRunner
{
    private readonly object _childProcessGate = new();
    private readonly HashSet<Process> _childProcesses = new();

    public async Task<ProcessRunResult> RunAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken = default, string? standardInput = null)
    {
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.RedirectStandardInput = standardInput is not null;

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start()) throw new InvalidOperationException($"Failed to start {Path.GetFileName(psi.FileName)}.");
        TrackChildProcess(process);

        var outputTail = new CappedTextBuffer(256 * 1024);
        var errorTail = new CappedTextBuffer(256 * 1024);
        var outputTask = ReadCappedAsync(process.StandardOutput, outputTail, linkedCts.Token);
        var errorTask = ReadCappedAsync(process.StandardError, errorTail, linkedCts.Token);
        var inputTask = standardInput is null
            ? Task.CompletedTask
            : WriteStandardInputAsync(process, standardInput, linkedCts.Token);
        try
        {
            try
            {
                await process.WaitForExitAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                TryKillProcessTree(process);
                CloseRedirectedStreams(process);
                await ObserveIoCompletionAsync(outputTask, errorTask, inputTask);
                throw new TimeoutException($"{Path.GetFileName(psi.FileName)} did not finish within {timeout.TotalMinutes:0.#} minutes.");
            }
            catch (OperationCanceledException)
            {
                TryKillProcessTree(process);
                CloseRedirectedStreams(process);
                await ObserveIoCompletionAsync(outputTask, errorTask, inputTask);
                throw;
            }

            await Task.WhenAll(outputTask, errorTask, inputTask);
            return new ProcessRunResult(process.ExitCode, outputTail.ToString(), errorTail.ToString());
        }
        finally
        {
            UntrackChildProcess(process);
        }
    }

    private static async Task ObserveIoCompletionAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Child process I/O completed with an observed shutdown exception: {ex.Message}");
        }
    }

    private static void CloseRedirectedStreams(Process process)
    {
        try { process.StandardInput.Close(); } catch { }
        try { process.StandardOutput.Close(); } catch { }
        try { process.StandardError.Close(); } catch { }
    }

    public void KillTrackedProcesses()
    {
        Process[] processes;
        lock (_childProcessGate)
            processes = _childProcesses.ToArray();
        foreach (var process in processes)
            TryKillProcessTree(process);
    }

    private static async Task WriteStandardInputAsync(Process process, string input, CancellationToken cancellationToken)
    {
        try
        {
            await process.StandardInput.WriteAsync(input.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (IOException)
        {
            // The child can exit before consuming stdin; stdout/stderr and exit code still carry the useful result.
        }
        catch (InvalidOperationException)
        {
            // Standard input was closed by the child process.
        }
        finally
        {
            try { process.StandardInput.Close(); }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not close child process standard input: {ex.Message}");
            }
        }
    }

    private static async Task ReadCappedAsync(StreamReader reader, CappedTextBuffer tail, CancellationToken cancellationToken)
    {
        var buffer = new char[8192];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken);
            if (read <= 0) return;
            tail.Append(buffer, read);
        }
    }

    private void TrackChildProcess(Process process)
    {
        lock (_childProcessGate)
            _childProcesses.Add(process);
    }

    private void UntrackChildProcess(Process process)
    {
        lock (_childProcessGate)
            _childProcesses.Remove(process);
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Best-effort cleanup during cancellation/shutdown.
        }
    }

    private sealed class CappedTextBuffer
    {
        private readonly int _maxChars;
        private readonly StringBuilder _builder = new();

        public CappedTextBuffer(int maxChars) => _maxChars = Math.Max(1024, maxChars);

        public void Append(char[] buffer, int length)
        {
            if (length <= 0) return;
            _builder.Append(buffer, 0, length);
            if (_builder.Length > _maxChars)
                _builder.Remove(0, _builder.Length - _maxChars);
        }

        public override string ToString() => _builder.ToString();
    }
}
