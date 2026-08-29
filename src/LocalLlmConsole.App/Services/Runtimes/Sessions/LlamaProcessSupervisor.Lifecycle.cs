namespace LocalLlmConsole.Services;

public sealed partial class LlamaProcessSupervisor
{
    public sealed record StopVerification(bool VerifiedStopped, string Error);

    public void Stop()
    {
        var result = StopVerifiedAsync().GetAwaiter().GetResult();
        if (!result.VerifiedStopped)
            Trace.TraceWarning($"Could not verify llama runtime shutdown: {result.Error}");
    }

    public async Task<StopVerification> StopVerifiedAsync(CancellationToken cancellationToken = default)
    {
        var verified = true;
        var error = "";
        if (_lastRuntimeMode == RuntimeMode.Native)
        {
            var result = await _nativeRuntimeStop.StopAsync(_process, cancellationToken);
            verified = result.Exited;
            if (!verified)
                error = "The native runtime process remained alive after both stop attempts.";
        }
        else
        {
            verified = await StopHostProcessAsync(cancellationToken);
            if (!verified)
                error = "The WSL host process remained alive after the stop attempt.";
        }

        if (_lastSettings is not null && _lastRuntimeMode == RuntimeMode.Wsl)
        {
            var wslResult = await _wslRuntimeStop.StopAsync(new WslRuntimeStopRequest(
                _lastSettings,
                _lastRuntimeExecutablePath,
                _lastWslProcessMarker,
                LogPath,
                BoundedLogFile.MegabytesToBytes(_lastSettings.MaxLogFileSizeMb)), cancellationToken);
            verified &= wslResult.VerifiedStopped;
            if (!wslResult.VerifiedStopped)
                error = string.IsNullOrWhiteSpace(wslResult.Error)
                    ? "WSL could not verify that the runtime process stopped."
                    : wslResult.Error;
        }

        if (!verified)
            return new StopVerification(false, error);

        try { _process?.Dispose(); }
        catch (Exception ex) { Trace.TraceWarning($"Could not dispose llama process handle: {ex.Message}"); }
        try { _jobObject?.Dispose(); }
        catch (Exception ex) { Trace.TraceWarning($"Could not dispose llama job object: {ex.Message}"); }
        try { _log?.Dispose(); }
        catch (Exception ex) { Trace.TraceWarning($"Could not dispose llama log writer: {ex.Message}"); }
        _process = null;
        _jobObject = null;
        _log = null;
        ActiveModelId = "";
        ActiveRuntimeId = "";
        State = LlamaRuntimeState.Stopped;
        LastExitCode = null;
        _lastSettings = null;
        _lastRuntimeExecutablePath = "";
        _lastWslProcessMarker = "";
        _lastApiKey = "";
        _attached = false;
        _recovered = false;
        return new StopVerification(true, "");
    }

    private async Task<bool> StopHostProcessAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(entireProcessTree: true);
                try
                {
                    await _process.WaitForExitAsync(cancellationToken)
                        .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken);
                }
                catch (TimeoutException)
                {
                    // The verification below reports whether termination actually completed.
                }
            }
            return _process is null || _process.HasExited;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not stop llama host process: {ex.Message}");
            try { return _process is null || _process.HasExited; }
            catch { return false; }
        }
    }

    public async ValueTask DisposeAsync()
    {
        var result = await StopVerifiedAsync();
        if (!result.VerifiedStopped)
            Trace.TraceWarning($"Could not verify llama runtime shutdown: {result.Error}");
        GC.SuppressFinalize(this);
    }

    public void Dispose()
    {
        if (_process is null && _lastSettings is null && _log is null && _jobObject is null)
            return;

        Stop();
        GC.SuppressFinalize(this);
    }
}
