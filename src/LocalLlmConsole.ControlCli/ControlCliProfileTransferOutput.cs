using System.Text;
using System.Text.Json;

namespace LocalLlmConsole.ControlCli;

internal static class ControlCliProfileTransferOutput
{
    internal static void SaveExportFile(string response, Arguments args)
    {
        if (args.Positionals.ElementAtOrDefault(0) is not ("profiles" or "profile")
            || args.Positionals.ElementAtOrDefault(1) != "export" || args.Value("file") is not { } path) return;
        using var document = JsonDocument.Parse(response);
        if (!document.RootElement.TryGetProperty("ok", out var ok) || ok.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException("Profile export failed; no file was written.");
        var text = document.RootElement.GetProperty("text").GetString()
            ?? throw new InvalidOperationException("Export response did not contain a profile bundle.");
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }
}
