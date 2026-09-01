using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed record BenchmarkSpeedReportBar(
    string Label,
    double TokensPerSecond,
    string ConfigurationLabel = "");

public enum BenchmarkSpeedReportKind
{
    PromptProcessing,
    Generation,
    Combined
}

public sealed record BenchmarkSpeedReportSection(
    BenchmarkSpeedReportKind Kind,
    IReadOnlyList<BenchmarkSpeedReportBar> Bars,
    int TotalBars);

public static class BenchmarkSpeedReportService
{
    public const int MaximumBarsPerSection = 100;

    public static IReadOnlyList<BenchmarkSpeedReportSection> Build(IReadOnlyList<StoredBenchmarkResult> results)
    {
        var tests = results
            .Where(row => !row.IsPartialAttempt)
            .GroupBy(row => row.Result.WorkloadSignature, StringComparer.OrdinalIgnoreCase)
            .Select(group => Average(group.Select(row => row.Result).ToArray()))
            .OrderBy(test => test.Sample.PromptTokens)
            .ThenBy(test => test.Sample.GenerationTokens)
            .ThenBy(test => test.Sample.ContextSize)
            .ThenBy(test => test.Sample.BatchSize)
            .ThenBy(test => test.Sample.Concurrency)
            .ToArray();

        var sections = new List<BenchmarkSpeedReportSection>();
        AddSection(
            sections,
            BenchmarkSpeedReportKind.PromptProcessing,
            tests.Select(test => PromptBar(test)).Where(bar => bar is not null).Cast<BenchmarkSpeedReportBar>());
        AddSection(
            sections,
            BenchmarkSpeedReportKind.Generation,
            tests.Select(test => GenerationBar(test)).Where(bar => bar is not null).Cast<BenchmarkSpeedReportBar>());
        AddSection(
            sections,
            BenchmarkSpeedReportKind.Combined,
            tests.Where(test => test.Sample.ExecutionMode == BenchmarkExecutionMode.LlamaBench
                && test.Sample.Classification == BenchmarkResultClassification.PromptAndGeneration
                                && test.AverageTokensPerSecond > 0)
                .Select(test => Bar(test.Sample, test.AverageTokensPerSecond)));
        return sections;
    }

    private static void AddSection(
        ICollection<BenchmarkSpeedReportSection> sections,
        BenchmarkSpeedReportKind kind,
        IEnumerable<BenchmarkSpeedReportBar> source)
    {
        var bars = source.ToArray();
        if (bars.Length == 0) return;
        sections.Add(new BenchmarkSpeedReportSection(
            kind,
            bars.Take(MaximumBarsPerSection).ToArray(),
            bars.Length));
    }

    private static BenchmarkSpeedReportBar? PromptBar(AverageTest test)
    {
        var rate = test.Sample.ExecutionMode == BenchmarkExecutionMode.ProfileServing
            ? test.AveragePromptTokensPerSecond
            : test.Sample.Classification == BenchmarkResultClassification.PromptProcessing
                ? test.AverageTokensPerSecond
                : 0;
        return rate > 0 ? Bar(test.Sample, rate) : null;
    }

    private static BenchmarkSpeedReportBar? GenerationBar(AverageTest test)
    {
        var isGeneration = test.Sample.ExecutionMode == BenchmarkExecutionMode.ProfileServing
                           || test.Sample.Classification == BenchmarkResultClassification.TokenGeneration;
        return isGeneration && test.AverageTokensPerSecond > 0
            ? Bar(test.Sample, test.AverageTokensPerSecond)
            : null;
    }

    private static BenchmarkSpeedReportBar Bar(BenchmarkParsedResult result, double tokensPerSecond)
        => new(Label(result), tokensPerSecond, ConfigurationLabel(result));

    private static string ConfigurationLabel(BenchmarkParsedResult result)
    {
        if (result.ExecutionMode != BenchmarkExecutionMode.ProfileServing) return "";
        return (result.SpeculativeType ?? "").Trim().ToLowerInvariant() switch
        {
            "draft-dflash" => "DFlash2",
            "draft-mtp" => "MTP",
            "atomic-mtp" => "Atomic MTP",
            "draft-eagle3" => "Eagle 3",
            "draft-dspark" => "DSpark",
            "draft-simple" => "Draft model",
            "ngram-simple" => "N-gram simple",
            "ngram-map-k" => "N-gram map-k",
            "ngram-map-k4v" => "N-gram map-k4v",
            "ngram-mod" => "N-gram modified",
            "ngram-cache" => "N-gram cache",
            "" or "none" => "No speculative decoding",
            var value => value
        };
    }

    private static AverageTest Average(IReadOnlyList<BenchmarkParsedResult> rows)
    {
        var promptRates = rows.Select(row => row.AveragePromptTokensPerSecond).Where(rate => rate > 0).ToArray();
        return new AverageTest(
            rows[0],
            rows.Average(row => row.AverageTokensPerSecond),
            promptRates.Length == 0 ? 0 : promptRates.Average());
    }

    private static string Label(BenchmarkParsedResult result)
    {
        if (result.ExecutionMode == BenchmarkExecutionMode.ProfileServing)
        {
            var profile = string.IsNullOrWhiteSpace(result.ProfileName) ? "Saved profile" : result.ProfileName;
            var memory = result.ObservedGpuMemoryUsedMiB > 0 ? $" · VRAM {result.ObservedGpuMemoryUsedMiB:N0} MiB" : "";
            return $"{profile} · {result.PromptTokens}/{result.GenerationTokens} tokens · ctx {result.ContextSize} · batch {result.BatchSize} · concurrency {result.Concurrency}{memory}";
        }

        var parts = new List<string>
        {
            $"{result.PromptTokens}/{result.GenerationTokens} tokens",
            $"batch {result.BatchSize}",
            $"micro {result.MicroBatchSize}",
            $"threads {result.Threads}",
            $"GPU layers {result.GpuLayers}"
        };
        if (result.Depth > 0) parts.Add($"depth {result.Depth}");
        if (!string.IsNullOrWhiteSpace(result.SplitMode))
            parts.Add(string.IsNullOrWhiteSpace(result.TensorSplit)
                ? result.SplitMode
                : $"{result.SplitMode} {result.TensorSplit}");
        if (!string.IsNullOrWhiteSpace(result.FlashAttention)) parts.Add($"flash {result.FlashAttention}");
        if (!string.IsNullOrWhiteSpace(result.CacheTypeK)) parts.Add($"cache {result.CacheTypeK}");
        return string.Join(" · ", parts);
    }

    private sealed record AverageTest(
        BenchmarkParsedResult Sample,
        double AverageTokensPerSecond,
        double AveragePromptTokensPerSecond);
}
