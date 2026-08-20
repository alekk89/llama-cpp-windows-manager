using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.ControlCli;

internal static class ControlCliArgumentValues
{
    public static JsonObject SettingsPatch(Arguments args)
    {
        var patch = new JsonObject();
        foreach (var assignment in args.Values("set"))
        {
            var split = assignment.IndexOf('=');
            if (split <= 0) throw new InvalidOperationException($"Invalid --set '{assignment}'. Use --set name=value.");
            var name = assignment[..split].Trim();
            var value = assignment[(split + 1)..].Trim();
            patch[name] = ParseValue(value);
        }
        if (args.Value("settings-file") is { Length: > 0 } file)
        {
            var fromFile = JsonNode.Parse(File.ReadAllText(file)) as JsonObject
                ?? throw new InvalidOperationException("--settings-file must contain a JSON object.");
            foreach (var (name, value) in fromFile) patch[name] = value?.DeepClone();
        }
        return patch;
    }

    private static JsonNode? ParseValue(string value)
    {
        if (string.Equals(value, "null", StringComparison.OrdinalIgnoreCase)) return null;
        if (bool.TryParse(value, out var boolean)) return JsonValue.Create(boolean);
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer)) return JsonValue.Create(integer);
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)) return JsonValue.Create(number);
        if ((value.StartsWith('"') && value.EndsWith('"')) || value.StartsWith('{') || value.StartsWith('['))
        {
            try { return JsonNode.Parse(value); }
            catch (JsonException) { }
        }
        return JsonValue.Create(value);
    }

    public static string SelfQuery(Arguments args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        Add("sessionId", args.Value("session") ?? Environment.GetEnvironmentVariable("LLWM_SESSION_ID"));
        Add("model", args.Value("model")
            ?? Environment.GetEnvironmentVariable("LLWM_MODEL_ID")
            ?? Environment.GetEnvironmentVariable("OPENCODE_MODEL")
            ?? Environment.GetEnvironmentVariable("OPENAI_MODEL")
            ?? Environment.GetEnvironmentVariable("LLM_MODEL"));
        Add("endpoint", args.Value("endpoint")
            ?? Environment.GetEnvironmentVariable("LLWM_ENDPOINT")
            ?? Environment.GetEnvironmentVariable("OPENAI_BASE_URL")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_BASE"));
        Add("port", args.Value("port"));
        Add("processId", args.Value("process-id"));
        return values.Count == 0 ? "" : "?" + string.Join("&", values.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value.Trim();
        }
    }

}
