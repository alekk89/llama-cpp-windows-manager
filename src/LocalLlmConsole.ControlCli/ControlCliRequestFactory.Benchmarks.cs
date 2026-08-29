using System.Text.Json.Nodes;

namespace LocalLlmConsole.ControlCli;

internal static partial class ControlCliRequestFactory
{
    private static ControlRequest BenchmarkRequest(string action, Arguments args)
        => action switch
        {
            "list" => Get($"/api/v1/benchmarks?limit={IntValue(args, "limit", 100)}&offset={IntValue(args, "offset", 0)}"),
            "schema" or "contract" => Get("/api/v1/benchmarks/schema"),
            "presets" => Get("/api/v1/benchmarks/presets"),
            "capabilities" => BenchmarkCapabilitiesRequest(args),
            "validate" => Post("/api/v1/benchmarks/validate", new JsonObject { ["plan"] = ReadBenchmarkPlan(args) }),
            "run" or "start" => Post("/api/v1/benchmarks/run", new JsonObject
            {
                ["plan"] = ReadBenchmarkPlan(args),
                ["confirm"] = args.Has("confirm"),
                ["dryRun"] = args.Has("dry-run")
            }),
            "get" or "inspect" or "wait" => Get($"/api/v1/benchmarks/{Segment(Identifier(args, 2))}"),
            "plan" or "clone" => Get($"/api/v1/benchmarks/{Segment(Identifier(args, 2))}/plan"),
            "results" => Get($"/api/v1/benchmarks/{Segment(Identifier(args, 2))}/results?limit={IntValue(args, "limit", 200)}&offset={IntValue(args, "offset", 0)}&includePartial={(!args.Has("exclude-partial")).ToString().ToLowerInvariant()}"),
            "export" => Get($"/api/v1/benchmarks/{Segment(Identifier(args, 2))}/export?format={Uri.EscapeDataString(args.Value("format") ?? "json")}"),
            "log" => Get($"/api/v1/benchmarks/{Segment(Identifier(args, 2))}/log?tail={IntValue(args, "tail", 80000)}"),
            "compare" => Post("/api/v1/benchmarks/compare", new JsonObject
            {
                ["baselineRunId"] = Required(args, "baseline", args.Positionals.ElementAtOrDefault(2)),
                ["candidateRunId"] = Required(args, "candidate", args.Positionals.ElementAtOrDefault(3)),
                ["includePartialAttempts"] = args.Has("include-partial")
            }),
            "pause" or "resume" or "cancel" => Post($"/api/v1/benchmarks/{Segment(Identifier(args, 2))}/{action}"),
            "delete" or "remove" => Delete($"/api/v1/benchmarks/{Segment(Identifier(args, 2))}?confirm={args.Has("confirm").ToString().ToLowerInvariant()}"),
            _ => throw new InvalidOperationException($"Unknown benchmark action '{action}'.")
        };

    private static ControlRequest BenchmarkCapabilitiesRequest(Arguments args)
    {
        var runtime = args.Value("runtime") ?? args.Positionals.ElementAtOrDefault(2) ?? "";
        var distro = args.Value("wsl-distro") ?? "";
        return Get($"/api/v1/benchmarks/capabilities?runtime={Uri.EscapeDataString(runtime)}&wslDistro={Uri.EscapeDataString(distro)}");
    }

    private static JsonNode ReadBenchmarkPlan(Arguments args)
    {
        var path = Required(args, "plan");
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath)) throw new FileNotFoundException("Benchmark plan file was not found.", fullPath);
        return JsonNode.Parse(File.ReadAllText(fullPath))
            ?? throw new InvalidOperationException("Benchmark plan file was empty.");
    }
}
