namespace LocalLlmConsole.Services;

public enum LlamaRuntimeState
{
    Stopped,
    Loading,
    Loaded,
    Failed
}
public sealed partial class LlamaProcessSupervisor : IDisposable
{
    private readonly WslRuntimeStopService _wslRuntimeStop;
    private readonly NativeRuntimeStopService _nativeRuntimeStop;
    private Process? _process;
    private ProcessJobObjectService? _jobObject;
    private BoundedLogWriter? _log;
    private bool _attached;
    private bool _recovered;
    private RuntimeMode _lastRuntimeMode;
    private int _state = (int)LlamaRuntimeState.Stopped;

    public bool IsRunning => _process is { HasExited: false } || _attached;
    public bool IsRecovered => _recovered;
    public string ActiveModelId { get; private set; } = "";
    public string ActiveRuntimeId { get; private set; } = "";
    public string LogPath { get; private set; } = "";
    public LlamaRuntimeState State
    {
        get => (LlamaRuntimeState)Volatile.Read(ref _state);
        private set => Volatile.Write(ref _state, (int)value);
    }
    public int? LastExitCode { get; private set; }
    public int ProcessId
    {
        get
        {
            try { return _process?.Id ?? 0; }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not read llama process id: {ex.Message}");
                return 0;
            }
        }
    }
    public string WslProcessMarker => _lastWslProcessMarker;
    private AppSettings? _lastSettings;
    private string _lastRuntimeExecutablePath = "";
    private string _lastWslProcessMarker = "";
    private string _lastApiKey = "";

    public LlamaProcessSupervisor(
        WslRuntimeStopService wslRuntimeStop,
        NativeRuntimeStopService nativeRuntimeStop)
    {
        _wslRuntimeStop = wslRuntimeStop ?? throw new ArgumentNullException(nameof(wslRuntimeStop));
        _nativeRuntimeStop = nativeRuntimeStop ?? throw new ArgumentNullException(nameof(nativeRuntimeStop));
    }

    public Task StartAsync(RuntimeRecord runtime, ModelRecord model, AppSettings settings, string logRoot)
    {
        RuntimeAvailabilityService.EnsureAvailable(runtime);
        Stop();
        Directory.CreateDirectory(logRoot);
        LogPath = Path.Combine(logRoot, $"llama-server-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.log");
        _log = new BoundedLogWriter(LogPath, BoundedLogFile.MegabytesToBytes(settings.MaxLogFileSizeMb));
        State = LlamaRuntimeState.Loading;
        LastExitCode = null;
        _attached = false;
        _recovered = false;
        _lastRuntimeMode = runtime.Mode;

        var executable = runtime.Mode == RuntimeMode.Wsl ? ToWslPath(runtime.ExecutablePath) : runtime.ExecutablePath;
        _lastRuntimeExecutablePath = executable;
        _lastWslProcessMarker = runtime.Mode == RuntimeMode.Wsl ? $"local-llm-console-llama-{Guid.NewGuid():N}" : "";
        var modelPath = runtime.Mode == RuntimeMode.Wsl ? ToWslPath(model.ModelPath) : model.ModelPath;
        var embeddedVisionProjector = VisionProjectorSelection.IsEmbeddedOrMainModel(model.ModelPath, settings.VisionProjectorPath);
        if (VisionProjectorSelection.IsExternal(settings.VisionProjectorPath)
            && !embeddedVisionProjector
            && !File.Exists(Path.GetFullPath(settings.VisionProjectorPath.Trim())))
            throw new InvalidOperationException("Configured vision head/projector GGUF file was not found.");
        var mmprojPath = ModelCatalogService.ResolveVisionProjectorPath(model.ModelPath, settings.VisionProjectorPath);
        var visionProjectorPath = string.IsNullOrWhiteSpace(mmprojPath)
            ? null
            : runtime.Mode == RuntimeMode.Wsl ? ToWslPath(mmprojPath) : mmprojPath;
        var speculativeType = LaunchSettingMetadataService.NormalizeSpeculativeType(settings.SpeculativeType);
        var usesDraftModel = speculativeType.StartsWith("draft-", StringComparison.OrdinalIgnoreCase);
        if (usesDraftModel
            && !string.IsNullOrWhiteSpace(settings.SpecDraftModelPath)
            && !File.Exists(Path.GetFullPath(settings.SpecDraftModelPath.Trim())))
            throw new InvalidOperationException("Configured speculative draft/sidecar GGUF file was not found.");
        var draftModelPath = ModelCatalogService.ResolveDraftModelPath(model.ModelPath, settings.SpecDraftModelPath, settings.SpeculativeType);
        var usesEmbeddedDraftMtp = speculativeType.Equals("draft-mtp", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(settings.SpecDraftModelPath)
            && ModelCatalogService.HasEmbeddedDraftMtp(model.ModelPath);
        if (usesDraftModel && string.IsNullOrWhiteSpace(draftModelPath) && !usesEmbeddedDraftMtp)
            throw new InvalidOperationException($"No matching {speculativeType} companion GGUF was found in the model folder. Select a compatible draft file explicitly or choose another speculative type.");
        var launchDraftModelPath = string.IsNullOrWhiteSpace(draftModelPath)
            ? null
            : runtime.Mode == RuntimeMode.Wsl ? ToWslPath(draftModelPath) : draftModelPath;
        var mtpHeadPath = ResolveMtpHeadPath(model.ModelPath, settings.MtpHeadPath, settings.SpeculativeType);
        if (!string.IsNullOrWhiteSpace(settings.MtpHeadPath) && !File.Exists(Path.GetFullPath(settings.MtpHeadPath.Trim())))
            throw new InvalidOperationException("Configured MTP head GGUF file was not found.");
        var launchMtpHeadPath = string.IsNullOrWhiteSpace(mtpHeadPath)
            ? null
            : runtime.Mode == RuntimeMode.Wsl ? ToWslPath(mtpHeadPath) : mtpHeadPath;
        var allowDirectLanAccess = AppPreferenceService.DirectModelsAllowLanAccess(settings.ModelAccessMode);
        var launchHost = allowDirectLanAccess
            ? string.IsNullOrWhiteSpace(settings.Host) ? "0.0.0.0" : settings.Host
            : "127.0.0.1";
        var customArgs = CustomLaunchParameterParser.Parse(settings.CustomParameters);
        ValidateCustomArgs(customArgs);
        var extraArgs = new List<string>();
        if (settings.EnableMetrics)
            extraArgs.Add("--metrics");
        extraArgs.AddRange(customArgs);

        var request = RuntimeLaunchRequestFactory.Create(settings, new RuntimeLaunchRequestContext(
            runtime.Mode,
            runtime.Backend,
            executable,
            modelPath,
            launchHost,
            allowDirectLanAccess,
            visionProjectorPath ?? "",
            embeddedVisionProjector,
            launchDraftModelPath ?? "",
            launchMtpHeadPath ?? "",
            extraArgs));
        _lastApiKey = settings.ModelApiKey ?? "";
        var args = RuntimeAdapter.BuildArgs(request);
        var psi = runtime.Mode == RuntimeMode.Wsl
            ? new ProcessStartInfo(HostExecutableResolver.WslExe())
            : new ProcessStartInfo(runtime.ExecutablePath);
        if (runtime.Mode == RuntimeMode.Wsl)
        {
            var executableDir = WslDirectoryName(executable);
            var runtimeLibDir = WslSiblingDirectory(executableDir, "lib");
            var libraryPath = string.IsNullOrWhiteSpace(executableDir)
                ? "$LD_LIBRARY_PATH"
                : $"{BashQuote(executableDir)}:{BashQuote(runtimeLibDir)}:${{LD_LIBRARY_PATH:-}}";
            var argv0 = string.IsNullOrWhiteSpace(_lastWslProcessMarker) ? "" : $" -a {BashQuote(_lastWslProcessMarker)}";
            var syclEnv = WslSyclEnvironmentPrefix(runtime.Backend);
            var command = $"{syclEnv}export LD_LIBRARY_PATH={libraryPath}; cd {BashQuote(string.IsNullOrWhiteSpace(executableDir) ? "/" : executableDir)}; exec{argv0} {BashQuote(executable)} {string.Join(" ", args.Select(BashQuote))}";
            psi.ArgumentList.Add("-d");
            psi.ArgumentList.Add(settings.WslDistro);
            psi.ArgumentList.Add("--");
            psi.ArgumentList.Add("bash");
            psi.ArgumentList.Add("-lc");
            psi.ArgumentList.Add(command);
        }
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        // Pass API key via environment variable (LLAMA_API_KEY) instead of CLI arg
        // to keep it out of process command lines visible in Task Manager / WMI.
        if (!string.IsNullOrWhiteSpace(_lastApiKey))
        {
            psi.Environment["LLAMA_API_KEY"] = _lastApiKey;
            // For WSL, tell the interop layer to forward this env var into the Linux session.
            if (runtime.Mode == RuntimeMode.Wsl)
                psi.Environment["WSLENV"] = "LLAMA_API_KEY";
        }
        if (runtime.Mode == RuntimeMode.Native)
        {
            psi.WorkingDirectory = Path.GetDirectoryName(runtime.ExecutablePath) ?? Environment.CurrentDirectory;
            if (runtime.Backend == RuntimeBackend.Sycl)
                ApplyNativeSyclEnvironment(psi);
        }
        if (runtime.Mode == RuntimeMode.Native)
        {
            // Bind native child process to a Windows Job Object with KILL_ON_JOB_CLOSE.
            // If the app crashes or is force-killed, the OS terminates llama-server.exe
            // automatically — no orphaned processes.
            _jobObject?.Dispose();
            _jobObject = new ProcessJobObjectService();
            foreach (var arg in args) psi.ArgumentList.Add(arg);
        }
        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (_, e) => ObserveOutput(e.Data);
        _process.ErrorDataReceived += (_, e) => ObserveOutput(e.Data);
        _process.Exited += (_, _) =>
        {
            try { LastExitCode = _process?.ExitCode; }
            catch (Exception ex) { Trace.TraceWarning($"Could not read llama process exit code: {ex.Message}"); }
            if (State != LlamaRuntimeState.Stopped)
                State = LlamaRuntimeState.Failed;
        };
        try
        {
            if (!_process.Start())
            {
                State = LlamaRuntimeState.Failed;
                throw new InvalidOperationException("Failed to start llama-server.");
            }
            _jobObject?.AssignProcess(_process.Handle);
        }
        catch
        {
            _jobObject?.Dispose();
            _jobObject = null;
            State = LlamaRuntimeState.Failed;
            throw;
        }
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        ActiveModelId = model.Id;
        ActiveRuntimeId = runtime.Id;
        _lastSettings = settings;
        return Task.CompletedTask;
    }

    public bool MarkLoadedIfRunning()
    {
        if (!IsRunning || !TrySetLoadedFromLoading()) return false;
        LastExitCode = null;
        return true;
    }

    private void ObserveOutput(string? line)
    {
        if (LlamaRuntimeOutputObserver.Observe(line, _log, _lastApiKey))
            TrySetLoadedFromLoading();
    }

    private static void ValidateCustomArgs(IReadOnlyList<string> extraArgs)
        => RuntimeLaunchOptionPolicy.ValidateCustomArguments(extraArgs);

    private bool TrySetLoadedFromLoading()
        => Interlocked.CompareExchange(
            ref _state,
            (int)LlamaRuntimeState.Loaded,
            (int)LlamaRuntimeState.Loading) == (int)LlamaRuntimeState.Loading;
}
