using System.Globalization;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.ControlCli;

internal static partial class ControlCliRequestFactory
{
    private static string ModelArg(Arguments args, int positionalIndex)
        => Required(args, "model", args.Positionals.ElementAtOrDefault(positionalIndex));

    private static string Identifier(Arguments args, int positionalIndex)
        => args.Positionals.ElementAtOrDefault(positionalIndex)
            ?? throw new InvalidOperationException("A resource identifier is required.");

    private static string? SettingArg(Arguments args, string name)
    {
        foreach (var assignment in args.Values("set").Reverse())
        {
            var split = assignment.IndexOf('=');
            if (split > 0 && assignment[..split].Trim().Equals(name, StringComparison.OrdinalIgnoreCase))
                return assignment[(split + 1)..].Trim();
        }
        return null;
    }

    private static string Required(Arguments args, string name, string? fallback = null)
        => args.Value(name) is { Length: > 0 } value ? value
            : !string.IsNullOrWhiteSpace(fallback) ? fallback
            : throw new InvalidOperationException($"--{name} is required.");

    private static int IntValue(Arguments args, string name, int fallback)
        => int.TryParse(args.Value(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string Segment(string value) => Uri.EscapeDataString(value);
    private static ControlRequest Get(string path) => new("GET", path, null);
    private static ControlRequest Post(string path, JsonObject? body = null) => new("POST", path, body);
    private static ControlRequest Put(string path, JsonObject? body = null) => new("PUT", path, body);
    private static ControlRequest Patch(string path, JsonObject? body = null) => new("PATCH", path, body);
    private static ControlRequest Delete(string path) => new("DELETE", path, null);

}

internal sealed record ControlRequest(string Method, string Path, JsonObject? Body);
