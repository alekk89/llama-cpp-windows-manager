namespace LocalLlmConsole.Services;

public enum RuntimeLaunchAdmissionAction
{
    Allow,
    Warn,
    Block
}

public sealed record RuntimeLaunchAdmissionPlan(
    RuntimeLaunchAdmissionAction Action,
    string Message,
    string InteractiveMessage,
    string GatewayStatusMessage,
    string GatewayBlockMessage)
{
    public bool RequiresInteractiveConfirmation => Action == RuntimeLaunchAdmissionAction.Warn;
    public bool BlocksLaunch => Action == RuntimeLaunchAdmissionAction.Block;
}

public sealed class RuntimeLaunchAdmissionService
{
    private readonly VramAdmissionService _vramAdmission;

    public RuntimeLaunchAdmissionService(VramAdmissionService vramAdmission)
    {
        _vramAdmission = vramAdmission ?? throw new ArgumentNullException(nameof(vramAdmission));
    }

    public bool RequiresMemoryProbe(bool hasRunningGpuSessions, RuntimeRecord runtime)
        => RequiresAdmissionCheck(hasRunningGpuSessions, runtime) && runtime.Backend == RuntimeBackend.Cuda;

    public RuntimeLaunchAdmissionPlan Assess(
        RuntimeRecord runtime,
        ModelRecord model,
        AppSettings launchSettings,
        bool hasRunningGpuSessions,
        VramMemorySnapshot? memory)
    {
        if (!RequiresAdmissionCheck(hasRunningGpuSessions, runtime))
            return new RuntimeLaunchAdmissionPlan(RuntimeLaunchAdmissionAction.Allow, "", "", "", "");

        var result = _vramAdmission.Assess(model, runtime, launchSettings, memory);
        return result.Decision switch
        {
            VramAdmissionDecision.Block => new RuntimeLaunchAdmissionPlan(
                RuntimeLaunchAdmissionAction.Block,
                result.Message,
                $"{result.Message}\n\nUnload another model or reduce GPU layers/context before loading {model.Name}.",
                "",
                $"{result.Message} Auto-load gateway refused to load {model.Name} while keeping existing models loaded. Switch Gateway policy to Single active model, unload another model, or reduce GPU layers/context."),
            VramAdmissionDecision.Warn => new RuntimeLaunchAdmissionPlan(
                RuntimeLaunchAdmissionAction.Warn,
                result.Message,
                $"{result.Message}\n\nLoad {model.Name} anyway? Existing loaded models will keep serving on their own ports.",
                $"Gateway loading {model.Name} while keeping existing models loaded: {result.Message}",
                ""),
            _ => new RuntimeLaunchAdmissionPlan(RuntimeLaunchAdmissionAction.Allow, result.Message, "", "", "")
        };
    }

    private static bool RequiresAdmissionCheck(bool hasRunningGpuSessions, RuntimeRecord runtime)
        => hasRunningGpuSessions && runtime.Backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Sycl;
}
