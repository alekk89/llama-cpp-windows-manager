using System.Text.Json.Nodes;

namespace LocalLlmConsole.ControlCli;

/// <summary>
/// The Manager binds request bodies case-insensitively, so safety checks match
/// field names the same way. Flags that can stop a session are set when any
/// spelling is true; flags that relax a check, such as dryRun, require every
/// spelling to agree.
/// </summary>
internal static class ControlCliJsonFields
{
    public static IEnumerable<JsonNode?> Values(JsonObject? body, string name)
        => body is null
            ? []
            : body.Where(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Value);

    public static bool AnyTrue(JsonObject? body, string name)
        => Values(body, name).Any(IsTrue);

    public static bool AllTrue(JsonObject? body, string name)
    {
        var values = Values(body, name).ToArray();
        return values.Length > 0 && values.All(IsTrue);
    }

    public static bool AnyNestedTrue(JsonObject? body, string parent, string name)
        => Values(body, parent).OfType<JsonObject>().Any(child => AnyTrue(child, name));

    public static IEnumerable<string> Strings(JsonObject? body, string name)
        => Values(body, name).Where(value => value is not null).Select(value => value!.ToString());

    /// <summary>Operations read the exact "dryRun" field; benchmarks bind it case-insensitively.</summary>
    public static bool DryRun(JsonObject body)
        => AllTrue(body, "dryRun")
            && body.TryGetPropertyValue("dryRun", out var exact)
            && IsTrue(exact);

    private static bool IsTrue(JsonNode? value)
        => value is JsonValue json && json.TryGetValue<bool>(out var flag) && flag;
}
