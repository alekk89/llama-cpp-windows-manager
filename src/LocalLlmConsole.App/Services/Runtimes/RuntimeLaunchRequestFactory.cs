namespace LocalLlmConsole.Services;

public sealed record RuntimeLaunchRequestContext(
    RuntimeMode Mode,
    RuntimeBackend Backend,
    string ExecutablePath,
    string ModelPath,
    string Host,
    bool AllowNetworkAccess,
    string VisionProjectorPath = "",
    bool VisionProjectorEmbedded = false,
    string DraftModelPath = "",
    string MtpHeadPath = "",
    IReadOnlyList<string>? ExtraArguments = null);

public static class RuntimeLaunchRequestFactory
{
    public static RuntimeLaunchRequest Create(AppSettings settings, RuntimeLaunchRequestContext context)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(context);
        return new RuntimeLaunchRequest
        {
            Mode = context.Mode,
            Backend = context.Backend,
            ExecutablePath = context.ExecutablePath,
            ModelPath = context.ModelPath,
            WslDistro = context.Mode == RuntimeMode.Wsl ? settings.WslDistro : "",
            Host = context.Host,
            AllowNetworkAccess = context.AllowNetworkAccess,
            ApiKey = settings.ModelApiKey,
            RequireApiKeyAuth = true,
            Port = settings.Port,
            ContextSize = settings.ContextSize,
            GpuLayers = settings.GpuLayers,
            GpuMode = settings.GpuMode,
            GpuDevices = settings.GpuDevices,
            GpuSplit = settings.GpuSplit,
            ParallelSlots = settings.ParallelSlots,
            BatchSize = settings.BatchSize,
            MicroBatchSize = settings.MicroBatchSize,
            Threads = settings.Threads,
            FlashAttention = settings.FlashAttention,
            CacheTypeK = settings.CacheTypeK,
            CacheTypeV = settings.CacheTypeV,
            KvOffload = settings.KvOffload,
            KvUnified = settings.KvUnified,
            PromptCacheMode = settings.PromptCacheMode,
            PromptCacheRamMb = settings.PromptCacheRamMb,
            ContextCheckpointsMode = settings.ContextCheckpointsMode,
            ContextCheckpointCount = settings.ContextCheckpointCount,
            ContextCheckpointEveryNTokens = settings.ContextCheckpointEveryNTokens,
            ContinuousBatching = settings.ContinuousBatching,
            ReasoningMode = settings.ReasoningMode,
            ReasoningFormat = settings.ReasoningFormat,
            ReasoningEffort = settings.ReasoningEffort,
            ReasoningBudget = settings.ReasoningBudget,
            ReasoningBudgetMessage = settings.ReasoningBudgetMessage,
            ReasoningPreserve = settings.ReasoningPreserve,
            VisionMode = settings.VisionMode,
            VisionProjectorPath = context.VisionProjectorPath,
            VisionProjectorEmbedded = context.VisionProjectorEmbedded,
            VisionImageMinTokens = settings.VisionImageMinTokens,
            VisionImageMaxTokens = settings.VisionImageMaxTokens,
            JinjaMode = settings.JinjaMode,
            MmapMode = settings.MmapMode,
            MlockMode = settings.MlockMode,
            Temperature = settings.Temperature,
            TopK = settings.TopK,
            TopP = settings.TopP,
            MinP = settings.MinP,
            MaxTokens = settings.MaxTokens,
            Seed = settings.Seed,
            RepeatLastN = settings.RepeatLastN,
            RepeatPenalty = settings.RepeatPenalty,
            PresencePenalty = settings.PresencePenalty,
            FrequencyPenalty = settings.FrequencyPenalty,
            RopeScaling = settings.RopeScaling,
            RopeScale = settings.RopeScale,
            RopeFreqBase = settings.RopeFreqBase,
            RopeFreqScale = settings.RopeFreqScale,
            SpeculativeType = settings.SpeculativeType,
            SpecDraftModelPath = context.DraftModelPath,
            MtpHeadPath = context.MtpHeadPath,
            SpecDraftGpuLayers = settings.SpecDraftGpuLayers,
            SpecDraftMinTokens = settings.SpecDraftMinTokens,
            SpecDraftMaxTokens = settings.SpecDraftMaxTokens,
            SpecDraftPSplit = settings.SpecDraftPSplit,
            SpecDraftPMin = settings.SpecDraftPMin,
            SpecDraftCacheTypeK = settings.SpecDraftCacheTypeK,
            SpecDraftCacheTypeV = settings.SpecDraftCacheTypeV,
            ExtraArgs = context.ExtraArguments ?? []
        };
    }

    public static string Preview(AppSettings settings, RuntimeChoice? runtime)
    {
        var previewSettings = settings with
        {
            RequireApiKeyAuth = true,
            ModelApiKey = new string('x', 32)
        };
        var custom = CustomLaunchParameterParser.Parse(previewSettings.CustomParameters);
        RuntimeLaunchOptionPolicy.ValidateCustomArguments(custom);
        var extra = new List<string>();
        if (previewSettings.EnableMetrics) extra.Add("--metrics");
        extra.AddRange(custom);
        var speculativeType = LaunchSettingMetadataService.NormalizeSpeculativeType(previewSettings.SpeculativeType);
        var request = Create(previewSettings, new RuntimeLaunchRequestContext(
            runtime?.Mode ?? RuntimeMode.Native,
            runtime?.Backend ?? RuntimeBackend.Cpu,
            runtime?.ExecutablePath ?? "llama-server",
            "<model.gguf>",
            "127.0.0.1",
            false,
            previewSettings.VisionProjectorPath,
            VisionProjectorEmbedded: string.Equals(previewSettings.VisionMode, "on", StringComparison.OrdinalIgnoreCase) && string.IsNullOrWhiteSpace(previewSettings.VisionProjectorPath),
            DraftModelPath: previewSettings.SpecDraftModelPath,
            MtpHeadPath: LaunchSettingMetadataService.IsAtomicMtpSpeculativeType(speculativeType) && string.IsNullOrWhiteSpace(previewSettings.MtpHeadPath) ? "<mtp-head.gguf>" : previewSettings.MtpHeadPath,
            ExtraArguments: extra));
        var executable = string.IsNullOrWhiteSpace(runtime?.ExecutablePath) ? "llama-server" : Path.GetFileName(runtime.ExecutablePath);
        return LaunchArgumentText.Format(new[] { executable }.Concat(RuntimeAdapter.BuildArgs(request)));
    }
}
