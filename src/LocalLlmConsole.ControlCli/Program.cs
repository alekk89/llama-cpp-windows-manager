using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.ControlCli;

internal static class Program
{
    private const string ProtectedPrefix = "dpapi:v1:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("LocalLlmConsole:model-api-key:v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var command = new Arguments(args);
            if (command.Positionals.Count == 0 || command.Has("help") || command.Positionals[0] is "help" or "--help" or "-h")
            {
                Console.WriteLine(HelpText);
                return 0;
            }

            var connection = Discover(command);
            using var http = new HttpClient { BaseAddress = new Uri(connection.BaseUrl.TrimEnd('/') + "/"), Timeout = TimeSpan.FromMinutes(65) };
            http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", Unprotect(connection.ProtectedToken));

            var request = BuildRequest(command);
            await EnforceSelfSafetyAsync(http, command, request);
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Path.TrimStart('/'));
            if (request.Body is not null)
                message.Content = new StringContent(request.Body.ToJsonString(), Encoding.UTF8, "application/json");
            using var response = await http.SendAsync(message);
            var text = await response.Content.ReadAsStringAsync();
            WriteResponse(text, command.Has("compact"));
            return response.IsSuccessStatusCode ? 0 : Math.Clamp((int)response.StatusCode, 1, 255);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(JsonSerializer.Serialize(new { ok = false, error = ex.Message }));
            return 1;
        }
    }

    private static ControlRequest BuildRequest(Arguments args)
    {
        var group = args.Positionals[0].ToLowerInvariant();
        var action = args.Positionals.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "list";
        return group switch
        {
            "status" => Get("/api/v1/status"),
            "capabilities" => Get("/api/v1/capabilities"),
            "self" or "whoami" => Get("/api/v1/self" + SelfQuery(args)),
            "models" or "model" => ModelRequest(action, args),
            "profiles" or "profile" => ProfileRequest(action, args),
            "load" => LoadRequest("load", args),
            "restart" => LoadRequest("restart", args),
            "unload" => Post($"/api/v1/models/{Segment(ModelArg(args, 1))}/unload"),
            "sessions" or "session" => SessionRequest(action, args),
            "metrics" => Get("/api/v1/metrics"),
            "logs" or "log" => LogRequest(action, args),
            "settings" or "setting" => SettingsRequest(action, args),
            "runtimes" or "runtime" => RuntimeRequest(action, args),
            "hf" or "huggingface" => HuggingFaceRequest(action, args),
            "jobs" or "job" => JobRequest(action, args),
            "operations" or "operation" or "ops" => OperationRequest(action, args),
            "request" => RawRequest(args),
            _ => throw new InvalidOperationException($"Unknown command '{group}'. Run 'llwmctl help'.")
        };
    }

    internal static (string Method, string Path, JsonObject? Body) BuildRequestForTests(params string[] args)
    {
        var request = BuildRequest(new Arguments(args));
        return (request.Method, request.Path, request.Body?.DeepClone().AsObject());
    }

    private static async Task EnforceSelfSafetyAsync(HttpClient http, Arguments args, ControlRequest request)
    {
        if (args.Has("allow-self-stop")) return;
        var group = args.Positionals.ElementAtOrDefault(0)?.ToLowerInvariant() ?? "";
        var action = args.Positionals.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "";
        var requestSegments = RequestSegments(request.Path);
        var rawOperation = group == "request"
            && request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase)
            && requestSegments.Length == 4
            && requestSegments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            && requestSegments[1].Equals("v1", StringComparison.OrdinalIgnoreCase)
            && requestSegments[2].Equals("operations", StringComparison.OrdinalIgnoreCase)
                ? requestSegments[3].ToLowerInvariant()
                : "";
        var operation = rawOperation.Length > 0
            ? rawOperation
            : group is "operations" or "operation" or "ops" && action is "run" or "execute"
            ? args.Positionals.ElementAtOrDefault(2)?.ToLowerInvariant() ?? ""
            : "";
        var operationSetting = new Func<string, string?>(name =>
            request.Body?[name]?.ToString() ?? SettingArg(args, name));
        var operationMayStopSelf = operation is "app.shutdown" or "updates.install" or "runtime.delete"
            || operation == "wsl.setup" && (operationSetting("action")?.StartsWith("Delete", StringComparison.OrdinalIgnoreCase) ?? false);
        if (operationMayStopSelf && (request.Body?["dryRun"]?.GetValue<bool>() ?? args.Has("dry-run"))) return;

        var rawModelAction = group == "request" ? RawModelAction(request, requestSegments) : null;
        var unloadOthers = args.Has("unload-others") || (request.Body?["unloadOthers"]?.GetValue<bool>() ?? false);
        var rawStopsTarget = rawModelAction?.Action is "restart" or "unload" or "delete"
            || rawModelAction?.Action == "load" && (request.Body?["restart"]?.GetValue<bool>() ?? false);
        var namedLoadRestarts = group == "load" && (request.Body?["restart"]?.GetValue<bool>() ?? false);
        var destructive = group is "restart" or "unload"
            || group is "models" or "model" && action is "restart" or "unload" or "delete"
            || namedLoadRestarts
            || rawStopsTarget
            || unloadOthers
            || operationMayStopSelf;
        if (!destructive) return;

        using var selfResponse = await http.GetAsync(("api/v1/self" + SelfQuery(args)).TrimStart('/'));
        if (!selfResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "Could not identify the current agent session before a destructive request. " +
                "Retry after the Manager is responsive, or use --allow-self-stop only when the user explicitly requested that consequence.");
        using var selfJson = JsonDocument.Parse(await selfResponse.Content.ReadAsStringAsync());
        var root = selfJson.RootElement;
        if (!root.TryGetProperty("identified", out var identified) || identified.ValueKind != JsonValueKind.True)
            throw new InvalidOperationException(
                "The current agent session could not be identified unambiguously before a destructive request. " +
                "Supply --session, --model, --endpoint, --port, or --process-id, or use --allow-self-stop only when the user explicitly requested that consequence.");
        var selfSession = root.GetProperty("session");
        var selfModelId = StringProperty(selfSession, "modelId");

        if (operationMayStopSelf)
        {
            var targetsOperationSelf = operation switch
            {
                "app.shutdown" or "updates.install" => true,
                "runtime.delete" => StringProperty(selfSession, "runtimeId").Equals(
                    operationSetting("runtime"), StringComparison.OrdinalIgnoreCase),
                "wsl.setup" => StringProperty(selfSession, "mode").Equals("Wsl", StringComparison.OrdinalIgnoreCase),
                _ => false
            };
            if (targetsOperationSelf)
                throw new InvalidOperationException(
                    $"Refusing operation '{operation}' because it can stop the current agent session. " +
                    "Retry with --allow-self-stop only when the user explicitly requested it.");
            return;
        }

        var target = rawModelAction?.Model
            ?? ModelArg(args, group is "models" or "model" ? 2 : 1);
        var targetModelId = await ResolveModelIdAsync(http, target);
        var targetsSelf = selfModelId.Equals(targetModelId, StringComparison.OrdinalIgnoreCase);
        var stopsSelf = group is "restart" or "unload"
            || group is "models" or "model" && action is "restart" or "unload" or "delete"
            || namedLoadRestarts
            || rawStopsTarget
            ? targetsSelf
            : unloadOthers && !targetsSelf;
        if (stopsSelf)
            throw new InvalidOperationException(
                $"Refusing to stop the current agent model '{selfModelId}'. This can terminate the active response. " +
                "Retry with --allow-self-stop only when the user explicitly requested it.");
    }

    private static RawModelStop? RawModelAction(ControlRequest request, string[] segments)
    {
        if (segments.Length < 4
            || !segments[0].Equals("api", StringComparison.OrdinalIgnoreCase)
            || !segments[1].Equals("v1", StringComparison.OrdinalIgnoreCase)
            || !segments[2].Equals("models", StringComparison.OrdinalIgnoreCase))
            return null;

        var model = Uri.UnescapeDataString(segments[3]);
        if (request.Method.Equals("DELETE", StringComparison.OrdinalIgnoreCase) && segments.Length == 4)
            return new RawModelStop(model, "delete");
        if (request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase) && segments.Length == 5)
        {
            var action = segments[4].ToLowerInvariant();
            if (action is "load" or "restart" or "unload")
                return new RawModelStop(model, action);
        }
        return null;
    }

    private static string[] RequestSegments(string path)
    {
        var question = path.IndexOf('?');
        var route = question >= 0 ? path[..question] : path;
        return route.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(Uri.UnescapeDataString)
            .ToArray();
    }

    private static async Task<string> ResolveModelIdAsync(HttpClient http, string identifier)
    {
        using var response = await http.GetAsync($"api/v1/models/{Segment(identifier)}");
        if (!response.IsSuccessStatusCode) return identifier;
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return StringProperty(json.RootElement.GetProperty("model"), "id") is { Length: > 0 } id ? id : identifier;
    }

    private static string StringProperty(JsonElement element, string name)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.ToString();
        }
        return "";
    }

    private static ControlRequest ModelRequest(string action, Arguments args)
        => action switch
        {
            "list" => Get("/api/v1/models"),
            "get" => Get($"/api/v1/models/{Segment(ModelArg(args, 2))}"),
            "scan" => Post("/api/v1/models/scan"),
            "import" => Post("/api/v1/models/import", new JsonObject { ["folder"] = Required(args, "folder") }),
            "companions" or "heads" => Get($"/api/v1/models/{Segment(ModelArg(args, 2))}/companions"),
            "load" => LoadRequest("load", args, 2),
            "restart" => LoadRequest("restart", args, 2),
            "unload" => Post($"/api/v1/models/{Segment(ModelArg(args, 2))}/unload"),
            "delete" => Delete($"/api/v1/models/{Segment(ModelArg(args, 2))}?confirm={args.Has("confirm").ToString().ToLowerInvariant()}"),
            _ => throw new InvalidOperationException($"Unknown model action '{action}'.")
        };

    private static ControlRequest ProfileRequest(string action, Arguments args)
    {
        var model = Segment(ModelArg(args, 2));
        return action switch
        {
            "list" => Get($"/api/v1/models/{model}/profiles"),
            "create" => Post($"/api/v1/models/{model}/profiles", ProfileBody(args, requireName: true)),
            "update" => Put($"/api/v1/models/{model}/profiles/{Segment(Required(args, "id"))}", ProfileBody(args, requireName: false)),
            "delete" => Delete($"/api/v1/models/{model}/profiles/{Segment(Required(args, "id"))}"),
            _ => throw new InvalidOperationException($"Unknown profile action '{action}'.")
        };
    }

    private static JsonObject ProfileBody(Arguments args, bool requireName)
    {
        var body = new JsonObject
        {
            ["name"] = requireName ? Required(args, "name") : args.Value("name") ?? "",
            ["isDefault"] = args.Has("default"),
            ["replace"] = args.Has("replace"),
            ["settings"] = SettingsPatch(args)
        };
        if (args.Value("id") is { Length: > 0 } id) body["id"] = id;
        return body;
    }

    private static ControlRequest LoadRequest(string action, Arguments args, int positionalIndex = 1)
    {
        var model = Segment(ModelArg(args, positionalIndex));
        var saveProfileName = args.Value("save-profile") ?? "";
        if (args.Has("save-profile") && saveProfileName.Equals("true", StringComparison.OrdinalIgnoreCase))
            saveProfileName = "";
        var body = new JsonObject
        {
            ["profileId"] = args.Value("profile-id") ?? "",
            ["profileName"] = args.Value("profile") ?? "",
            ["runtimeId"] = args.Value("runtime") ?? "",
            ["settings"] = SettingsPatch(args),
            ["restart"] = action == "restart" || args.Has("restart"),
            ["unloadOthers"] = args.Has("unload-others"),
            ["waitForReady"] = args.Has("wait"),
            ["timeoutSeconds"] = IntValue(args, "timeout", 600),
            ["saveProfile"] = args.Has("save-profile"),
            ["saveProfileName"] = saveProfileName
        };
        return Post($"/api/v1/models/{model}/{action}", body);
    }

    private static ControlRequest SessionRequest(string action, Arguments args)
        => action switch
        {
            "list" => Get("/api/v1/sessions"),
            "get" => Get($"/api/v1/sessions/{Segment(Identifier(args, 2))}"),
            "logs" => Get($"/api/v1/sessions/{Segment(Identifier(args, 2))}/logs?tail={IntValue(args, "tail", 16000)}"),
            "metrics" => Get($"/api/v1/sessions/{Segment(Identifier(args, 2))}/metrics"),
            _ => throw new InvalidOperationException($"Unknown session action '{action}'.")
        };

    private static ControlRequest LogRequest(string action, Arguments args)
        => action switch
        {
            "list" => Get("/api/v1/logs"),
            "tail" or "get" => Get($"/api/v1/logs/{Segment(Identifier(args, 2))}?tail={IntValue(args, "tail", 80000)}"),
            _ => throw new InvalidOperationException($"Unknown log action '{action}'.")
        };

    private static ControlRequest SettingsRequest(string action, Arguments args)
        => action switch
        {
            "get" or "list" => Get("/api/v1/settings"),
            "set" or "patch" or "update" => Patch("/api/v1/settings", SettingsPatch(args)),
            "rotate-key" => Post("/api/v1/settings/model-api-key/rotate"),
            _ => throw new InvalidOperationException($"Unknown settings action '{action}'.")
        };

    private static ControlRequest RuntimeRequest(string action, Arguments args)
        => action switch
        {
            "list" => Get("/api/v1/runtimes"),
            "scan" => Post("/api/v1/runtimes/scan"),
            "register" => Post("/api/v1/runtimes/register", new JsonObject { ["folder"] = Required(args, "folder") }),
            _ => throw new InvalidOperationException($"Unknown runtime action '{action}'.")
        };

    private static ControlRequest HuggingFaceRequest(string action, Arguments args)
        => action switch
        {
            "search" => Get($"/api/v1/huggingface/search?q={Uri.EscapeDataString(Required(args, "query", args.Positionals.ElementAtOrDefault(2)))}"),
            "download" => Post("/api/v1/huggingface/download", new JsonObject
            {
                ["query"] = args.Value("query") ?? "",
                ["repo"] = args.Value("repo") ?? "",
                ["path"] = args.Value("file") ?? args.Value("path") ?? "",
                ["revision"] = args.Value("revision") ?? "",
                ["dryRun"] = args.Has("dry-run")
            }),
            _ => throw new InvalidOperationException($"Unknown Hugging Face action '{action}'.")
        };

    private static ControlRequest JobRequest(string action, Arguments args)
        => action switch
        {
            "list" => Get("/api/v1/jobs"),
            "pause" or "resume" or "cancel" or "stop" => Post($"/api/v1/jobs/{Segment(Identifier(args, 2))}/{action}"),
            _ => throw new InvalidOperationException($"Unknown job action '{action}'.")
        };

    private static ControlRequest OperationRequest(string action, Arguments args)
    {
        if (action == "list") return Get("/api/v1/operations");
        if (action is not ("run" or "execute"))
            throw new InvalidOperationException($"Unknown operation action '{action}'.");
        var name = Identifier(args, 2);
        var body = SettingsPatch(args);
        body["confirm"] = args.Has("confirm");
        body["dryRun"] = args.Has("dry-run");
        return Post($"/api/v1/operations/{Segment(name)}", body);
    }

    private static ControlRequest RawRequest(Arguments args)
    {
        var method = Identifier(args, 1).ToUpperInvariant();
        var path = Identifier(args, 2);
        JsonObject? body = null;
        if (args.Value("body-file") is { Length: > 0 } file)
            body = JsonNode.Parse(File.ReadAllText(file)) as JsonObject ?? throw new InvalidOperationException("Body file must contain a JSON object.");
        else if (args.Value("body") is { Length: > 0 } json)
            body = JsonNode.Parse(json) as JsonObject ?? throw new InvalidOperationException("--body must be a JSON object.");
        return new ControlRequest(method, path, body);
    }

    private static JsonObject SettingsPatch(Arguments args)
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

    private static string SelfQuery(Arguments args)
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

    private static DiscoveryDocument Discover(Arguments args)
    {
        var paths = new List<string>();
        if (args.Value("connection") is { Length: > 0 } explicitPath) paths.Add(explicitPath);
        if (args.Value("workspace") is { Length: > 0 } workspace) paths.Add(Path.Combine(workspace, "state", "control.json"));
        foreach (var variable in new[] { "LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE", "LLAMA_CPP_CONSOLE_WORKSPACE", "LOCAL_LLM_CONSOLE_WORKSPACE" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } root)
                paths.Add(Path.Combine(root, "state", "control.json"));
        }
        paths.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "llama.cpp Windows Manager", "control.json"));
        paths.Add(Path.Combine(AppContext.BaseDirectory, "data", "state", "control.json"));

        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(path)) continue;
            try
            {
                var document = JsonSerializer.Deserialize<DiscoveryDocument>(File.ReadAllText(path), JsonOptions);
                if (document is null || string.IsNullOrWhiteSpace(document.BaseUrl) || string.IsNullOrWhiteSpace(document.ProtectedToken)) continue;
                if (document.ProcessId > 0)
                {
                    try { _ = Process.GetProcessById(document.ProcessId); }
                    catch { continue; }
                }
                return document;
            }
            catch { }
        }
        throw new InvalidOperationException("llama.cpp Windows Manager control endpoint was not found. Start the app, or pass --connection <control.json> / --workspace <path>.");
    }

    private static string Unprotect(string value)
    {
        if (!value.StartsWith(ProtectedPrefix, StringComparison.Ordinal)) return value;
        var protectedBytes = Convert.FromBase64String(value[ProtectedPrefix.Length..]);
        var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(bytes);
    }

    private static void WriteResponse(string text, bool compact)
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

    private sealed record ControlRequest(string Method, string Path, JsonObject? Body);
    private sealed record RawModelStop(string Model, string Action);
    private sealed record DiscoveryDocument(int Version, int ProcessId, string BaseUrl, string ProtectedToken, string WorkspaceRoot, DateTimeOffset StartedAt);

    private sealed class Arguments
    {
        private readonly Dictionary<string, List<string>> _options = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Positionals { get; } = [];

        public Arguments(string[] args)
        {
            for (var i = 0; i < args.Length; i++)
            {
                var item = args[i];
                if (!item.StartsWith("--", StringComparison.Ordinal))
                {
                    Positionals.Add(item);
                    continue;
                }
                var option = item[2..];
                var equals = option.IndexOf('=');
                if (equals >= 0)
                {
                    Add(option[..equals], option[(equals + 1)..]);
                    continue;
                }
                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    Add(option, args[++i]);
                else
                    Add(option, "true");
            }
        }

        public bool Has(string name) => _options.ContainsKey(name);
        public string? Value(string name) => _options.TryGetValue(name, out var values) ? values.LastOrDefault() : null;
        public IReadOnlyList<string> Values(string name) => _options.TryGetValue(name, out var values) ? values : [];
        private void Add(string name, string value)
        {
            if (!_options.TryGetValue(name, out var values)) _options[name] = values = [];
            values.Add(value);
        }
    }

    private const string HelpText = """
llwmctl - control llama.cpp Windows Manager

Core:
  llwmctl status | capabilities | self [--endpoint URL|--model ID|--session ID]
  llwmctl models list|get|scan|import|companions|delete
  llwmctl load|restart|unload MODEL [--profile NAME] [--runtime ID] [--set name=value] [--wait]
  llwmctl profiles list|create|update|delete --model MODEL [--id ID] [--name NAME] [--set name=value]
  llwmctl sessions list|get|logs|metrics [SESSION]
  llwmctl metrics
  llwmctl logs list|tail FILE [--tail CHARACTERS]
  llwmctl settings get|set --set name=value | settings rotate-key
  llwmctl runtimes list|scan|register --folder PATH
  llwmctl hf search QUERY
  llwmctl hf download --repo OWNER/REPO --file FILE.gguf [--revision REV]
  llwmctl jobs list|pause|resume|cancel JOB
  llwmctl operations list
  llwmctl operations run NAME [--set name=value] [--dry-run|--confirm]

Full settings:
  Repeat --set for any field returned by `llwmctl capabilities`.
  Use --settings-file settings.json for large setting objects.
  Launch overrides are one-shot unless --save-profile[=NAME] is supplied.
  Self-stop operations are blocked when identity is known; use --allow-self-stop only on explicit request.

Raw API:
  llwmctl request METHOD /api/v1/path [--body JSON|--body-file FILE]

Connection:
  The CLI auto-discovers the current app. Override with --connection FILE or --workspace PATH.
  Add --compact for single-line JSON.
""";
}
