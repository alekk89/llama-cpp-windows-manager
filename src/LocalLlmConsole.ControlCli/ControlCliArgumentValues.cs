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

    public static string SelfQuery(Arguments args) => SelfQuery(args, Environment.GetEnvironmentVariable);

    internal static string SelfQuery(Arguments args, Func<string, string?> env)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var agent = CallingAgent(env);
        Add("sessionId", args.Value("session") ?? env("LLWM_SESSION_ID"));
        Add("model", args.Value("model")
            ?? env("LLWM_MODEL_ID")
            ?? (agent is null
                ? env("OPENCODE_MODEL") ?? env("OPENAI_MODEL") ?? env("LLM_MODEL")
                : agent.Value.Model));
        Add("endpoint", args.Value("endpoint")
            ?? env("LLWM_ENDPOINT")
            ?? agent?.Endpoint
            ?? env("OPENAI_BASE_URL")
            ?? env("OPENAI_API_BASE"));
        Add("port", args.Value("port"));
        Add("processId", args.Value("process-id"));
        return values.Count == 0 ? "" : "?" + string.Join("&", values.Select(pair => $"{pair.Key}={Uri.EscapeDataString(pair.Value)}"));

        void Add(string key, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) values[key] = value.Trim();
        }
    }

    // An agent that marks the commands it runs reports its own model endpoint, so a generic
    // variable set for other tools (for example OPENAI_BASE_URL pointing at a local model) is not
    // mistaken for the endpoint the agent itself uses. The Manager then decides whether it serves it.
    private static (string Endpoint, string? Model)? CallingAgent(Func<string, string?> env)
    {
        if (env("CLAUDECODE") != "1") return null;
        string? Set(string name) => string.IsNullOrWhiteSpace(env(name)) ? null : env(name);
        var endpoint = env("CLAUDE_CODE_USE_BEDROCK") == "1"
            ? Set("ANTHROPIC_BEDROCK_BASE_URL") ?? "https://bedrock-runtime.amazonaws.com"
            : env("CLAUDE_CODE_USE_VERTEX") == "1"
            ? Set("ANTHROPIC_VERTEX_BASE_URL") ?? "https://aiplatform.googleapis.com"
            : Set("ANTHROPIC_BASE_URL") ?? "https://api.anthropic.com";
        return (endpoint, Set("ANTHROPIC_MODEL"));
    }

}
