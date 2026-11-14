using System.Text.RegularExpressions;

namespace LocalLlmConsole.Services;

/// <summary>
/// A managed launch setting whose value the Manager passes verbatim to one runtime flag.
/// </summary>
/// <param name="Id">The launch-setting property name, such as <c>CacheTypeK</c>.</param>
/// <param name="Flag">The runtime flag that receives the value.</param>
/// <param name="AppValues">Manager-only values (for example <c>auto</c>, meaning "omit the flag") that are always offered.</param>
/// <param name="Fallback">Values offered when the selected runtime does not advertise its own list.</param>
public sealed record RuntimeDefinedChoiceSetting(
    string Id,
    string Flag,
    IReadOnlyList<string> AppValues,
    IReadOnlyList<string> Fallback);

/// <summary>
/// The valid values of these settings belong to the runtime, not the Manager: forks and newer
/// llama.cpp builds add cache types and formats. The Manager therefore only checks that a value
/// is a well-formed token, offers the values the selected runtime advertises in its help, and
/// leaves semantic validation to the runtime itself.
/// </summary>
public static partial class RuntimeDefinedChoicePolicy
{
    public static readonly IReadOnlyList<string> CacheTypes = ["f16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1", "f32", "bf16"];
    public static readonly IReadOnlyList<string> ReasoningFormats = ["none", "deepseek", "deepseek-legacy"];
    public static readonly IReadOnlyList<string> RopeScalings = ["none", "linear", "yarn"];

    public static readonly RuntimeDefinedChoiceSetting CacheTypeK = new("CacheTypeK", "--cache-type-k", [], CacheTypes);
    public static readonly RuntimeDefinedChoiceSetting CacheTypeV = new("CacheTypeV", "--cache-type-v", [], CacheTypes);
    public static readonly RuntimeDefinedChoiceSetting SpecDraftCacheTypeK = new("SpecDraftCacheTypeK", "--cache-type-k-draft", [], CacheTypes);
    public static readonly RuntimeDefinedChoiceSetting SpecDraftCacheTypeV = new("SpecDraftCacheTypeV", "--cache-type-v-draft", [], CacheTypes);
    public static readonly RuntimeDefinedChoiceSetting ReasoningFormat = new("ReasoningFormat", "--reasoning-format", ["auto"], ReasoningFormats);
    public static readonly RuntimeDefinedChoiceSetting RopeScaling = new("RopeScaling", "--rope-scaling", ["auto"], RopeScalings);

    public static IReadOnlyList<RuntimeDefinedChoiceSetting> Settings { get; } =
        [CacheTypeK, CacheTypeV, SpecDraftCacheTypeK, SpecDraftCacheTypeV, ReasoningFormat, RopeScaling];

    public static RuntimeDefinedChoiceSetting? Find(string id)
        => Settings.FirstOrDefault(setting => string.Equals(setting.Id, id, StringComparison.Ordinal));

    /// <summary>The values offered before any runtime is known: app values, then the fallback list.</summary>
    public static IReadOnlyList<string> DefaultChoices(RuntimeDefinedChoiceSetting setting)
        => Merge(setting.AppValues, setting.Fallback);

    /// <summary>
    /// A runtime value is a single short token. Values reach the runtime as one argv entry, so this
    /// is defense in depth against flags, whitespace, and shell metacharacters, not a list check.
    /// </summary>
    public static bool IsWellFormed(string? value)
        => !string.IsNullOrEmpty(value) && WellFormedValue().IsMatch(value);

    public static string InvalidValueMessage(string label, RuntimeDefinedChoiceSetting setting)
        => $"{label} must be a runtime value such as {string.Join(", ", DefaultChoices(setting).Take(4))} " +
           "(letters, digits, '.', '_', '+', or '-'; at most 64 characters).";

    /// <summary>
    /// The values the runtime advertises for <paramref name="setting"/>, or null when its help
    /// does not enumerate them.
    /// </summary>
    public static IReadOnlyList<string>? AdvertisedChoices(
        RuntimeDefinedChoiceSetting setting,
        IReadOnlyList<RuntimeLaunchOptionDefinition>? runtimeOptions)
    {
        var option = runtimeOptions?.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, setting.Flag, StringComparison.OrdinalIgnoreCase)
            || candidate.Aliases.Contains(setting.Flag, StringComparer.OrdinalIgnoreCase));
        var advertised = option?.Choices.Where(IsWellFormed).ToArray() ?? [];
        return advertised.Length >= 2 ? advertised : null;
    }

    /// <summary>
    /// The values to offer for <paramref name="setting"/>: app values, then the runtime's advertised
    /// list (or the fallback), then <paramref name="current"/> when it is not already listed, so a
    /// saved value is never silently replaced.
    /// </summary>
    public static IReadOnlyList<string> Choices(
        RuntimeDefinedChoiceSetting setting,
        IReadOnlyList<RuntimeLaunchOptionDefinition>? runtimeOptions,
        string? current)
    {
        var values = Merge(setting.AppValues, AdvertisedChoices(setting, runtimeOptions) ?? setting.Fallback);
        var trimmed = (current ?? "").Trim();
        return IsWellFormed(trimmed) && !values.Contains(trimmed, StringComparer.OrdinalIgnoreCase)
            ? [.. values, trimmed]
            : values;
    }

    private static IReadOnlyList<string> Merge(IEnumerable<string> first, IEnumerable<string> second)
        => first.Concat(second).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9_.+-]{0,63}$")]
    private static partial Regex WellFormedValue();
}
