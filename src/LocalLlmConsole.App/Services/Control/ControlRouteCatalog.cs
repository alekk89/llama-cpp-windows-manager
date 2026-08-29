namespace LocalLlmConsole.Services;

public sealed record ControlRouteGroup(string Root, params string[] Routes);

public static class ControlRouteCatalog
{
    public static IReadOnlyList<ControlRouteGroup> Groups { get; } =
    [
        new("status", "GET /api/v1/status"),
        new("capabilities", "GET /api/v1/capabilities"),
        new("self", "GET /api/v1/self"),
        new("models",
            "GET /api/v1/models", "POST /api/v1/models/scan", "POST /api/v1/models/import",
            "GET /api/v1/models/{model}/companions", "POST /api/v1/models/{model}/load",
            "POST /api/v1/models/{model}/restart", "POST /api/v1/models/{model}/unload",
            "DELETE /api/v1/models/{model}?confirm=true", "GET /api/v1/models/{model}/profiles",
            "POST /api/v1/models/{model}/profiles", "PUT /api/v1/models/{model}/profiles/{profile}",
            "DELETE /api/v1/models/{model}/profiles/{profile}",
            "GET|PUT|DELETE /api/v1/models/{model}/profiles/{profile}/group"),
        new("model-groups",
            "GET|POST /api/v1/model-groups", "GET|PATCH|DELETE /api/v1/model-groups/{group}"),
        new("settings",
            "GET|PATCH /api/v1/settings", "POST /api/v1/settings/model-api-key/rotate"),
        new("runtimes",
            "GET /api/v1/runtimes", "POST /api/v1/runtimes/scan", "POST /api/v1/runtimes/register"),
        new("sessions",
            "GET /api/v1/sessions", "GET /api/v1/sessions/{session}/logs",
            "GET /api/v1/sessions/{session}/metrics", "GET /api/v1/sessions/{session}/inspect"),
        new("gateway", "GET /api/v1/gateway/inspect"),
        new("logs", "GET /api/v1/logs", "GET /api/v1/logs/{file}"),
        new("metrics",
            "GET /api/v1/metrics", "GET /api/v1/metrics/usage?range=1d|7d|30d|90d|all"),
        new("jobs", "GET /api/v1/jobs", "POST /api/v1/jobs/{job}/pause|resume|cancel"),
        new("benchmarks",
            "GET /api/v1/benchmarks", "GET /api/v1/benchmarks/schema", "GET /api/v1/benchmarks/presets",
            "GET /api/v1/benchmarks/capabilities?runtime=...&wslDistro=...", "POST /api/v1/benchmarks/validate", "POST /api/v1/benchmarks/run",
            "POST /api/v1/benchmarks/compare",
            "GET /api/v1/benchmarks/{run}", "GET /api/v1/benchmarks/{run}/wait",
            "GET /api/v1/benchmarks/{run}/plan", "GET /api/v1/benchmarks/{run}/results",
            "GET /api/v1/benchmarks/{run}/export", "GET /api/v1/benchmarks/{run}/log",
            "POST /api/v1/benchmarks/{run}/pause|resume|cancel", "DELETE /api/v1/benchmarks/{run}?confirm=true"),
        new("huggingface", "GET /api/v1/huggingface/search?q=...", "POST /api/v1/huggingface/download"),
        new("operations", "GET /api/v1/operations", "POST /api/v1/operations/{operation}")
    ];

    public static IReadOnlyList<string> AdvertisedRoutes { get; } = Groups
        .SelectMany(group => group.Routes)
        .ToArray();
}
