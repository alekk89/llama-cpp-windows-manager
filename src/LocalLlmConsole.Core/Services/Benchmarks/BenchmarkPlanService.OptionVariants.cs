using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkPlanService
{
    private static IReadOnlyList<BenchmarkEffectiveOptions> EffectiveOptionVariants(
        BenchmarkPlan plan,
        ModelLaunchSettings profile)
    {
        IReadOnlyList<BenchmarkEffectiveOptions> variants = plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing
                                                            || plan.Options.CacheTypesKv.Count == 0
            ? [EffectiveOptions(plan.Options, profile)]
            : plan.Options.CacheTypesKv
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(value => EffectiveOptions(plan.Options with
                {
                    CacheTypesKv = [],
                    CacheTypesK = [value],
                    CacheTypesV = [value]
                }, profile))
                .ToArray();
        if (plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing
            || plan.Options.GpuConfigurations.Count == 0)
            return variants;
        return variants
            .SelectMany(options => plan.Options.GpuConfigurations.Select(configuration =>
            {
                var mode = ServingGpuMode(configuration.Mode);
                return options with
                {
                    SplitModes = [mode.Equals("single", StringComparison.OrdinalIgnoreCase) ? "none" : mode],
                    TensorSplits = mode.Equals("single", StringComparison.OrdinalIgnoreCase)
                        || string.IsNullOrWhiteSpace(configuration.Split)
                            ? []
                            : [NormalizeGpuSplit(configuration.Split)]
                };
            }))
            .ToArray();
    }
}
