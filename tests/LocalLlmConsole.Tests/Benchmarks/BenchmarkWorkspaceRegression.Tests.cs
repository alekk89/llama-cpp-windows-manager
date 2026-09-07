using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class BenchmarkWorkspaceRegressionTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;
    private static readonly ModelRecord Model = new("m", "Model", "m.gguf", OwnershipKind.External, "{}", Now);
    private static readonly RuntimeRecord Runtime = new("r", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, "llama-server.exe", "{}", Now);
    private static readonly ModelLaunchSettings Settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(""), "r");
    private static readonly NamedModelLaunchProfile Profile = new("p", "m", "Profile", Settings, Now, true);
    private static BenchmarkPlan Plan => new() { AllModels = true, AllProfiles = true, PromptSizes = [512], GenerationSizes = [], Repetitions = 1 };
    private static BenchmarkPlanPreview Preview(BenchmarkPlan plan) => new BenchmarkPlanService().Preview(plan, [Model], [Profile], [Runtime]);

    [Fact]
    public void MatrixRowsAndDifferentRuntimesNeverCollapseIntoOneAverage()
    {
        var a = Parse(4, 100);
        var b = Parse(8, 200);
        Assert.NotEqual(a.WorkloadSignature, b.WorkloadSignature);
        var bars = BenchmarkSpeedReportService.Build([Store(1, "runtime-a", a), Store(2, "runtime-a", b), Store(3, "runtime-b", a)])
            .SelectMany(section => section.Bars).ToArray();
        Assert.Equal(3, bars.Length);
        Assert.DoesNotContain(bars, bar => bar.TokensPerSecond == 150);
    }

    [Fact]
    public void MatchingRowsCompareAcrossPlansWithDifferentRepetitionCounts()
    {
        var first = Preview(Plan).WorkItems.Single();
        var second = Preview(Plan with { Repetitions = 7 }).WorkItems.Single();
        Assert.NotEqual(first.EffectiveCommandSignature, second.EffectiveCommandSignature);
        Assert.Equal(BenchmarkCommandBuilder.ResultSignature(first.Options), BenchmarkCommandBuilder.ResultSignature(second.Options));
        var comparison = Assert.Single(BenchmarkComparisonService.Compare([Store(1, "a", Parse(4, 100))], [Store(2, "b", Parse(4, 120))]));
        Assert.Equal(20, comparison.PercentChange);
    }

    [Fact]
    public void GpuGroupsUseSlashWithinEachConfiguration()
    {
        var plan = Plan with { Options = new() { Devices = ["CUDA0,CUDA1", "CUDA2/CUDA3"], TensorSplits = ["3,2"] } };
        var preview = Preview(plan);
        Assert.True(preview.IsValid, string.Join("; ", preview.Errors));
        var args = BenchmarkCommandBuilder.Build(plan, preview.WorkItems.Single(), "m.gguf").ToList();
        Assert.Equal("CUDA0/CUDA1,CUDA2/CUDA3", args[args.IndexOf("--device") + 1]);
        Assert.Equal("3/2", args[args.IndexOf("--tensor-split") + 1]);
    }

    [Fact]
    public void ServingMatrixLimitRejectsBeforeMaterializingCombinations()
    {
        var plan = Plan with
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            GenerationSizes = [16],
            Options = new() { Threads = Enumerable.Range(1, 40).ToArray(), BatchSizes = Enumerable.Range(512, 40).ToArray() }
        };
        var preview = Preview(plan);
        Assert.False(preview.IsValid);
        Assert.Empty(preview.WorkItems);
        Assert.Contains(preview.Errors, error => error.Contains("maximum", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ProfileOverridesAreTypedTemporaryAndMatrixValuesTakePrecedence()
    {
        var plan = Plan with
        {
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            GenerationSizes = [16],
            Serving = new() { ProfileOverrides = new Dictionary<string, string> { ["Threads"] = "7", ["TopP"] = "0.8" } },
            Options = new() { Threads = [4, 8] }
        };
        var preview = Preview(plan);
        Assert.True(preview.IsValid, string.Join("; ", preview.Errors));
        Assert.Equal(new[] { 4, 8 }, preview.WorkItems.Select(item => item.LaunchSettings!.Threads));
        Assert.All(preview.WorkItems, item => Assert.Equal(.8, item.LaunchSettings!.TopP));
        Assert.Equal(Settings, Profile.Settings);
        Assert.False(Preview(plan with { Serving = new() { ProfileOverrides = new Dictionary<string, string> { ["Host"] = "0.0.0.0" } } }).IsValid);
        Assert.False(Preview(plan with { Serving = new() { ProfileOverrides = new Dictionary<string, string> { ["Threads"] = "abc" } } }).IsValid);
    }

    [Fact]
    public void LazyModeIsCountedForStructuredAndLegacyExpertPlans()
    {
        Assert.Equal(2, Preview(Plan with { Options = new() { LazyModes = ["on", "off"] } }).ExpectedResultRows);
        Assert.Equal(2, Preview(Plan with { Options = new() { AdditionalArguments = ["--lazy-mode", "on,off"] } }).ExpectedResultRows);
        Assert.True(Preview(Plan with { Options = new() { LoadModes = ["auto"] } }).IsValid);
    }

    [Fact]
    public void MissingDecodeTimingsAreNotRelabeledRequestThroughput()
    {
        var result = Parse(4, 100) with { ExecutionMode = BenchmarkExecutionMode.ProfileServing, AveragePromptTokensPerSecond = 900 };
        var sections = BenchmarkSpeedReportService.Build([Store(1, "a", result)]);
        Assert.DoesNotContain(sections, section => section.Kind == BenchmarkSpeedReportKind.Generation);
        Assert.Equal("—", new BenchmarkResultTableRow(Store(1, "a", result)).Decode);
        var baseline = new BenchmarkResultTableRow(Store(1, "a", result));
        Assert.Equal(100.ToString("N2"), baseline.Total);
        var candidate = new BenchmarkResultTableRow(Store(2, "b", result with { AverageGenerationTokensPerSecond = 100 }));
        candidate.CompareTo(baseline);
        Assert.Equal("—", candidate.Change);
    }

    [Fact]
    public void CombinedMicrobenchmarkSpeedIsNotPresentedAsPromptOrGenerationTiming()
    {
        var result = Parse(4, 120) with { Classification = BenchmarkResultClassification.PromptAndGeneration, GenerationTokens = 128 };
        var row = new BenchmarkResultTableRow(Store(1, "a", result));
        Assert.Equal("—", row.Prompt);
        Assert.Equal("—", row.Decode);
        Assert.Equal(120.ToString("N2"), row.Total);
        Assert.Equal("—", new BenchmarkResultTableRow(Store(2, "b", Parse(4, 120))).Total);
    }

    private static BenchmarkParsedResult Parse(int threads, double speed)
    {
        var json = JsonSerializer.Serialize(new { model_filename = "m.gguf", n_prompt = 512, n_gen = 0, n_threads = threads, avg_ts = speed });
        Assert.True(BenchmarkResultService.TryParse(json, "same-model", "same-unreported-options", RuntimeMode.Native, RuntimeBackend.Cpu, out var result, out var error), error);
        return result!;
    }
    private static StoredBenchmarkResult Store(int id, string key, BenchmarkParsedResult result) => new(id, "run", key, 1, id, false, result, Now);
}
