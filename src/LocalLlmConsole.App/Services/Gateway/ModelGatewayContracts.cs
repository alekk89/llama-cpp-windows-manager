namespace LocalLlmConsole.Services;

public enum ModelGatewaySwapPolicy
{
    KeepLoaded,
    SingleActive
}

public sealed record ModelGatewayOptions(
    bool Enabled,
    string AccessMode,
    int Port,
    string ApiKey,
    bool RequireApiKeyAuth,
    ModelGatewaySwapPolicy SwapPolicy,
    long MaxRequestBodyBytes = 64L * 1024 * 1024,
    int MaxConcurrentRequests = 8,
    int RequestBodyTimeoutSeconds = 120,
    bool AutoLoadModels = true)
{
    public bool AllowLanAccess
        => AppPreferenceService.GatewayAllowsLanAccess(AccessMode);

    public string ListenerPrefix
        => GatewayUrlReservationService.ListenerPrefixForPort(Port, AllowLanAccess);

    public string LocalOpenAiBaseUrl
        => $"http://127.0.0.1:{Port.ToString(CultureInfo.InvariantCulture)}/v1";

    public static ModelGatewayOptions FromSettings(AppSettings settings)
        => new(
            settings.AutoLoadGatewayEnabled,
            settings.ModelAccessMode,
            settings.AutoLoadGatewayPort,
            RuntimeEndpointService.ModelApiKeyForClient(settings),
            settings.RequireApiKeyAuth,
            AppPreferenceService.GatewaySwapPolicy(settings.AutoLoadGatewayPolicy) == "singleActive"
                ? ModelGatewaySwapPolicy.SingleActive
                : ModelGatewaySwapPolicy.KeepLoaded,
            AutoLoadModels: settings.GatewayAutoLoadModels);
}

public sealed record ModelGatewayModelRoute(
    ModelRecord Model,
    NamedModelLaunchProfile Profile,
    string RouteId = "")
{
    public string Id
        => !string.IsNullOrWhiteSpace(RouteId)
            ? RouteId
            : RuntimeModelAliasService.ReadAliases(Profile.Settings.CustomParameters).FirstOrDefault() ?? LegacyId;

    public string LegacyRouteId { get; init; } = "";

    public string LegacyId
        => !string.IsNullOrWhiteSpace(LegacyRouteId)
            ? LegacyRouteId
            : Profile.IsDefault
            ? Model.Id
            : $"{Model.Id}--{ModelGatewayRouteId.SafeSegment(Profile.Id)}";

    public string Name
        => Profile.IsDefault
            ? Model.Name
            : $"{Model.Name} — {Profile.Name}";

    public bool MatchesRunningSession(LoadedModelSessionSnapshot? session)
        => session is { IsRunning: true }
            && string.Equals(session.ModelId, Model.Id, StringComparison.OrdinalIgnoreCase)
            && string.Equals(session.LaunchProfileId, Profile.Id, StringComparison.OrdinalIgnoreCase);
}

public static class ModelGatewayRouteId
{
    public static IReadOnlyList<ModelGatewayModelRoute> EnsureUnique(IReadOnlyList<ModelGatewayModelRoute> routes)
    {
        ArgumentNullException.ThrowIfNull(routes);
        var legacyCollisions = routes
            .GroupBy(route => route.LegacyId, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = routes.Select(route => legacyCollisions.Contains(route.LegacyId)
            ? route with { LegacyRouteId = $"{route.LegacyId}-{StableHash(route.Profile.Id)}" }
            : route).ToArray();

        // Reserve explicit names and old IDs before assigning suffixes. For example,
        // a real alias "qwen:2" must not be taken by a duplicate alias "qwen".
        var legacyIds = result.Select(route => route.LegacyId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preferredIds = result.Select(route => route.Id).ToArray();
        var reserved = preferredIds.Concat(legacyIds).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var assigned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextSuffix = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var ordered = Enumerable.Range(0, result.Length)
            .OrderByDescending(index => result[index].Profile.IsDefault)
            .ThenBy(index => result[index].Model.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(index => result[index].Profile.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(index => result[index].Model.Id, StringComparer.Ordinal)
            .ThenBy(index => result[index].Profile.Id, StringComparer.Ordinal);
        foreach (var index in ordered)
        {
            var route = result[index];
            var preferredId = preferredIds[index];
            var candidate = preferredId;
            if (assigned.Contains(candidate)
                || (legacyIds.Contains(candidate) && !candidate.Equals(route.LegacyId, StringComparison.OrdinalIgnoreCase)))
            {
                var suffix = nextSuffix.GetValueOrDefault(preferredId, 2);
                do
                {
                    candidate = $"{preferredId}:{suffix.ToString(CultureInfo.InvariantCulture)}";
                    suffix++;
                } while (reserved.Contains(candidate) || assigned.Contains(candidate));
                nextSuffix[preferredId] = suffix;
            }
            assigned.Add(candidate);
            result[index] = route with { RouteId = candidate };
        }
        return result;
    }

    public static string SafeSegment(string? value)
    {
        var source = (value ?? "").Trim().ToLowerInvariant();
        if (source.Length == 0) return "profile";

        var builder = new StringBuilder(source.Length);
        var previousWasSeparator = false;
        foreach (var character in source)
        {
            if (char.IsLetterOrDigit(character) || character is '.' or '_')
            {
                builder.Append(character);
                previousWasSeparator = false;
            }
            else if (!previousWasSeparator)
            {
                builder.Append('-');
                previousWasSeparator = true;
            }
        }

        var result = builder.ToString().Trim('-');
        return result.Length == 0 ? "profile" : result;
    }

    private static string StableHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public interface IModelGatewayRuntimeController
{
    Task<IReadOnlyList<ModelGatewayModelRoute>> ListModelsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<LoadedModelSessionSnapshot>> RunningSessionsAsync(CancellationToken cancellationToken = default);
    Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(ModelGatewayModelRoute route, ModelGatewaySwapPolicy policy, CancellationToken cancellationToken = default);
}

public interface IModelGatewayHost : IAsyncDisposable
{
    bool IsListening => true;
    string LastListenerError => "";
    Task StartAsync(CancellationToken cancellationToken = default);
}
