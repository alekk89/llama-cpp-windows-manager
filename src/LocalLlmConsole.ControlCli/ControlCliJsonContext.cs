using System.Text.Json;
using System.Text.Json.Serialization;

namespace LocalLlmConsole.ControlCli;

internal sealed record ControlCliError(bool Ok, string Error);

[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(DiscoveryDocument))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(ControlCliError))]
internal sealed partial class ControlCliJsonContext : JsonSerializerContext
{
    public static ControlCliJsonContext Indented { get; } = new(new JsonSerializerOptions
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    });
}
