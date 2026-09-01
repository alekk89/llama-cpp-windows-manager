namespace LocalLlmConsole.Services;

public static class LlamaCppArgumentBuilder
{
    public static IReadOnlyList<string> Build(RuntimeLaunchRequest request)
    {
        var validation = LlamaCppLaunchValidator.Validate(request);
        if (!validation.Ok) throw new InvalidOperationException(string.Join(" ", validation.Errors));
        var host = (request.Host ?? "").Trim();
        var ropeScaling = (request.RopeScaling ?? "auto").Trim().ToLowerInvariant();
        var promptCacheMode = (request.PromptCacheMode ?? "auto").Trim().ToLowerInvariant();
        var contextCheckpointsMode = (request.ContextCheckpointsMode ?? "auto").Trim().ToLowerInvariant();
        var speculativeType = LaunchSettingMetadataService.NormalizeSpeculativeType(request.SpeculativeType);
        var llamaSpeculativeType = LaunchSettingMetadataService.LlamaSpeculativeTypeArgument(speculativeType);
        var args = new List<string>
        {
            "--model", request.ModelPath,
            "--host", host,
            "--port", request.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ctx-size", request.ContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        // API key is passed via LLAMA_API_KEY environment variable (not CLI arg)
        // to avoid exposure in process command lines visible to Task Manager / WMI.
        if (request.Backend is RuntimeBackend.Cuda or RuntimeBackend.Vulkan or RuntimeBackend.Metal or RuntimeBackend.Sycl or RuntimeBackend.Rocm)
        {
            args.AddRange(["--n-gpu-layers", request.GpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            var gpuMode = LaunchSettingMetadataService.NormalizeGpuMode(request.GpuMode);
            var gpuDevices = LaunchSettingMetadataService.NormalizeGpuCsv(request.GpuDevices);
            var gpuSplit = LaunchSettingMetadataService.NormalizeGpuCsv(request.GpuSplit);
            if (gpuMode != AppSettings.DefaultGpuMode)
                args.AddRange(["--split-mode", LaunchSettingMetadataService.LlamaSplitModeArgument(gpuMode)]);
            if (gpuDevices.Length > 0)
                args.AddRange(["--device", gpuDevices]);
            if (gpuSplit.Length > 0)
                args.AddRange(["--tensor-split", gpuSplit]);
            if (!string.IsNullOrWhiteSpace(request.TensorBufferOverrides))
                args.AddRange(["--override-tensor", request.TensorBufferOverrides.Trim()]);
        }
        args.AddRange([
            "--parallel", request.ParallelSlots.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--batch-size", request.BatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--ubatch-size", request.MicroBatchSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--flash-attn", request.FlashAttention,
            "--cache-type-k", request.CacheTypeK,
            "--cache-type-v", request.CacheTypeV,
            "--temp", request.Temperature.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "--top-k", request.TopK.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--top-p", request.TopP.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "--min-p", request.MinP.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "--repeat-last-n", request.RepeatLastN.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--repeat-penalty", request.RepeatPenalty.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "--presence-penalty", request.PresencePenalty.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            "--frequency-penalty", request.FrequencyPenalty.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
        ]);
        if (request.MaxTokens >= 0)
            args.AddRange(["--predict", request.MaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (request.Seed >= 0)
            args.AddRange(["--seed", request.Seed.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (request.Threads > 0)
            args.AddRange(["--threads", request.Threads.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (ropeScaling != "auto")
            args.AddRange(["--rope-scaling", ropeScaling]);
        if (request.RopeScale > 0)
            args.AddRange(["--rope-scale", request.RopeScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]);
        if (request.RopeFreqBase > 0)
            args.AddRange(["--rope-freq-base", request.RopeFreqBase.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]);
        if (request.RopeFreqScale > 0)
            args.AddRange(["--rope-freq-scale", request.RopeFreqScale.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]);
        if (request.KvOffload == "on")
            args.Add("--kv-offload");
        else if (request.KvOffload == "off")
            args.Add("--no-kv-offload");
        if (request.KvUnified == "on")
            args.Add("--kv-unified");
        else if (request.KvUnified == "off")
            args.Add("--no-kv-unified");
        if (promptCacheMode == "on")
            args.AddRange(["--cache-ram", request.PromptCacheRamMb.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        else if (promptCacheMode == "off")
            args.AddRange(["--cache-ram", "0"]);
        if (contextCheckpointsMode == "on")
        {
            args.AddRange(["--ctx-checkpoints", request.ContextCheckpointCount.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
            args.AddRange(["--checkpoint-min-step", request.ContextCheckpointEveryNTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        }
        else if (contextCheckpointsMode == "off")
        {
            args.AddRange(["--ctx-checkpoints", "0"]);
        }
        if (request.ContinuousBatching == "on")
            args.Add("--cont-batching");
        else if (request.ContinuousBatching == "off")
            args.Add("--no-cont-batching");
        if (request.ReasoningMode != "auto")
            args.AddRange(["--reasoning", request.ReasoningMode]);
        if (request.ReasoningFormat != "auto")
            args.AddRange(["--reasoning-format", request.ReasoningFormat]);
        if (request.ReasoningEffort != "default")
            args.AddRange(["--reasoning-effort", request.ReasoningEffort]);
        if (request.ReasoningBudget >= 0)
            args.AddRange(["--reasoning-budget", request.ReasoningBudget.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (!string.IsNullOrWhiteSpace(request.ReasoningBudgetMessage))
            args.AddRange(["--reasoning-budget-message", request.ReasoningBudgetMessage]);
        if (request.ReasoningPreserve == "on")
            args.Add("--reasoning-preserve");
        else if (request.ReasoningPreserve == "off")
            args.Add("--no-reasoning-preserve");
        if (request.VisionMode == "off")
            args.Add("--no-mmproj");
        else if (!request.VisionProjectorEmbedded && !string.IsNullOrWhiteSpace(request.VisionProjectorPath))
            args.AddRange(["--mmproj", request.VisionProjectorPath]);
        if (request.VisionMode != "off" && request.VisionImageMinTokens > 0)
            args.AddRange(["--image-min-tokens", request.VisionImageMinTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (request.VisionMode != "off" && request.VisionImageMaxTokens > 0)
            args.AddRange(["--image-max-tokens", request.VisionImageMaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (request.JinjaMode == "on")
            args.Add("--jinja");
        else if (request.JinjaMode == "off")
            args.Add("--no-jinja");
        if (request.MmapMode == "on")
            args.Add("--mmap");
        else if (request.MmapMode == "off")
            args.Add("--no-mmap");
        if (request.MlockMode == "on")
            args.Add("--mlock");
        if (speculativeType != "none")
        {
            args.AddRange(["--spec-type", llamaSpeculativeType]);
            if (LaunchSettingMetadataService.IsAtomicMtpSpeculativeType(speculativeType))
            {
                args.AddRange(["--mtp-head", request.MtpHeadPath!.Trim()]);
            }
            else if (speculativeType.StartsWith("draft-", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(request.SpecDraftModelPath))
                    args.AddRange(["--model-draft", request.SpecDraftModelPath.Trim()]);
                if (request.SpecDraftGpuLayers >= 0)
                    args.AddRange(["--n-gpu-layers-draft", request.SpecDraftGpuLayers.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                if (request.SpecDraftMinTokens > 0)
                    args.AddRange(["--spec-draft-n-min", request.SpecDraftMinTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                if (request.SpecDraftMaxTokens > 0)
                    args.AddRange(["--spec-draft-n-max", request.SpecDraftMaxTokens.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
                if (request.SpecDraftPSplit >= 0)
                    args.AddRange(["--spec-draft-p-split", request.SpecDraftPSplit.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]);
                if (request.SpecDraftPMin >= 0)
                    args.AddRange(["--spec-draft-p-min", request.SpecDraftPMin.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)]);
                args.AddRange([
                    "--cache-type-k-draft", request.SpecDraftCacheTypeK,
                    "--cache-type-v-draft", request.SpecDraftCacheTypeV
                ]);
            }
        }
        args.AddRange(request.ExtraArgs.Where(arg => !string.IsNullOrWhiteSpace(arg)));
        return args;
    }
}
