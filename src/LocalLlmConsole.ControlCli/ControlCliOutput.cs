using System.Text.Json;

namespace LocalLlmConsole.ControlCli;

internal static class ControlCliOutput
{
    public static void WriteResponse(string text, bool compact)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            var typeInfo = compact
                ? ControlCliJsonContext.Default.JsonElement
                : ControlCliJsonContext.Indented.JsonElement;
            Console.WriteLine(JsonSerializer.Serialize(json.RootElement, typeInfo));
        }
        catch
        {
            Console.WriteLine(text);
        }
    }
}
