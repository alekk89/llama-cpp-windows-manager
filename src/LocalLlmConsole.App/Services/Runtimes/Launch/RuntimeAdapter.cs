namespace LocalLlmConsole.Services;

/// <summary>
/// Transitional compatibility surface for callers compiled against the former
/// combined launch adapter. New code should use the focused validator and
/// argument builder directly.
/// </summary>
[Obsolete("Use LlamaCppLaunchValidator and LlamaCppArgumentBuilder directly.")]
public static class RuntimeAdapter
{
    public static ValidationResult Validate(RuntimeLaunchRequest request)
        => LlamaCppLaunchValidator.Validate(request);

    public static IReadOnlyList<string> BuildArgs(RuntimeLaunchRequest request)
        => LlamaCppArgumentBuilder.Build(request);
}
