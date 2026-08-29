namespace LocalLlmConsole.Services;

public enum LlamaRuntimeState
{
    Stopped,
    Loading,
    Loaded,
    Failed
}
public sealed partial class LlamaProcessSupervisor : IDisposable, IAsyncDisposable
{
    private readonly WslRuntimeStopService _wslRuntimeStop;
    private readonly NativeRuntimeStopService _nativeRuntimeStop;
    private readonly Action<Process>? _afterProcessStart;
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
        : this(wslRuntimeStop, nativeRuntimeStop, null)
    {
    }

    internal LlamaProcessSupervisor(
        WslRuntimeStopService wslRuntimeStop,
        NativeRuntimeStopService nativeRuntimeStop,
        Action<Process>? afterProcessStart)
    {
        _wslRuntimeStop = wslRuntimeStop ?? throw new ArgumentNullException(nameof(wslRuntimeStop));
        _nativeRuntimeStop = nativeRuntimeStop ?? throw new ArgumentNullException(nameof(nativeRuntimeStop));
        _afterProcessStart = afterProcessStart;
    }

    public async Task StartAsync(RuntimeRecord runtime, ModelRecord model, AppSettings settings, string logRoot)
    {
        RuntimeAvailabilityService.EnsureAvailable(runtime);
        var previousStop = await StopVerifiedAsync();
        if (!previousStop.VerifiedStopped)
            throw new InvalidOperationException(previousStop.Error);
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
        var shouldInspectEmbeddedDraftMtp = speculativeType.Equals("draft-mtp", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(settings.SpecDraftModelPath);
        var embeddedDraftMtp = shouldInspectEmbeddedDraftMtp
            ? ModelCatalogService.InspectEmbeddedDraftMtp(model.ModelPath)
            : null;
        var draftModelPath = embeddedDraftMtp?.Embedded == true
            ? null
            : ModelCatalogService.ResolveDraftModelPath(model.ModelPath, settings.SpecDraftModelPath, settings.SpeculativeType);
        var usesEmbeddedDraftMtp = embeddedDraftMtp?.Embedded == true;
        if (usesDraftModel
            && string.IsNullOrWhiteSpace(draftModelPath)
            && shouldInspectEmbeddedDraftMtp
            && embeddedDraftMtp?.MetadataReadable == false)
            throw new InvalidOperationException($"The main GGUF metadata could not be inspected for embedded draft-mtp tensors: {embeddedDraftMtp.Error}");
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
        _lastSettings = settings;
        var args = LlamaCppArgumentBuilder.Build(request);
        var psi = CreateProcessStartInfo(runtime, settings, executable, args);
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
            _afterProcessStart?.Invoke(_process);
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
            ActiveModelId = model.Id;
            ActiveRuntimeId = runtime.Id;
        }
        catch
        {
            await CleanupFailedStartAsync();
            throw;
        }
    }

    private async Task CleanupFailedStartAsync()
    {
        try
        {
            var stop = await StopVerifiedAsync(CancellationToken.None);
            if (!stop.VerifiedStopped)
            {
                Trace.TraceWarning($"Could not verify cleanup after a failed llama-server start: {stop.Error}");
                await StopHostProcessAsync(CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not stop llama-server after partial start: {ex.Message}");
            try { await StopHostProcessAsync(CancellationToken.None); }
            catch (Exception cleanupEx) { Trace.TraceWarning($"Forced llama-server cleanup failed: {cleanupEx.Message}"); }
        }
        finally
        {
            try { _process?.Dispose(); }
            catch (Exception ex) { Trace.TraceWarning($"Could not dispose partially started llama process: {ex.Message}"); }
            try { _jobObject?.Dispose(); }
            catch (Exception ex) { Trace.TraceWarning($"Could not dispose partially initialized llama job object: {ex.Message}"); }
            try { _log?.Dispose(); }
            catch (Exception ex) { Trace.TraceWarning($"Could not dispose partially initialized llama log: {ex.Message}"); }
            _process = null;
            _jobObject = null;
            _log = null;
            ActiveModelId = "";
            ActiveRuntimeId = "";
            _lastSettings = null;
            _lastRuntimeExecutablePath = "";
            _lastWslProcessMarker = "";
            _lastApiKey = "";
            State = LlamaRuntimeState.Failed;
        }
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
