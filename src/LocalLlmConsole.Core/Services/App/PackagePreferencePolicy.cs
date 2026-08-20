namespace LocalLlmConsole.Services;

public static class PackagePreferencePolicy
{
    public static string NormalizeCuda(string? text)
    {
        var value = (text ?? "").Trim()
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();

        return value is "compatibility" or "compatible" or "cuda12" or "cuda12compatibility"
            ? "compatibility"
            : "latest";
    }
}
