namespace LocalLlmConsole.ControlCli;

internal static partial class ControlCliRequestFactory
{
    private static ControlRequest MetricsRequest(string action, Arguments args)
    {
        if (action is "list" or "live") return Get("/api/v1/metrics");
        if (action is not ("usage" or "history"))
            throw new InvalidOperationException($"Unknown metrics action '{action}'.");

        var query = new List<string>
        {
            $"range={Uri.EscapeDataString(args.Value("range") ?? "30d")}"
        };
        AddQuery(query, "model", args.Value("model"));
        AddQuery(query, "profile", args.Value("profile"));
        AddQuery(query, "runtime", args.Value("runtime"));
        AddQuery(query, "timeZone", args.Value("time-zone"));
        var dates = args.Values("date")
            .Concat((args.Value("dates") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (dates.Length > 0) AddQuery(query, "dates", string.Join(',', dates));
        return Get("/api/v1/metrics/usage?" + string.Join("&", query));
    }

    private static void AddQuery(List<string> query, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            query.Add($"{name}={Uri.EscapeDataString(value)}");
    }
}
