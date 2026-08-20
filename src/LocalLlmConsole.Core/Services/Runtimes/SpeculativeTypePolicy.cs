namespace LocalLlmConsole.Services;

public static class SpeculativeTypePolicy
{
    public const string AtomicMtp = "atomic-mtp";

    public static string Normalize(string? value)
    {
        var normalized = (value ?? "")
            .Trim()
            .ToLowerInvariant()
            .Replace('_', '-')
            .Replace(' ', '-');
        return normalized == "mtp" ? AtomicMtp : normalized;
    }

    public static bool IsAtomicMtp(string? value)
        => Normalize(value).Equals(AtomicMtp, StringComparison.OrdinalIgnoreCase);

    public static string LlamaArgument(string? value)
        => IsAtomicMtp(value) ? "mtp" : Normalize(value);
}
