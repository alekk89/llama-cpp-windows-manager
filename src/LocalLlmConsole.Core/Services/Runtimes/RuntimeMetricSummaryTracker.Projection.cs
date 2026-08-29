namespace LocalLlmConsole.Services;

public sealed partial class RuntimeMetricSummaryTracker
{
    private static RuntimeMetricProjection ProjectResult(
        string runtimeKey,
        AppSettings metricsSettings,
        RuntimeMetricObservation observation,
        RuntimeMetricDisplayState display)
    {
        var generationRateText = $"Gen {RuntimeDashboardService.RateLabel(observation.LiveGenerationRate, display.AverageGenerationRate)}\nPrompt {RuntimeDashboardService.RateLabel(observation.LivePromptRate, display.AveragePromptRate)}";
        var totalTokensText = RuntimeDashboardService.TokenSummaryLabel(display.GeneratedTokens, display.PromptTokens);
        var tokensText = RuntimeDashboardService.TokenAverageAndTotalSummaryLabel(
            display.AverageGenerationRate,
            display.AveragePromptRate,
            display.GeneratedTokens,
            display.PromptTokens,
            observation.PromptTokensCached);
        var mtpTokensText = MtpTokensText(
            metricsSettings,
            observation.LiveMtpGeneratedRate,
            display.AverageMtpGeneratedRate,
            observation.LiveMtpAcceptedRate,
            display.AverageMtpAcceptedRate,
            display.MtpGeneratedTokens,
            display.MtpAcceptedTokens);
        var capacity = observation.Capacity;
        var atomic = new RuntimeMetricAtomicSnapshot(
            observation.LiveGenerationRate,
            observation.LivePromptRate,
            display.AverageGenerationRate,
            display.AveragePromptRate,
            display.GeneratedTokens,
            display.PromptTokens,
            observation.LiveMtpGeneratedRate ?? observation.AverageMtpGeneratedRate,
            observation.LiveMtpAcceptedRate ?? observation.AverageMtpAcceptedRate,
            display.AverageMtpGeneratedRate,
            display.AverageMtpAcceptedRate,
            display.MtpGeneratedTokens,
            display.MtpAcceptedTokens,
            capacity.ActiveSlots,
            capacity.SlotCapacity,
            capacity.QueuedRequests,
            capacity.BusyDecodeSlots,
            capacity.KvTokens,
            capacity.ContextCapacityTokens,
            capacity.KvUsagePercent,
            metricsSettings.KvUnified.ToLowerInvariant() switch
            {
                "on" => "Unified",
                "off" => "Partitioned",
                _ => "Automatic"
            },
            observation.LiveGenerationRate,
            observation.LivePromptRate,
            observation.PromptTokensCached,
            observation.PromptCacheReusePercent,
            observation.DraftAcceptancePercent,
            observation.PeakContextTokens,
            observation.ContextShiftCount);
        var update = new RuntimeMetricDisplayUpdate
        {
            TokensText = tokensText,
            GenerationRateText = generationRateText,
            TotalTokensText = totalTokensText,
            MtpTokensText = mtpTokensText,
            SlotsText = capacity.SlotsText,
            SettingsText = capacity.SettingsText,
            GeneratedTokens = display.GeneratedTokens,
            PromptTokens = display.PromptTokens,
            MtpGeneratedTokens = display.MtpGeneratedTokens,
            MtpAcceptedTokens = display.MtpAcceptedTokens,
            AverageGenerationRate = display.AverageGenerationRate,
            AveragePromptRate = display.AveragePromptRate,
            AverageMtpGeneratedRate = display.AverageMtpGeneratedRate,
            AverageMtpAcceptedRate = display.AverageMtpAcceptedRate,
            GeneratedTokensCapturedAt = display.GeneratedTokensCapturedAt,
            PromptTokensCapturedAt = display.PromptTokensCapturedAt,
            MtpGeneratedTokensCapturedAt = display.MtpGeneratedTokensCapturedAt,
            MtpAcceptedTokensCapturedAt = display.MtpAcceptedTokensCapturedAt,
            AverageGenerationRateCapturedAt = display.AverageGenerationRateCapturedAt,
            AveragePromptRateCapturedAt = display.AveragePromptRateCapturedAt,
            AverageMtpGeneratedRateCapturedAt = display.AverageMtpGeneratedRateCapturedAt,
            AverageMtpAcceptedRateCapturedAt = display.AverageMtpAcceptedRateCapturedAt,
            CapturedAt = display.SnapshotCapturedAt,
            Atomic = atomic
        };
        var result = new RuntimeMetricSummaryResult(
            tokensText,
            generationRateText,
            totalTokensText,
            mtpTokensText,
            capacity.SlotsText,
            capacity.SettingsText,
            display.UsedLastKnown,
            display.UsedLastKnown ? display.LastKnownCapturedAt : null,
            new RuntimeMetricGraphSample(
                runtimeKey,
                display.AverageGenerationRate,
                display.AveragePromptRate,
                observation.LiveMtpGeneratedRate ?? observation.AverageMtpGeneratedRate,
                observation.LiveMtpAcceptedRate ?? observation.AverageMtpAcceptedRate,
                capacity.KvUsagePercent),
            atomic);
        return new RuntimeMetricProjection(update, result);
    }

    private sealed record RuntimeMetricProjection(
        RuntimeMetricDisplayUpdate Update,
        RuntimeMetricSummaryResult Result);
}
