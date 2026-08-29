namespace LocalLlmConsole.Services;

public sealed record BenchmarkComparisonRow(
    string WorkloadSignature,
    BenchmarkResultClassification Classification,
    int PromptTokens,
    int GenerationTokens,
    int ContextSize,
    int BatchSize,
    int Depth,
    double BaselineTokensPerSecond,
    double CandidateTokensPerSecond,
    double PercentChange,
    bool EnvironmentMatches);

public static class BenchmarkComparisonService
{
    public static IReadOnlyList<BenchmarkComparisonRow> Compare(
        IReadOnlyList<StoredBenchmarkResult> baseline,
        IReadOnlyList<StoredBenchmarkResult> candidate,
        bool includePartialAttempts = false)
    {
        var baselineRows = ComparableAverages(baseline, includePartialAttempts);
        var candidateRows = ComparableAverages(candidate, includePartialAttempts);
        return baselineRows.Keys.Intersect(candidateRows.Keys, StringComparer.OrdinalIgnoreCase)
            .Select(signature =>
            {
                var first = baselineRows[signature];
                var second = candidateRows[signature];
                var percent = first.TokensPerSecond > 0
                    ? ((second.TokensPerSecond - first.TokensPerSecond) / first.TokensPerSecond) * 100
                    : 0;
                return new BenchmarkComparisonRow(
                    signature,
                    first.Sample.Classification,
                    first.Sample.PromptTokens,
                    first.Sample.GenerationTokens,
                    first.Sample.ContextSize,
                    first.Sample.BatchSize,
                    first.Sample.Depth,
                    first.TokensPerSecond,
                    second.TokensPerSecond,
                    percent,
                    first.Sample.EnvironmentSignature.Equals(second.Sample.EnvironmentSignature, StringComparison.OrdinalIgnoreCase));
            })
            .OrderBy(row => row.Classification)
            .ThenBy(row => row.PromptTokens)
            .ThenBy(row => row.GenerationTokens)
            .ThenBy(row => row.ContextSize)
            .ThenBy(row => row.BatchSize)
            .ThenBy(row => row.Depth)
            .ToArray();
    }

    private static IReadOnlyDictionary<string, AverageRow> ComparableAverages(
        IReadOnlyList<StoredBenchmarkResult> rows,
        bool includePartialAttempts)
        => rows.Where(row => includePartialAttempts || !row.IsPartialAttempt)
            .GroupBy(row => row.Result.WorkloadSignature, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => new AverageRow(group.First().Result, group.Average(row => row.Result.AverageTokensPerSecond)),
                StringComparer.OrdinalIgnoreCase);

    private sealed record AverageRow(BenchmarkParsedResult Sample, double TokensPerSecond);
}
