using System.Globalization;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed record BenchmarkWorkloadPreset(
    string Name,
    string Description,
    IReadOnlyList<BenchmarkPromptGenerationPair> PromptGenerationPairs,
    int ContextSize,
    int Repetitions,
    int ReadyTimeoutSeconds,
    int RequestTimeoutSeconds)
{
    public string PromptGenerationPairText => string.Join(", ", PromptGenerationPairs.Select(pair =>
        $"{pair.PromptTokens.ToString(CultureInfo.InvariantCulture)}/{pair.GenerationTokens.ToString(CultureInfo.InvariantCulture)}"));
}

public static class BenchmarkWorkloadPresetCatalog
{
    public static IReadOnlyList<BenchmarkWorkloadPreset> All { get; } =
    [
        new(
            "Short",
            "Short-context interactive workloads up to 4K prompt tokens.",
            [new(512, 128), new(2048, 256), new(4096, 256)],
            8192,
            5,
            600,
            600),
        new(
            "Medium",
            "Medium-context workloads from 8K through 32K prompt tokens.",
            [new(8192, 512), new(16384, 512), new(32768, 1024)],
            65536,
            5,
            600,
            1800),
        new(
            "Long",
            "Long-context workloads from 32K through 128K prompt tokens.",
            [new(32768, 1024), new(65536, 1024), new(131072, 1024)],
            262144,
            5,
            1200,
            3600)
    ];

    public static BenchmarkWorkloadPreset Get(string name)
        => All.FirstOrDefault(preset => preset.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
           ?? throw new KeyNotFoundException($"Benchmark workload preset '{name}' was not found.");
}
