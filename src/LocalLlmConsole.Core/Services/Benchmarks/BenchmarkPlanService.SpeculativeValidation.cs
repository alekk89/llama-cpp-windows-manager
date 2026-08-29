using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkPlanService
{
    private static void ValidateSpeculativeConfigurations(
        IReadOnlyList<BenchmarkSpeculativeConfiguration> configurations,
        ICollection<string> errors)
    {
        foreach (var configuration in configurations)
        {
            var type = SpeculativeTypePolicy.Normalize(configuration.Type);
            if (!SpeculativeTypePolicy.SupportedTypes.Contains(type, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Speculative type '{configuration.Type}' is not supported.");

            var head = NormalizeSpeculativeHead(configuration.Head);
            if (head is not ("profile" or "auto"))
                errors.Add($"Speculative head '{configuration.Head}' must be profile or auto.");
        }
    }

    private static string SpeculativeConfigurationKey(BenchmarkSpeculativeConfiguration configuration)
        => $"{SpeculativeTypePolicy.Normalize(configuration.Type)}|{NormalizeSpeculativeHead(configuration.Head)}";

    private static string NormalizeSpeculativeHead(string value)
        => string.IsNullOrWhiteSpace(value) ? "profile" : value.Trim().ToLowerInvariant();
}
