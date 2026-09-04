namespace LocalLlmConsole.Services;

public delegate Task<AppSettings> ModelRuntimeApiKeyEnsurer(AppSettings launchSettings, CancellationToken cancellationToken);

public delegate Task<bool> RuntimeLaunchAdmissionConfirmation(RuntimeLaunchAdmissionPlan plan, CancellationToken cancellationToken);

public delegate Task<VramMemorySnapshot?> RuntimeLaunchMemoryReader(CancellationToken cancellationToken);

public enum SameModelProfileLoadChoice { Cancel, Alongside, Replace }

public delegate Task<SameModelProfileLoadChoice> SameModelProfileLoadChooser(
    ModelRecord model, IReadOnlyList<LoadedModelSessionSnapshot> existing, CancellationToken cancellationToken);

public sealed record ModelRuntimeLaunchPreparationRequest(
    RuntimeRecord Runtime,
    ModelRecord Model,
    AppSettings LaunchSettings,
    bool InteractivePrompts,
    bool AutoLoadGatewayEnabled,
    int AutoLoadGatewayPort,
    ModelRuntimeApiKeyEnsurer EnsureApiKeyAsync,
    RuntimeEndpointRespondingProbe EndpointRespondingAsync,
    RuntimeLaunchAdmissionConfirmation? ConfirmAdmissionAsync = null,
    RuntimeLaunchMemoryReader? ReadMemoryAsync = null,
    string LaunchProfileId = "",
    SameModelProfileLoadChooser? ChooseSameModelLoadAsync = null,
    Func<Task>? SessionsReplacedAsync = null);

public sealed record ModelRuntimeLaunchPreparationResult(
    bool CanLaunch,
    AppSettings LaunchSettings,
    string StatusMessage);

public sealed class ModelRuntimeLaunchPreparationService
{
    private readonly RuntimeSessionCoordinator _runtimeSessions;
    private readonly RuntimeLaunchPrerequisiteService _runtimeLaunchPrerequisites;
    private readonly RuntimeLaunchAdmissionService _runtimeLaunchAdmission;
    private readonly GpuStatusProbeService _gpuStatus;

    public ModelRuntimeLaunchPreparationService(
        RuntimeSessionCoordinator runtimeSessions,
        RuntimeLaunchPrerequisiteService runtimeLaunchPrerequisites,
        RuntimeLaunchAdmissionService runtimeLaunchAdmission,
        GpuStatusProbeService gpuStatus)
    {
        _runtimeSessions = runtimeSessions ?? throw new ArgumentNullException(nameof(runtimeSessions));
        _runtimeLaunchPrerequisites = runtimeLaunchPrerequisites ?? throw new ArgumentNullException(nameof(runtimeLaunchPrerequisites));
        _runtimeLaunchAdmission = runtimeLaunchAdmission ?? throw new ArgumentNullException(nameof(runtimeLaunchAdmission));
        _gpuStatus = gpuStatus ?? throw new ArgumentNullException(nameof(gpuStatus));
    }

    public async Task<ModelRuntimeLaunchPreparationResult> PrepareAsync(
        ModelRuntimeLaunchPreparationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.EnsureApiKeyAsync);
        ArgumentNullException.ThrowIfNull(request.EndpointRespondingAsync);

        var launchSettings = await request.EnsureApiKeyAsync(request.LaunchSettings, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var existing = request.InteractivePrompts
            ? _runtimeSessions.Sessions.SessionsForModel(request.Model.Id)
                .Where(session => session.IsRunning && !string.Equals(session.LaunchProfileId, request.LaunchProfileId, StringComparison.OrdinalIgnoreCase)).ToArray()
            : [];
        var choice = SameModelProfileLoadChoice.Alongside;
        if (existing.Length > 0)
        {
            choice = AppPreferenceService.SameModelLoadPolicy(launchSettings.SameModelLoadPolicy) switch
            {
                "alongside" => SameModelProfileLoadChoice.Alongside,
                "replace" => SameModelProfileLoadChoice.Replace,
                _ => request.ChooseSameModelLoadAsync is { } choose
                    ? await choose(request.Model, existing, cancellationToken)
                    : throw new InvalidOperationException("Loading another profile requires a choice handler.")
            };
            if (choice == SameModelProfileLoadChoice.Cancel)
                return new(false, launchSettings, "");
        }
        var replaceIds = choice == SameModelProfileLoadChoice.Replace
            ? existing.Select(session => session.SessionId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : [];

        _runtimeSessions.EnsureLaunchPortAvailable(
            request.Model.Id,
            launchSettings,
            request.AutoLoadGatewayEnabled,
            request.AutoLoadGatewayPort,
            request.LaunchProfileId,
            replaceIds);
        RuntimeLaunchOptionPolicy.ValidateCustomArguments(CustomLaunchParameterParser.Parse(launchSettings.CustomParameters));
        RuntimeDirectAliasService.ValidateSuffix(launchSettings.DirectModelAliasSuffix);
        await _runtimeLaunchPrerequisites.EnsureReadyAsync(new RuntimeLaunchPrerequisiteRequest(
            request.Runtime,
            launchSettings,
            request.EndpointRespondingAsync,
            PortWillBeReleased: existing.Any(session => replaceIds.Contains(session.SessionId) && session.LaunchSettings.Port == launchSettings.Port)), cancellationToken);

        var plan = await AssessAdmissionAsync(request, launchSettings, replaceIds, cancellationToken);
        if (request.InteractivePrompts && plan.Action != RuntimeLaunchAdmissionAction.Allow)
        {
            if (request.ConfirmAdmissionAsync is null)
                throw new InvalidOperationException("Interactive launch admission requires a confirmation handler.");
            if (replaceIds.Count > 0)
                plan = plan with
                {
                    InteractiveMessage = $"{plan.Message}\n\nReplace {replaceIds.Count} loaded profile(s) of {request.Model.Name} and load this profile? Other models will keep serving. Current free-memory readings include the profiles being replaced."
                };
            if (!await request.ConfirmAdmissionAsync(plan, cancellationToken))
                return new(false, launchSettings, "");
        }
        else if (!request.InteractivePrompts && plan.BlocksLaunch)
            throw new InvalidOperationException(plan.GatewayBlockMessage);

        // All cancellable preflight and user decisions precede the first stop.
        cancellationToken.ThrowIfCancellationRequested();
        if (replaceIds.Count > 0)
        {
            if (!File.Exists(request.Model.ModelPath))
                throw new InvalidOperationException("The model file was not found. Existing profiles were kept running.");
            try
            {
                foreach (var sessionId in replaceIds)
                    await _runtimeSessions.Sessions.StopAsync(sessionId, "Replaced by another profile of the same model", cancellationToken);
            }
            finally
            {
                if (request.SessionsReplacedAsync is { } changed) await changed();
            }
            _runtimeSessions.EnsureLaunchPortAvailable(request.Model.Id, launchSettings,
                request.AutoLoadGatewayEnabled, request.AutoLoadGatewayPort, request.LaunchProfileId);
        }

        return Ready(launchSettings, request.InteractivePrompts ? plan.Message : plan.GatewayStatusMessage);
    }

    private async Task<RuntimeLaunchAdmissionPlan> AssessAdmissionAsync(
        ModelRuntimeLaunchPreparationRequest request,
        AppSettings launchSettings,
        IReadOnlySet<string> replaceIds,
        CancellationToken cancellationToken)
    {
        var hasRunningGpuSessions = _runtimeSessions.Sessions.Snapshots().Any(session =>
            session.IsRunning && !replaceIds.Contains(session.SessionId)
            && session.Backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Sycl or RuntimeBackend.Rocm);
        var memory = _runtimeLaunchAdmission.RequiresMemoryProbe(hasRunningGpuSessions, request.Runtime)
            ? await ReadMemoryAsync(request, cancellationToken)
            : null;
        return _runtimeLaunchAdmission.Assess(
            request.Runtime,
            request.Model,
            launchSettings,
            hasRunningGpuSessions,
            memory);
    }

    private async Task<VramMemorySnapshot?> ReadMemoryAsync(
        ModelRuntimeLaunchPreparationRequest request,
        CancellationToken cancellationToken)
    {
        if (request.ReadMemoryAsync is not null)
            return await request.ReadMemoryAsync(cancellationToken);
        return await _gpuStatus.MemoryAsync(cancellationToken);
    }

    private static ModelRuntimeLaunchPreparationResult Ready(AppSettings launchSettings, string statusMessage)
        => new(true, launchSettings, statusMessage);
}
