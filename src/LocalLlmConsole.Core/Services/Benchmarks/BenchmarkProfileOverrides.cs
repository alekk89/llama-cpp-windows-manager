using System.Globalization;
using System.Reflection;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

/// <summary>Temporary, typed profile edits owned by a benchmark plan, never saved to the profile.</summary>
public static class BenchmarkProfileOverrides
{
    private static readonly HashSet<string> Owned = new(StringComparer.OrdinalIgnoreCase)
        { "RuntimeId", "Port", "Host", "Seed", "Temperature", "MaxTokens", "EnableMetrics" };

    public static IReadOnlyList<PropertyInfo> Properties { get; } = typeof(ModelLaunchSettings)
        .GetProperties().Where(property => property.CanWrite && !Owned.Contains(property.Name)).ToArray();

    public static ModelLaunchSettings Apply(ModelLaunchSettings source, IReadOnlyDictionary<string, string> overrides)
    {
        var result = source with { };
        foreach (var (name, text) in overrides)
        {
            var property = Properties.FirstOrDefault(item => item.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException($"'{name}' is not a benchmark profile override.");
            object value;
            try { value = Convert.ChangeType(text, property.PropertyType, CultureInfo.InvariantCulture); }
            catch (Exception error) when (error is FormatException or OverflowException or InvalidCastException)
            { throw new ArgumentException($"'{text}' is not a valid {name} value.", error); }
            if (value is double number && !double.IsFinite(number))
                throw new ArgumentException($"{name} must be finite.");
            property.SetValue(result, value);
        }
        return result;
    }
}
