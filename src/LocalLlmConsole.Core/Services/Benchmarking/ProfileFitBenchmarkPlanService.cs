using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public static class ProfileFitBenchmarkPlanService
{
    public static BenchmarkPlan Create(
        ModelRecord model,
        string originalProfileId,
        NamedModelLaunchProfile fitted,
        bool stopActiveSessions,
        bool preventSystemSleep)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(fitted);
        if (string.IsNullOrWhiteSpace(originalProfileId))
            throw new InvalidOperationException("The original profile is unavailable for comparison.");
        var maxWorkload = Math.Max(256, Math.Min(fitted.Settings.ContextSize, 4096));
        return new BenchmarkPlan
        {
            Name = $"Original vs fitted — {model.Name}",
            ExecutionMode = BenchmarkExecutionMode.ProfileServing,
            ModelIds = [model.Id],
            ProfileIds = [originalProfileId, fitted.Id],
            ScopeSelections =
            [
                new BenchmarkScopeSelection(model.Id, originalProfileId),
                new BenchmarkScopeSelection(model.Id, fitted.Id)
            ],
            UseProfileRuntime = true,
            PromptSizes = [],
            GenerationSizes = [],
            PromptGenerationPairs = [new BenchmarkPromptGenerationPair(Math.Max(128, maxWorkload - 128), 128)],
            Repetitions = 3,
            Warmup = true,
            FailurePolicy = BenchmarkFailurePolicy.Continue,
            StopActiveSessions = stopActiveSessions,
            PreventSystemSleep = preventSystemSleep,
            Serving = new BenchmarkServingOptions { Concurrencies = [1], RequireSpeculativeMetrics = false }
        };
    }
}
