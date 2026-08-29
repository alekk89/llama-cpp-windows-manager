using static LocalLlmConsole.Services.RuntimeMetricSummaryCalculations;

namespace LocalLlmConsole.Services;

public sealed partial class RuntimeMetricSummaryTracker
{
    private static RuntimeMetricDisplayState MergeWithPrevious(
        RuntimeMetricObservation current,
        RuntimeMetricDisplaySnapshot? previous,
        DateTimeOffset now)
    {
        var displayGeneratedTokens = RuntimeDashboardService.MaxNullable(current.GeneratedTokens, previous?.GeneratedTokens);
        var displayPromptTokens = RuntimeDashboardService.MaxNullable(current.PromptTokens, previous?.PromptTokens);
        var displayMtpGeneratedTokens = RuntimeDashboardService.MaxNullable(current.MtpGeneratedTokens, previous?.MtpGeneratedTokens);
        var displayMtpAcceptedTokens = RuntimeDashboardService.MaxNullable(current.MtpAcceptedTokens, previous?.MtpAcceptedTokens);
        var displayAverageGenerationRate = current.AverageGenerationRate ?? previous?.AverageGenerationRate;
        var displayAveragePromptRate = current.AveragePromptRate ?? previous?.AveragePromptRate;
        var displayAverageMtpGeneratedRate = current.AverageMtpGeneratedRate ?? previous?.AverageMtpGeneratedRate;
        var displayAverageMtpAcceptedRate = current.AverageMtpAcceptedRate ?? previous?.AverageMtpAcceptedRate;
        var usedPreviousGeneratedTokens = UsedPreviousCounter(current.GeneratedTokens, previous?.GeneratedTokens, displayGeneratedTokens);
        var usedPreviousPromptTokens = UsedPreviousCounter(current.PromptTokens, previous?.PromptTokens, displayPromptTokens);
        var usedPreviousMtpGeneratedTokens = UsedPreviousCounter(current.MtpGeneratedTokens, previous?.MtpGeneratedTokens, displayMtpGeneratedTokens);
        var usedPreviousMtpAcceptedTokens = UsedPreviousCounter(current.MtpAcceptedTokens, previous?.MtpAcceptedTokens, displayMtpAcceptedTokens);
        var usedPreviousAverageGenerationRate = UsedPreviousAverage(current.AverageGenerationRate, previous?.AverageGenerationRate);
        var usedPreviousAveragePromptRate = UsedPreviousAverage(current.AveragePromptRate, previous?.AveragePromptRate);
        var usedPreviousAverageMtpGeneratedRate = UsedPreviousAverage(current.AverageMtpGeneratedRate, previous?.AverageMtpGeneratedRate);
        var usedPreviousAverageMtpAcceptedRate = UsedPreviousAverage(current.AverageMtpAcceptedRate, previous?.AverageMtpAcceptedRate);
        var generatedTokensCapturedAt = DisplayValueCapturedAt(current.GeneratedTokens, displayGeneratedTokens, previous?.GeneratedTokensCapturedAt ?? previous?.CapturedAt, now);
        var promptTokensCapturedAt = DisplayValueCapturedAt(current.PromptTokens, displayPromptTokens, previous?.PromptTokensCapturedAt ?? previous?.CapturedAt, now);
        var mtpGeneratedTokensCapturedAt = DisplayValueCapturedAt(current.MtpGeneratedTokens, displayMtpGeneratedTokens, previous?.MtpGeneratedTokensCapturedAt ?? previous?.CapturedAt, now);
        var mtpAcceptedTokensCapturedAt = DisplayValueCapturedAt(current.MtpAcceptedTokens, displayMtpAcceptedTokens, previous?.MtpAcceptedTokensCapturedAt ?? previous?.CapturedAt, now);
        var averageGenerationRateCapturedAt = DisplayValueCapturedAt(current.AverageGenerationRate, displayAverageGenerationRate, previous?.AverageGenerationRateCapturedAt ?? previous?.CapturedAt, now);
        var averagePromptRateCapturedAt = DisplayValueCapturedAt(current.AveragePromptRate, displayAveragePromptRate, previous?.AveragePromptRateCapturedAt ?? previous?.CapturedAt, now);
        var averageMtpGeneratedRateCapturedAt = DisplayValueCapturedAt(current.AverageMtpGeneratedRate, displayAverageMtpGeneratedRate, previous?.AverageMtpGeneratedRateCapturedAt ?? previous?.CapturedAt, now);
        var averageMtpAcceptedRateCapturedAt = DisplayValueCapturedAt(current.AverageMtpAcceptedRate, displayAverageMtpAcceptedRate, previous?.AverageMtpAcceptedRateCapturedAt ?? previous?.CapturedAt, now);
        var usedLastKnown = usedPreviousGeneratedTokens
            || usedPreviousPromptTokens
            || usedPreviousMtpGeneratedTokens
            || usedPreviousMtpAcceptedTokens
            || usedPreviousAverageGenerationRate
            || usedPreviousAveragePromptRate
            || usedPreviousAverageMtpGeneratedRate
            || usedPreviousAverageMtpAcceptedRate;

        return new RuntimeMetricDisplayState
        {
            GeneratedTokens = displayGeneratedTokens,
            PromptTokens = displayPromptTokens,
            MtpGeneratedTokens = displayMtpGeneratedTokens,
            MtpAcceptedTokens = displayMtpAcceptedTokens,
            AverageGenerationRate = displayAverageGenerationRate,
            AveragePromptRate = displayAveragePromptRate,
            AverageMtpGeneratedRate = displayAverageMtpGeneratedRate,
            AverageMtpAcceptedRate = displayAverageMtpAcceptedRate,
            GeneratedTokensCapturedAt = generatedTokensCapturedAt,
            PromptTokensCapturedAt = promptTokensCapturedAt,
            MtpGeneratedTokensCapturedAt = mtpGeneratedTokensCapturedAt,
            MtpAcceptedTokensCapturedAt = mtpAcceptedTokensCapturedAt,
            AverageGenerationRateCapturedAt = averageGenerationRateCapturedAt,
            AveragePromptRateCapturedAt = averagePromptRateCapturedAt,
            AverageMtpGeneratedRateCapturedAt = averageMtpGeneratedRateCapturedAt,
            AverageMtpAcceptedRateCapturedAt = averageMtpAcceptedRateCapturedAt,
            UsedLastKnown = usedLastKnown,
            LastKnownCapturedAt = OldestCapturedAt(
                usedPreviousGeneratedTokens ? generatedTokensCapturedAt : null,
                usedPreviousPromptTokens ? promptTokensCapturedAt : null,
                usedPreviousMtpGeneratedTokens ? mtpGeneratedTokensCapturedAt : null,
                usedPreviousMtpAcceptedTokens ? mtpAcceptedTokensCapturedAt : null,
                usedPreviousAverageGenerationRate ? averageGenerationRateCapturedAt : null,
                usedPreviousAveragePromptRate ? averagePromptRateCapturedAt : null,
                usedPreviousAverageMtpGeneratedRate ? averageMtpGeneratedRateCapturedAt : null,
                usedPreviousAverageMtpAcceptedRate ? averageMtpAcceptedRateCapturedAt : null),
            SnapshotCapturedAt = usedLastKnown && previous is not null ? previous.CapturedAt : now
        };
    }

    private sealed class RuntimeMetricDisplayState
    {
        public double? GeneratedTokens { get; init; }
        public double? PromptTokens { get; init; }
        public double? MtpGeneratedTokens { get; init; }
        public double? MtpAcceptedTokens { get; init; }
        public double? AverageGenerationRate { get; init; }
        public double? AveragePromptRate { get; init; }
        public double? AverageMtpGeneratedRate { get; init; }
        public double? AverageMtpAcceptedRate { get; init; }
        public DateTimeOffset? GeneratedTokensCapturedAt { get; init; }
        public DateTimeOffset? PromptTokensCapturedAt { get; init; }
        public DateTimeOffset? MtpGeneratedTokensCapturedAt { get; init; }
        public DateTimeOffset? MtpAcceptedTokensCapturedAt { get; init; }
        public DateTimeOffset? AverageGenerationRateCapturedAt { get; init; }
        public DateTimeOffset? AveragePromptRateCapturedAt { get; init; }
        public DateTimeOffset? AverageMtpGeneratedRateCapturedAt { get; init; }
        public DateTimeOffset? AverageMtpAcceptedRateCapturedAt { get; init; }
        public bool UsedLastKnown { get; init; }
        public DateTimeOffset? LastKnownCapturedAt { get; init; }
        public required DateTimeOffset SnapshotCapturedAt { get; init; }
    }
}
