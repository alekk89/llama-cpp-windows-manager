using System.Globalization;
using System.Net;

namespace LocalLlmConsole.Services;

public enum EndpointInspectionKind
{
    DirectModel,
    Gateway
}

public sealed record EndpointInspectionModel(
    string Id,
    string Name,
    string Owner,
    string Profile,
    long? Created,
    long? TrainingContext,
    long? ParameterCount,
    long? SizeBytes);

public sealed record EndpointInspectionDefaults(
    string ModelFile,
    int? ContextSize,
    int? ParallelSlots,
    int? MaximumOutputTokens,
    bool? Speculative,
    string Reasoning,
    string ReasoningFormat,
    string Vision,
    double? Temperature,
    int? TopK,
    double? TopP,
    double? MinP,
    string Build,
    bool? Sleeping,
    IReadOnlyDictionary<string, bool> ChatCapabilities);

public sealed record EndpointInspectionSlot(
    int? Id,
    bool IsProcessing,
    int? ContextSize,
    int? MaximumOutputTokens,
    int? DecodedTokens,
    int? RemainingTokens,
    string ReasoningFormat,
    bool? Speculative,
    double? Temperature,
    int? TopK,
    double? TopP,
    double? MinP);

public sealed record EndpointInspectionRunningModel(
    string Id,
    string Name,
    string Status,
    string Runtime,
    string Endpoint,
    DateTimeOffset? StartedAt);

public sealed record EndpointInspectionReport(
    EndpointInspectionKind Kind,
    string Title,
    string Endpoint,
    string Health,
    DateTimeOffset InspectedAt,
    IReadOnlyList<EndpointInspectionModel> Models,
    EndpointInspectionDefaults? Defaults,
    IReadOnlyList<EndpointInspectionSlot> Slots,
    IReadOnlyList<EndpointInspectionRunningModel> RunningModels,
    string GatewayPolicy,
    string GatewayExposure,
    IReadOnlyList<string> UnavailableSources)
{
    public bool IsReachable => !Health.StartsWith("Unavailable", StringComparison.OrdinalIgnoreCase);
}

public sealed class EndpointInspectionService
{
    private sealed record ProbeResult(string Path, HttpStatusCode? StatusCode, JsonNode? Json, string Error)
    {
        public bool IsSuccess => StatusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices && Json is not null;
    }

    private readonly HttpClient _http;

    public EndpointInspectionService(HttpClient http)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
    }

    public Task<EndpointInspectionReport> InspectDirectAsync(
        LoadedModelSessionSnapshot session,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);
        return InspectAsync(
            EndpointInspectionKind.DirectModel,
            session.ModelName,
            RuntimeEndpointService.LocalServerBaseUrl(session.LaunchSettings),
            session.LaunchSettings,
            gatewayPolicy: "",
            gatewayExposure: "",
            cancellationToken);
    }

    public Task<EndpointInspectionReport> InspectGatewayAsync(
        AppSettings settings,
        string policy,
        string exposure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return InspectAsync(
            EndpointInspectionKind.Gateway,
            "Shared gateway",
            RuntimeEndpointService.LocalGatewayServerBaseUrl(settings),
            settings,
            policy,
            exposure,
            cancellationToken);
    }

    private async Task<EndpointInspectionReport> InspectAsync(
        EndpointInspectionKind kind,
        string title,
        string serverBaseUrl,
        AppSettings settings,
        string gatewayPolicy,
        string gatewayExposure,
        CancellationToken cancellationToken)
    {
        var paths = kind == EndpointInspectionKind.DirectModel
            ? new[] { "/health", "/v1/models", "/props", "/slots" }
            : new[] { "/health", "/v1/models", "/running" };
        var probes = await Task.WhenAll(paths.Select(path => ProbeAsync(
            serverBaseUrl,
            path,
            settings,
            cancellationToken)));
        var byPath = probes.ToDictionary(probe => probe.Path, StringComparer.OrdinalIgnoreCase);
        var health = HealthLabel(byPath["/health"]);
        var unavailable = probes
            .Where(probe => !probe.IsSuccess)
            .Select(probe => $"{probe.Path}: {FailureLabel(probe)}")
            .ToArray();

        return new EndpointInspectionReport(
            kind,
            title,
            $"{serverBaseUrl}/v1",
            health,
            DateTimeOffset.Now,
            ParseModels(SuccessfulJson(byPath.GetValueOrDefault("/v1/models"))),
            kind == EndpointInspectionKind.DirectModel
                ? ParseDefaults(SuccessfulJson(byPath.GetValueOrDefault("/props")))
                : null,
            kind == EndpointInspectionKind.DirectModel
                ? ParseSlots(SuccessfulJson(byPath.GetValueOrDefault("/slots")))
                : [],
            kind == EndpointInspectionKind.Gateway
                ? ParseRunning(SuccessfulJson(byPath.GetValueOrDefault("/running")))
                : [],
            gatewayPolicy,
            gatewayExposure,
            unavailable);
    }

    private static JsonNode? SuccessfulJson(ProbeResult? result)
        => result is { IsSuccess: true } ? result.Json : null;

    private async Task<ProbeResult> ProbeAsync(
        string serverBaseUrl,
        string path,
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = RuntimeEndpointService.RuntimeGetRequest($"{serverBaseUrl}{path}", settings);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            JsonNode? json = null;
            if (!string.IsNullOrWhiteSpace(body))
            {
                try { json = JsonNode.Parse(body); }
                catch (JsonException) { }
            }
            return new ProbeResult(path, response.StatusCode, json, response.IsSuccessStatusCode ? "" : response.ReasonPhrase ?? "HTTP error");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProbeResult(path, null, null, InnermostMessage(ex));
        }
    }

    public static IReadOnlyList<EndpointInspectionModel> ParseModels(JsonNode? root)
    {
        var response = root as JsonObject;
        var models = response?["data"] as JsonArray ?? response?["models"] as JsonArray;
        if (models is null) return [];
        return models.OfType<JsonObject>().Select(model =>
        {
            var meta = model["meta"] as JsonObject;
            return new EndpointInspectionModel(
                Text(model, "id", "model"),
                Text(model, "name"),
                Text(model, "owned_by"),
                Text(model, "profile_name"),
                Integer(model, "created"),
                Integer(meta, "n_ctx_train"),
                Integer(meta, "n_params"),
                Integer(meta, "size"));
        }).ToArray();
    }

    public static EndpointInspectionDefaults? ParseDefaults(JsonNode? root)
    {
        if (root is not JsonObject props) return null;
        var generation = props["default_generation_settings"] as JsonObject;
        var parameters = generation?["params"] as JsonObject;
        var caps = props["chat_template_caps"] as JsonObject;
        var chatCapabilities = caps is null
            ? new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            : caps.Where(pair => BooleanValue(pair.Value).HasValue)
                .ToDictionary(pair => pair.Key, pair => BooleanValue(pair.Value)!.Value, StringComparer.OrdinalIgnoreCase);
        var reasoning = FirstText(parameters, "reasoning", "enable_reasoning", "reasoning_effort");
        if (string.IsNullOrWhiteSpace(reasoning))
        {
            var reasoningCaps = chatCapabilities.Where(pair => pair.Key.Contains("reason", StringComparison.OrdinalIgnoreCase)).ToArray();
            reasoning = reasoningCaps.Any(pair => pair.Value)
                ? "Supported by chat template; request-controlled"
                : reasoningCaps.Length > 0
                    ? "Not supported by reported chat template"
                    : "Not reported; request-controlled";
        }

        return new EndpointInspectionDefaults(
            Path.GetFileName(Text(props, "model_path")),
            Integer32(generation, "n_ctx"),
            Integer32(props, "total_slots"),
            FirstInteger32(parameters, "max_tokens", "n_predict"),
            BooleanValue(generation?["speculative"]),
            reasoning,
            FirstText(parameters, "reasoning_format", "reason_format"),
            VisionLabel(props["modalities"]),
            Number(parameters, "temperature"),
            Integer32(parameters, "top_k"),
            Number(parameters, "top_p"),
            Number(parameters, "min_p"),
            Text(props, "build_info"),
            BooleanValue(props["is_sleeping"]),
            chatCapabilities);
    }

    public static IReadOnlyList<EndpointInspectionSlot> ParseSlots(JsonNode? root)
    {
        if (root is not JsonArray slots) return [];
        return slots.OfType<JsonObject>().Select(slot =>
        {
            var parameters = slot["params"] as JsonObject;
            var nextToken = slot["next_token"] as JsonObject;
            return new EndpointInspectionSlot(
                Integer32(slot, "id"),
                BooleanValue(slot["is_processing"]) == true,
                Integer32(slot, "n_ctx"),
                FirstInteger32(parameters, "max_tokens", "n_predict"),
                Integer32(nextToken, "n_decoded"),
                Integer32(nextToken, "n_remain"),
                FirstText(parameters, "reasoning_format", "reason_format"),
                BooleanValue(slot["speculative"]),
                Number(parameters, "temperature"),
                Integer32(parameters, "top_k"),
                Number(parameters, "top_p"),
                Number(parameters, "min_p"));
        }).ToArray();
    }

    public static IReadOnlyList<EndpointInspectionRunningModel> ParseRunning(JsonNode? root)
    {
        var running = (root as JsonObject)?["data"] as JsonArray;
        if (running is null) return [];
        return running.OfType<JsonObject>().Select(model => new EndpointInspectionRunningModel(
            Text(model, "id"),
            Text(model, "name"),
            Text(model, "status"),
            Text(model, "runtime"),
            Text(model, "endpoint"),
            DateTimeOffset.TryParse(Text(model, "startedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var started)
                ? started
                : null)).ToArray();
    }

    private static string HealthLabel(ProbeResult health)
    {
        if (!health.IsSuccess) return $"Unavailable — {FailureLabel(health)}";
        if (BooleanValue((health.Json as JsonObject)?["ok"]) == true) return "Healthy";
        var status = Text(health.Json as JsonObject, "status");
        return string.IsNullOrWhiteSpace(status) ? "Responding" : status;
    }

    private static string FailureLabel(ProbeResult result)
        => result.StatusCode.HasValue
            ? $"HTTP {(int)result.StatusCode.Value} {result.Error}".Trim()
            : string.IsNullOrWhiteSpace(result.Error) ? "No response" : result.Error;

    private static string VisionLabel(JsonNode? modalities)
    {
        if (modalities is JsonObject obj && BooleanValue(obj["vision"]) is { } vision)
            return vision ? "Supported" : "Not reported as supported";
        if (modalities is JsonArray array)
            return array.Any(item => item?.ToString().Equals("vision", StringComparison.OrdinalIgnoreCase) == true)
                ? "Supported"
                : "Not reported as supported";
        return "Not reported";
    }

    private static string FirstText(JsonObject? obj, params string[] keys)
    {
        foreach (var key in keys)
        {
            var value = Text(obj, key);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static string Text(JsonObject? obj, params string[] keys)
    {
        if (obj is null) return "";
        foreach (var key in keys)
        {
            var value = obj[key]?.ToString();
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static long? Integer(JsonObject? obj, string key)
        => long.TryParse(obj?[key]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? Integer32(JsonObject? obj, string key)
        => int.TryParse(obj?[key]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static int? FirstInteger32(JsonObject? obj, params string[] keys)
    {
        foreach (var key in keys)
            if (Integer32(obj, key) is { } value) return value;
        return null;
    }

    private static double? Number(JsonObject? obj, string key)
        => double.TryParse(obj?[key]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : null;

    private static bool? BooleanValue(JsonNode? node)
        => bool.TryParse(node?.ToString(), out var value) ? value : null;

    private static string InnermostMessage(Exception ex)
    {
        while (ex.InnerException is not null) ex = ex.InnerException;
        return string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
    }
}
