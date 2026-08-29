namespace LocalLlmConsole.Services;

public sealed partial class LlamaProcessSupervisor
{
    public void AttachExisting(RuntimeRecord runtime, string modelId, AppSettings settings, string logPath, LlamaRuntimeState state = LlamaRuntimeState.Loaded, string processMarker = "", int processId = 0)
    {
        Stop();
        ActiveModelId = modelId;
        ActiveRuntimeId = runtime.Id;
        LogPath = logPath;
        State = state is LlamaRuntimeState.Stopped or LlamaRuntimeState.Failed ? LlamaRuntimeState.Loading : state;
        LastExitCode = null;
        _lastSettings = settings;
        _lastRuntimeMode = runtime.Mode;
        _lastRuntimeExecutablePath = runtime.Mode == RuntimeMode.Wsl ? ToWslPath(runtime.ExecutablePath) : runtime.ExecutablePath;
        _lastWslProcessMarker = runtime.Mode == RuntimeMode.Wsl ? processMarker : "";
        _lastApiKey = settings.ModelApiKey ?? "";
        _recovered = true;
        _attached = true;
        if (runtime.Mode == RuntimeMode.Native && processId > 0)
            AttachRecoveredNativeProcess(processId);
    }

    private void AttachRecoveredNativeProcess(int processId)
    {
        try
        {
            var process = Process.GetProcessById(processId);
            if (process.HasExited)
            {
                process.Dispose();
                State = LlamaRuntimeState.Failed;
                _attached = false;
                return;
            }

            process.EnableRaisingEvents = true;
            process.Exited += (_, _) =>
            {
                try { LastExitCode = process.ExitCode; }
                catch (Exception ex) { Trace.TraceWarning($"Could not read recovered llama process exit code: {ex.Message}"); }
                if (State != LlamaRuntimeState.Stopped)
                    State = LlamaRuntimeState.Failed;
            };
            _process = process;
            _attached = false;
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not attach to recovered native llama process {processId}: {ex.Message}");
            State = LlamaRuntimeState.Failed;
            _attached = false;
        }
    }
}
