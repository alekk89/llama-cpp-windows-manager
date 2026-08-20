namespace LocalLlmConsole.Services;

public sealed record NativeRuntimeStopResult(
    bool StopRequested,
    bool ExitedAfterPrimaryKill,
    bool ExitedAfterVerificationKill)
{
    public bool Exited => ExitedAfterPrimaryKill || ExitedAfterVerificationKill;
}

public sealed class NativeRuntimeStopService
{
    private const int PrimaryExitWaitMilliseconds = 3000;
    private const int VerificationExitWaitMilliseconds = 1000;

    public async Task<NativeRuntimeStopResult> StopAsync(
        Process? process,
        CancellationToken cancellationToken = default)
    {
        if (process is null)
            return new NativeRuntimeStopResult(false, true, true);

        if (HasExited(process))
            return new NativeRuntimeStopResult(false, true, true);

        var processId = TryGetProcessId(process);
        var startTime = TryGetStartTime(process);
        var exitedAfterPrimaryKill = await KillAndWaitAsync(
            process,
            TimeSpan.FromMilliseconds(PrimaryExitWaitMilliseconds),
            cancellationToken);
        var exitedAfterVerificationKill = exitedAfterPrimaryKill
            || await KillVerifiedProcessByIdAsync(processId, startTime, cancellationToken);

        return new NativeRuntimeStopResult(
            true,
            exitedAfterPrimaryKill,
            exitedAfterVerificationKill);
    }

    private static async Task<bool> KillAndWaitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            if (process.HasExited)
                return true;

            process.Kill(entireProcessTree: true);
            return await WaitForExitAsync(process, timeout, cancellationToken);
        }
        catch (TimeoutException)
        {
            return HasExited(process);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return HasExited(process);
        }
    }

    private static async Task<bool> KillVerifiedProcessByIdAsync(
        int processId,
        DateTime? expectedStartTime,
        CancellationToken cancellationToken)
    {
        if (processId <= 0)
            return false;

        try
        {
            using var process = Process.GetProcessById(processId);
            if (process.HasExited)
                return true;

            var actualStartTime = TryGetStartTime(process);
            if (expectedStartTime is not null
                && actualStartTime is not null
                && actualStartTime.Value != expectedStartTime.Value)
                return true;

            process.Kill(entireProcessTree: true);
            return await WaitForExitAsync(
                process,
                TimeSpan.FromMilliseconds(VerificationExitWaitMilliseconds),
                cancellationToken);
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<bool> WaitForExitAsync(
        Process process,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            await process.WaitForExitAsync(cancellationToken).WaitAsync(timeout, cancellationToken);
            return true;
        }
        catch (TimeoutException)
        {
            return HasExited(process);
        }
    }

    private static bool HasExited(Process process)
    {
        try { return process.HasExited; }
        catch { return true; }
    }

    private static int TryGetProcessId(Process process)
    {
        try { return process.Id; }
        catch { return 0; }
    }

    private static DateTime? TryGetStartTime(Process process)
    {
        try { return process.StartTime; }
        catch { return null; }
    }
}
