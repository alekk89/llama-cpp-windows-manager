namespace LocalLlmConsole.Services;

public delegate Task<AppSettings> ModelRuntimeApiKeyEnsurer(AppSettings launchSettings, CancellationToken cancellationToken);

public delegate Task<bool> RuntimeLaunchAdmissionConfirmation(RuntimeLaunchAdmissionPlan plan, CancellationToken cancellationToken);

public delegate Task<VramMemorySnapshot?> RuntimeLaunchMemoryReader(CancellationToken cancellationToken);

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
    RuntimeLaunchMemoryReader? ReadMemoryAsync = null);

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

        _runtimeSessions.EnsureLaunchPortAvailable(
            request.Model.Id,
            launchSettings,
            request.AutoLoadGatewayEnabled,
            request.AutoLoadGatewayPort);
        await _runtimeLaunchPrerequisites.EnsureReadyAsync(new RuntimeLaunchPrerequisiteRequest(
            request.Runtime,
            launchSettings,
            request.EndpointRespondingAsync), cancellationToken);

        var plan = await AssessAdmissionAsync(request, launchSettings, cancellationToken);
        if (request.InteractivePrompts)
        {
            if (plan.Action == RuntimeLaunchAdmissionAction.Allow)
                return Ready(launchSettings, plan.Message);
            if (request.ConfirmAdmissionAsync is null)
                throw new InvalidOperationException("Interactive launch admission requires a confirmation handler.");

            var confirmed = await request.ConfirmAdmissionAsync(plan, cancellationToken);
            return confirmed
                ? Ready(launchSettings, plan.Message)
                : new ModelRuntimeLaunchPreparationResult(false, launchSettings, "");
        }

        if (plan.BlocksLaunch)
            throw new InvalidOperationException(plan.GatewayBlockMessage);

        return Ready(launchSettings, plan.GatewayStatusMessage);
    }

    private async Task<RuntimeLaunchAdmissionPlan> AssessAdmissionAsync(
        ModelRuntimeLaunchPreparationRequest request,
        AppSettings launchSettings,
        CancellationToken cancellationToken)
    {
        var hasRunningGpuSessions = _runtimeSessions.Sessions.HasRunningGpuSessions;
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
