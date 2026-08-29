using static LocalLlmConsole.ControlCli.ControlCliArgumentValues;
using System.Globalization;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.ControlCli;

internal static partial class ControlCliRequestFactory
{
    internal static ControlRequest Build(Arguments args)
    {
        var group = args.Positionals[0].ToLowerInvariant();
        var action = args.Positionals.ElementAtOrDefault(1)?.ToLowerInvariant() ?? "list";
        return group switch
        {
            "status" => Get("/api/v1/status"),
            "capabilities" => Get("/api/v1/capabilities"),
            "self" or "whoami" => Get("/api/v1/self" + SelfQuery(args)),
            "models" or "model" => ModelRequest(action, args),
            "groups" or "group" or "model-groups" => ModelGroupRequest(action, args),
            "profiles" or "profile" => ProfileRequest(action, args),
            "load" => LoadRequest("load", args),
            "restart" => LoadRequest("restart", args),
            "unload" => Post($"/api/v1/models/{Segment(ModelArg(args, 1))}/unload"),
            "sessions" or "session" => SessionRequest(action, args),
            "gateway" => GatewayRequest(action),
            "metrics" => MetricsRequest(action, args),
            "logs" or "log" => LogRequest(action, args),
            "settings" or "setting" => SettingsRequest(action, args),
            "runtimes" or "runtime" => RuntimeRequest(action, args),
            "hf" or "huggingface" => HuggingFaceRequest(action, args),
            "jobs" or "job" => JobRequest(action, args),
            "benchmarks" or "benchmark" => BenchmarkRequest(action, args),
            "operations" or "operation" or "ops" => OperationRequest(action, args),
            "request" => RawRequest(args),
            _ => throw new InvalidOperationException($"Unknown command '{group}'. Run 'llwmctl help'.")
        };
    }

    internal static (string Method, string Path, JsonObject? Body) BuildForTests(params string[] args)
    {
        var request = Build(new Arguments(args));
        return (request.Method, request.Path, request.Body?.DeepClone().AsObject());
    }

    private static ControlRequest ModelRequest(string action, Arguments args)
        => action switch
        {
            "list" => Get("/api/v1/models"),
            "get" => Get($"/api/v1/models/{Segment(ModelArg(args, 2))}"),
            "scan" => Post("/api/v1/models/scan"),
            "import" => ModelImportRequest(args),
            "companions" or "heads" => Get($"/api/v1/models/{Segment(ModelArg(args, 2))}/companions"),
            "load" => LoadRequest("load", args, 2),
            "restart" => LoadRequest("restart", args, 2),
            "unload" => Post($"/api/v1/models/{Segment(ModelArg(args, 2))}/unload"),
            "delete" => Delete($"/api/v1/models/{Segment(ModelArg(args, 2))}?confirm={args.Has("confirm").ToString().ToLowerInvariant()}"),
            _ => throw new InvalidOperationException($"Unknown model action '{action}'.")
        };

    private static ControlRequest ModelImportRequest(Arguments args)
    {
        var file = args.Value("file")?.Trim() ?? "";
        var folder = args.Value("folder")?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(file) == string.IsNullOrWhiteSpace(folder))
            throw new InvalidOperationException("Specify exactly one of --file PATH or --folder PATH.");

        var body = new JsonObject();
        if (!string.IsNullOrWhiteSpace(file))
        {
            body["file"] = file;
            body["confirmRole"] = args.Has("confirm-role");
        }
        else
        {
            body["folder"] = folder;
        }
        return Post("/api/v1/models/import", body);
    }

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

    private static ControlRequest ModelGroupRequest(string action, Arguments args)
    {
        var identifier = args.Positionals.ElementAtOrDefault(2) ?? args.Value("id") ?? args.Value("group") ?? "";
        return action switch
        {
            "list" => Get("/api/v1/model-groups"),
            "get" => Get($"/api/v1/model-groups/{Segment(Required(args, "group", identifier))}"),
            "create" => Post("/api/v1/model-groups", ModelGroupBody(args, requireName: true)),
            "update" => Patch($"/api/v1/model-groups/{Segment(Required(args, "group", identifier))}", ModelGroupBody(args, requireName: false)),
            "delete" => Delete($"/api/v1/model-groups/{Segment(Required(args, "group", identifier))}"),
            "assign" => Put(
                $"/api/v1/models/{Segment(ModelArg(args, 2))}/profiles/{Segment(Required(args, "profile", args.Value("profile-id") ?? args.Positionals.ElementAtOrDefault(3)))}/group",
                new JsonObject { ["group"] = Required(args, "group", args.Positionals.ElementAtOrDefault(4)) }),
            "unassign" => Delete(
                $"/api/v1/models/{Segment(ModelArg(args, 2))}/profiles/{Segment(Required(args, "profile", args.Value("profile-id") ?? args.Positionals.ElementAtOrDefault(3)))}/group"),
            _ => throw new InvalidOperationException($"Unknown model group action '{action}'.")
        };
    }

    private static JsonObject ModelGroupBody(Arguments args, bool requireName)
    {
        var body = new JsonObject();
        if (requireName || args.Value("name") is { Length: > 0 })
            body["name"] = Required(args, "name");
        if (args.Value("retention") is { Length: > 0 } retention) body["retentionMode"] = retention;
        if (args.Value("idle-minutes") is { Length: > 0 } idleText)
        {
            if (!int.TryParse(idleText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var idleMinutes))
                throw new InvalidOperationException("--idle-minutes must be a whole number.");
            body["idleMinutes"] = idleMinutes;
        }
        if (args.Value("priority") is { Length: > 0 } priority) body["evictionPriority"] = priority;
        return body;
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
            "inspect" => Get($"/api/v1/sessions/{Segment(Identifier(args, 2))}/inspect"),
            _ => throw new InvalidOperationException($"Unknown session action '{action}'.")
        };

    private static ControlRequest GatewayRequest(string action)
        => action switch
        {
            "inspect" => Get("/api/v1/gateway/inspect"),
            _ => throw new InvalidOperationException($"Unknown gateway action '{action}'.")
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

}
