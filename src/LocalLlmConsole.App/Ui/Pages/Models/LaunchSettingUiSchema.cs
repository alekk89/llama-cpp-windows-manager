namespace LocalLlmConsole;

public enum LaunchSettingEditorKind
{
    Text,
    Choice,
    VisionProjector,
    DraftModel,
    MtpHead
}

public sealed record LaunchSettingUiDefinition(
    string Id,
    string SectionKey,
    string LabelKey,
    LaunchSettingEditorKind Editor = LaunchSettingEditorKind.Text,
    IReadOnlyList<string>? Choices = null,
    bool Advanced = false,
    bool AdvancedSection = false);

public static class LaunchSettingUiSchema
{
    public static IReadOnlyList<LaunchSettingUiDefinition> Definitions { get; } =
    [
        Text(nameof(AppSettings.ContextSize), "Basic", "ContextSize"),
        Text(nameof(AppSettings.Threads), "Basic", "Threads"),
        Text(nameof(AppSettings.GpuLayers), "Basic", "GpuLayers"),
        Choice(nameof(AppSettings.GpuMode), "Basic", "GpuMode", LaunchSettingMetadataService.GpuModeOptions),
        Text(nameof(AppSettings.GpuDevices), "Basic", "GpuDevices", advanced: true),
        Text(nameof(AppSettings.GpuSplit), "Basic", "GpuSplit", advanced: true),
        Text(nameof(AppSettings.BatchSize), "PerformanceMemory", "BatchSize"),
        Text(nameof(AppSettings.MicroBatchSize), "PerformanceMemory", "MicroBatch"),
        Choice(nameof(AppSettings.FlashAttention), "PerformanceMemory", "FlashAttention", LaunchSettingMetadataService.AutoOnOffOptions),
        Choice(nameof(AppSettings.CacheTypeK), "PerformanceMemory", "KCache", LaunchSettingMetadataService.CacheTypeOptions),
        Choice(nameof(AppSettings.CacheTypeV), "PerformanceMemory", "VCache", LaunchSettingMetadataService.CacheTypeOptions),
        Text(nameof(AppSettings.TensorBufferOverrides), "PerformanceMemory", "TensorBufferOverrides", advanced: true),
        Choice(nameof(AppSettings.KvOffload), "PerformanceMemory", "KvOffload", LaunchSettingMetadataService.AutoOnOffOptions, true),
        Choice(nameof(AppSettings.KvUnified), "PerformanceMemory", "UnifiedKv", LaunchSettingMetadataService.AutoOnOffOptions, true),
        Choice(nameof(AppSettings.PromptCacheMode), "PerformanceMemory", "PromptCache", LaunchSettingMetadataService.AutoOnOffOptions, true),
        Text(nameof(AppSettings.PromptCacheRamMb), "PerformanceMemory", "PromptCacheMb", true),
        Choice(nameof(AppSettings.ContextCheckpointsMode), "PerformanceMemory", "Checkpoints", LaunchSettingMetadataService.AutoOnOffOptions, true),
        Text(nameof(AppSettings.ContextCheckpointCount), "PerformanceMemory", "CheckpointCount", true),
        Text(nameof(AppSettings.ContextCheckpointEveryNTokens), "PerformanceMemory", "CheckpointSpacing", true),
        Choice(nameof(AppSettings.MmapMode), "PerformanceMemory", "MemoryMap", LaunchSettingMetadataService.AutoOnOffOptions, true),
        Choice(nameof(AppSettings.MlockMode), "PerformanceMemory", "MemoryLock", LaunchSettingMetadataService.OffOnOptions, true),

        Choice(nameof(AppSettings.SpeculativeType), "SpeculativeMtp", "SpecType", LaunchSettingMetadataService.SpeculativeTypeOptions),
        Picker(nameof(AppSettings.SpecDraftModelPath), "SpeculativeMtp", "DraftModel", LaunchSettingEditorKind.DraftModel),
        Picker(nameof(AppSettings.MtpHeadPath), "SpeculativeMtp", "MtpHead", LaunchSettingEditorKind.MtpHead),
        Choice(nameof(AppSettings.SpecDraftCacheTypeK), "SpeculativeMtp", "DraftKCache", LaunchSettingMetadataService.CacheTypeOptions),
        Choice(nameof(AppSettings.SpecDraftCacheTypeV), "SpeculativeMtp", "DraftVCache", LaunchSettingMetadataService.CacheTypeOptions),
        Text(nameof(AppSettings.SpecDraftMaxTokens), "SpeculativeMtp", "DraftMax"),
        Text(nameof(AppSettings.SpecDraftMinTokens), "SpeculativeMtp", "DraftMin"),
        Text(nameof(AppSettings.SpecDraftGpuLayers), "SpeculativeMtp", "DraftGpu", true),
        Text(nameof(AppSettings.SpecDraftPSplit), "SpeculativeMtp", "SplitProb", true),
        Text(nameof(AppSettings.SpecDraftPMin), "SpeculativeMtp", "MinProb", true),

        Choice(nameof(AppSettings.ReasoningMode), "ChatCapabilities", "Reasoning", LaunchSettingMetadataService.AutoOnOffOptions),
        Choice(nameof(AppSettings.ReasoningFormat), "ChatCapabilities", "ReasonFormat", LaunchSettingMetadataService.ReasoningFormatOptions),
        Choice(nameof(AppSettings.ReasoningEffort), "ChatCapabilities", "ReasoningEffort", LaunchSettingMetadataService.ReasoningEffortOptions),
        Text(nameof(AppSettings.ReasoningBudget), "ChatCapabilities", "ReasonBudget"),
        Text(nameof(AppSettings.ReasoningBudgetMessage), "ChatCapabilities", "ReasonBudgetMessage", advanced: true),
        Choice(nameof(AppSettings.ReasoningPreserve), "ChatCapabilities", "ReasonPreserve", LaunchSettingMetadataService.AutoOnOffOptions, advanced: true),
        Choice(nameof(AppSettings.JinjaMode), "ChatCapabilities", "JinjaChat", LaunchSettingMetadataService.AutoOnOffOptions),
        Choice(nameof(AppSettings.VisionMode), "ChatCapabilities", "Vision", LaunchSettingMetadataService.AutoOnOffOptions),
        Picker(nameof(AppSettings.VisionProjectorPath), "ChatCapabilities", "VisionHead", LaunchSettingEditorKind.VisionProjector),
        Text(nameof(AppSettings.VisionImageMinTokens), "ChatCapabilities", "ImageMin"),
        Text(nameof(AppSettings.VisionImageMaxTokens), "ChatCapabilities", "ImageMax"),

        Text(nameof(AppSettings.Temperature), "GenerationDefaults", "Temperature"),
        Text(nameof(AppSettings.TopK), "GenerationDefaults", "TopK"),
        Text(nameof(AppSettings.TopP), "GenerationDefaults", "TopP"),
        Text(nameof(AppSettings.MinP), "GenerationDefaults", "MinP"),
        Text(nameof(AppSettings.MaxTokens), "GenerationDefaults", "MaxTokens", true),
        Text(nameof(AppSettings.Seed), "GenerationDefaults", "Seed", true),
        Text(nameof(AppSettings.RepeatLastN), "GenerationDefaults", "RepeatWindow", true),
        Text(nameof(AppSettings.RepeatPenalty), "GenerationDefaults", "RepeatPen", true),
        Text(nameof(AppSettings.PresencePenalty), "GenerationDefaults", "Presence", true),
        Text(nameof(AppSettings.FrequencyPenalty), "GenerationDefaults", "Frequency", true),

        Choice(nameof(AppSettings.RopeScaling), "ContextExtension", "RopeScaling", LaunchSettingMetadataService.RopeScalingOptions, advancedSection: true),
        Text(nameof(AppSettings.RopeScale), "ContextExtension", "RopeScale", advancedSection: true),
        Text(nameof(AppSettings.RopeFreqBase), "ContextExtension", "RopeBase", advancedSection: true),
        Text(nameof(AppSettings.RopeFreqScale), "ContextExtension", "RopeFreq", advancedSection: true),

        Text(nameof(AppSettings.ParallelSlots), "Server", "ParallelSlots", advancedSection: true),
        Text(nameof(AppSettings.Host), "Server", "Host", advancedSection: true),
        Choice(nameof(AppSettings.ContinuousBatching), "Server", "ContinuousBatch", LaunchSettingMetadataService.OnOffOptions, advancedSection: true),
        Choice(nameof(AppSettings.EnableMetrics), "Server", "Metrics", LaunchSettingMetadataService.OnOffOptions, advancedSection: true),
        Text(nameof(AppSettings.CustomParameters), "Server", "CustomParams", advanced: true, advancedSection: true)
    ];

    private static LaunchSettingUiDefinition Text(string id, string section, string label, bool advanced = false, bool advancedSection = false)
        => new(id, $"Launch.Section.{section}", $"Launch.Field.{label}", Advanced: advanced, AdvancedSection: advancedSection);

    private static LaunchSettingUiDefinition Choice(string id, string section, string label, IReadOnlyList<string> choices, bool advanced = false, bool advancedSection = false)
        => new(id, $"Launch.Section.{section}", $"Launch.Field.{label}", LaunchSettingEditorKind.Choice, choices, advanced, advancedSection);

    private static LaunchSettingUiDefinition Picker(string id, string section, string label, LaunchSettingEditorKind editor)
        => new(id, $"Launch.Section.{section}", $"Launch.Field.{label}", editor);
}
