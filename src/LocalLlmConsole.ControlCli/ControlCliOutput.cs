using System.Text.Json;

namespace LocalLlmConsole.ControlCli;

internal static class ControlCliOutput
{
    public static void WriteResponse(string text, bool compact)
    {
        try
        {
            using var json = JsonDocument.Parse(text);
            Console.WriteLine(JsonSerializer.Serialize(json.RootElement, new JsonSerializerOptions { WriteIndented = !compact }));
        }
        catch
        {
            Console.WriteLine(text);
        }
    }
}
