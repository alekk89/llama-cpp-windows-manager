using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkApplicationService
{
    private static BenchmarkJobPayload UpdateCheckpoint(BenchmarkJobPayload payload, int index, BenchmarkWorkItemCheckpoint checkpoint)
    {
        var checkpoints = payload.Checkpoints.ToArray();
        checkpoints[index] = checkpoint;
        return payload with { Checkpoints = checkpoints };
    }

    private static BenchmarkJobPayload Recalculate(BenchmarkJobPayload payload)
        => payload with
        {
            CompletedWorkItems = payload.Checkpoints.Count(item => item.Status == BenchmarkWorkItemStatus.Passed),
            FailedWorkItems = payload.Checkpoints.Count(item => item.Status == BenchmarkWorkItemStatus.Failed),
            ResultRows = payload.Checkpoints.Sum(item => item.ResultRows)
        };

    private static bool ShouldRetry(BenchmarkFailurePolicy policy, int attempt)
        => attempt == 1 && policy is BenchmarkFailurePolicy.RetryOnceThenContinue or BenchmarkFailurePolicy.RetryOnceThenStop;
    private static bool ShouldStop(BenchmarkFailurePolicy policy)
        => policy is BenchmarkFailurePolicy.Stop or BenchmarkFailurePolicy.RetryOnceThenStop;
    private static bool IsTerminal(JobStatus status)
        => status is JobStatus.Completed or JobStatus.Cancelled or JobStatus.Failed or JobStatus.Interrupted;
    private static string Serialize(BenchmarkJobPayload payload) => JsonSerializer.Serialize(payload, JsonOptions);
    private static BenchmarkJobPayload Deserialize(string json)
        => JsonSerializer.Deserialize<BenchmarkJobPayload>(json, JsonOptions) ?? throw new InvalidOperationException("The benchmark job payload is empty.");
}
