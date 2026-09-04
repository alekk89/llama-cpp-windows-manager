namespace LocalLlmConsole.Services;

public sealed record LaunchSettingsControlStateRequest(
    bool ShowAdvancedSections,
    RuntimeBackend? RuntimeBackend,
    bool VisionLaunchSettingsAvailable,
    string SpeculativeType);

public sealed record LaunchSettingsControlStatePlan(
    bool ShowAdvancedSections,
    bool GpuLayersAvailable,
    bool VisionLaunchSettingsAvailable,
    bool MtpHeadSettingsAvailable,
    bool DraftSpeculativeSettingsAvailable,
    IReadOnlyDictionary<string, bool> VisibleSettings,
    IReadOnlyDictionary<string, bool> EnabledSettings);

public sealed class LaunchSettingsControlStateService
{
    public static readonly string[] DraftSettingLabels =
    [
        "Draft model",
        "Draft GPU",
        "Draft K cache",
        "Draft V cache",
        "Draft max",
        "Draft min",
        "Split prob",
        "Min prob"
    ];

    public LaunchSettingsControlStatePlan Build(LaunchSettingsControlStateRequest request)
    {
        var gpuRuntime = request.RuntimeBackend is RuntimeBackend.Cuda
            or RuntimeBackend.Vulkan
            or RuntimeBackend.Metal
            or RuntimeBackend.Sycl
            or RuntimeBackend.Rocm;
        var visionAvailable = true;
        var speculativeType = LaunchSettingMetadataService.NormalizeSpeculativeType(request.SpeculativeType);
        var draftAvailable = speculativeType.StartsWith("draft-", StringComparison.OrdinalIgnoreCase);
        var mtpHeadAvailable = LaunchSettingMetadataService.IsAtomicMtpSpeculativeType(speculativeType);

        var visible = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["GPU layers"] = gpuRuntime,
            ["GPU mode"] = gpuRuntime,
            ["GPU devices"] = gpuRuntime,
            ["GPU split"] = gpuRuntime,
            [Loc.T("Launch.Field.VulkanAllocationBlockSize")] = request.RuntimeBackend == RuntimeBackend.Vulkan,
            ["Vision"] = visionAvailable,
            ["Vision head"] = visionAvailable,
            ["Image min"] = visionAvailable,
            ["Image max"] = visionAvailable,
            ["MTP head"] = true,
            ["Reasoning"] = true,
            ["Reason format"] = true,
            ["Reasoning effort"] = true,
            ["Reason budget"] = true,
            ["Budget message"] = true,
            ["Preserve reasoning"] = true,
            ["Jinja chat"] = true
        };

        var enabled = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["GPU layers"] = gpuRuntime,
            ["GPU mode"] = gpuRuntime,
            ["GPU devices"] = gpuRuntime,
            ["GPU split"] = gpuRuntime,
            [Loc.T("Launch.Field.VulkanAllocationBlockSize")] = request.RuntimeBackend == RuntimeBackend.Vulkan,
            ["Vision"] = visionAvailable,
            ["Vision head"] = visionAvailable,
            ["Image min"] = visionAvailable,
            ["Image max"] = visionAvailable
        };
        enabled["MTP head"] = mtpHeadAvailable;

        foreach (var label in DraftSettingLabels)
            enabled[label] = draftAvailable;

        return new LaunchSettingsControlStatePlan(
            request.ShowAdvancedSections,
            gpuRuntime,
            visionAvailable,
            mtpHeadAvailable,
            draftAvailable,
            visible,
            enabled);
    }
}
