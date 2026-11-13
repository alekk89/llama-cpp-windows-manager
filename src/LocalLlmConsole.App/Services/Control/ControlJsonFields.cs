namespace LocalLlmConsole.Services;

/// <summary>
/// Case-insensitive request-body lookups for safety checks. Control endpoints
/// bind bodies case-insensitively, so a flag that can stop a session is set
/// when any spelling is true, while a flag that relaxes a check requires every
/// spelling to agree.
/// </summary>
internal static class ControlJsonFields
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
    public static bool DryRun(JsonObject? body)
        => AllTrue(body, "dryRun")
            && body!.TryGetPropertyValue("dryRun", out var exact)
            && IsTrue(exact);

    public static bool HasDotSegment(IEnumerable<string> decodedSegments)
        => decodedSegments.Any(segment => segment is "." or ".." || segment.Contains('\\'));

    private static bool IsTrue(JsonNode? value)
        => value is JsonValue json && json.TryGetValue<bool>(out var flag) && flag;
}
