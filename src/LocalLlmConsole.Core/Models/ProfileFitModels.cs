namespace LocalLlmConsole.Models;

[Flags]
public enum ProfileFitAdjustment
{
    None = 0,
    ContextSize = 1,
    GpuLayers = 2,
    TensorSplit = 4,
    TensorBufferOverrides = 8,
    All = ContextSize | GpuLayers | TensorSplit | TensorBufferOverrides
}

public sealed record ProfileFitRequest(
    string ModelPath,
    RuntimeRecord Runtime,
    ModelLaunchSettings CurrentProfile,
    int DesiredMaximumContext,
    int MinimumContext,
    IReadOnlyList<int> ReservedVramMiBPerGpu,
    string WslDistro = "",
    ProfileFitAdjustment AllowedAdjustments = ProfileFitAdjustment.All);

public sealed record ProfileFitDeviceEstimate(
    string Device,
    long UsedMiB,
    long FreeMiB);

public sealed record ProfileFitProposal(
    int ContextSize,
    int GpuLayers,
    string GpuSplit,
    string TensorBufferOverrides);

public sealed record ProfileFitResult(
    bool Success,
    ProfileFitProposal? Proposal,
    IReadOnlyList<ProfileFitDeviceEstimate> DeviceEstimates,
    IReadOnlyList<string> Warnings,
    string GeneratedArguments,
    string Diagnostics,
    string Error);

public sealed record ProfileFitRuntimeCapability(
    string RuntimeId,
    bool SupportsFitParams,
    string FitParamsExecutablePath,
    string Error);
