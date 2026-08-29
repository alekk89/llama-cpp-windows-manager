using System.Text.Json;
using static LocalLlmConsole.ControlCli.ControlCliArgumentValues;

namespace LocalLlmConsole.ControlCli;

internal static class ControlCliSelfSafety
{
    internal static async Task EnsureAllowedAsync(HttpClient http, Arguments args, ControlRequest request)
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
        var benchmarkMayStopSelf = group is "benchmarks" or "benchmark"
            && action is "run" or "start"
            && request.Body?["plan"]?["stopActiveSessions"]?.GetValue<bool>() == true
            && !args.Has("dry-run");
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
            || benchmarkMayStopSelf
            || operationMayStopSelf;
        if (!destructive) return;

        using var selfResponse = await http.GetAsync(("api/v1/self" + SelfQuery(args)).TrimStart('/'));
        if (!selfResponse.IsSuccessStatusCode)
            throw new InvalidOperationException(
                "Could not identify the current agent session before a destructive request. " +
                "Retry after the Manager is responsive, or use --allow-self-stop only when the user explicitly requested that consequence.");
        using var selfJson = JsonDocument.Parse(await selfResponse.Content.ReadAsStringAsync());
        var root = selfJson.RootElement;
        var hasCandidates = root.TryGetProperty("candidates", out var candidates)
            && candidates.ValueKind == JsonValueKind.Array
            && candidates.GetArrayLength() > 0;
        if (!root.TryGetProperty("identified", out var identified) || identified.ValueKind != JsonValueKind.True)
        {
            if (!hasCandidates) return;
            throw new InvalidOperationException(
                "The Manager cannot prove which running model, if any, serves this client. A lone running session is only a candidate, not an identified agent session. " +
                "Supply --session, --model, --endpoint, --port, or --process-id, or use --allow-self-stop only when the user explicitly requested that consequence.");
        }
        if (StringProperty(root, "confidence").Equals("inferred-single-running-session", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The Manager inferred the only running model as a candidate, but that does not prove it serves this client. " +
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

        if (benchmarkMayStopSelf)
            throw new InvalidOperationException(
                "Refusing a benchmark plan that stops active sessions because it can terminate the current agent session. " +
                "Retry with --allow-self-stop only when the user explicitly requested that consequence.");

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

    private static string ModelArg(Arguments args, int positionalIndex)
        => args.Value("model") is { Length: > 0 } model
            ? model
            : args.Positionals.ElementAtOrDefault(positionalIndex)
                ?? throw new InvalidOperationException("--model is required.");

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

    private static string Segment(string value) => Uri.EscapeDataString(value);

    private sealed record RawModelStop(string Model, string Action);
}
