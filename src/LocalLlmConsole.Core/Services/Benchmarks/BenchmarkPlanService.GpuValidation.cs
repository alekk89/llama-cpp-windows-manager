using System.Globalization;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkPlanService
{
    private static void ValidateBoolean(IReadOnlyList<string> values, string name, ICollection<string> errors)
    {
        if (values.Any(value => value is not ("0" or "1"))) errors.Add($"{name} values must be 0 or 1.");
    }

    private static void ValidateGpuConfigurations(
        IReadOnlyList<BenchmarkGpuConfiguration> configurations,
        ICollection<string> errors)
    {
        foreach (var configuration in configurations)
        {
            var mode = ServingGpuMode(configuration.Mode);
            if (mode is not ("single" or "layer" or "row" or "tensor"))
            {
                errors.Add("GPU configuration mode must be one of: single, layer, row, tensor.");
                continue;
            }
            var split = NormalizeGpuSplit(configuration.Split);
            if (mode == "single" && split.Length > 0)
                errors.Add("Single GPU configurations cannot include a GPU split.");
            var values = split.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (values.Length > 128)
                errors.Add("GPU split cannot contain more than 128 entries.");
            var positive = false;
            foreach (var value in values)
            {
                if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    || !double.IsFinite(number) || number < 0)
                {
                    errors.Add($"GPU split value '{value}' must be a non-negative number.");
                    continue;
                }
                positive |= number > 0;
            }
            if (values.Length > 0 && !positive)
                errors.Add("GPU split must assign a positive proportion to at least one GPU.");
        }
    }

    private static string GpuConfigurationKey(BenchmarkGpuConfiguration configuration)
        => $"{ServingGpuMode(configuration.Mode)}|{NormalizeGpuSplit(configuration.Split)}";

    private static void ValidateGpuSplitDeviceCount(
        string profileName,
        IReadOnlyList<string> devices,
        IReadOnlyList<string> splits,
        ICollection<string> errors)
    {
        foreach (var split in splits.Select(NormalizeGpuSplit).Where(value => value.Length > 0))
        {
            var splitCount = split.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
            foreach (var deviceSelection in devices.Where(value => !string.IsNullOrWhiteSpace(value)))
            {
                var deviceCount = deviceSelection.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Length;
                if (deviceCount != splitCount)
                    errors.Add($"{profileName}: GPU split '{split}' has {splitCount} entries, but the inherited device selection '{deviceSelection}' has {deviceCount} devices.");
            }
        }
    }
}
