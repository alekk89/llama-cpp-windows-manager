namespace LocalLlmConsole.Services;

public static class SpeculativeTypePolicy
{
    public const string AtomicMtp = "atomic-mtp";
    public static readonly IReadOnlyList<string> SupportedTypes =
    [
        "none", AtomicMtp, "draft-mtp", "draft-simple", "draft-eagle3", "draft-dflash", "draft-dspark",
        "ngram-simple", "ngram-map-k", "ngram-map-k4v", "ngram-mod", "ngram-cache"
    ];

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
