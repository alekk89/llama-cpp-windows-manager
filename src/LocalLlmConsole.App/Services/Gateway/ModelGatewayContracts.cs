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
    int RequestBodyTimeoutSeconds = 120)
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
                : ModelGatewaySwapPolicy.KeepLoaded);
}

public sealed record ModelGatewayModelRoute(
    ModelRecord Model,
    NamedModelLaunchProfile Profile,
    string RouteId = "")
{
    public string Id
        => !string.IsNullOrWhiteSpace(RouteId)
            ? RouteId
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
        var collisions = routes
            .GroupBy(route => route.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (collisions.Count == 0) return routes;

        return routes.Select(route => collisions.Contains(route.Id)
            ? route with { RouteId = $"{route.Id}-{StableHash(route.Profile.Id)}" }
            : route).ToArray();
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
