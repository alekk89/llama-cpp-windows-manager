using System.Net;
using System.Net.NetworkInformation;

namespace LocalLlmConsole.Services;

/// <summary>How an agent's model endpoint relates to this Manager.</summary>
public enum AgentEndpointKind
{
    /// <summary>A running Manager session's direct endpoint on this machine.</summary>
    Session,
    /// <summary>This Manager's model gateway; any running session may serve the agent.</summary>
    Gateway,
    /// <summary>Not served by this Manager (another host, a cloud API, or an unused local port).</summary>
    NotManaged
}

public static class ControlSelfIdentification
{
    /// <summary>
    /// Decides whether the endpoint an agent's model is served from belongs to this Manager. A
    /// direct session or the gateway counts only when the endpoint names this machine; anything
    /// else (for example a cloud API) cannot be a Manager model, so stopping one cannot stop the agent.
    /// </summary>
    public static AgentEndpointKind ClassifyEndpoint(
        Uri endpoint,
        IReadOnlyList<LoadedModelSessionSnapshot> runningSessions,
        int gatewayPort,
        Func<string, bool> isThisMachine,
        out LoadedModelSessionSnapshot? session)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(runningSessions);
        ArgumentNullException.ThrowIfNull(isThisMachine);
        session = null;
        if (!endpoint.IsAbsoluteUri || !isThisMachine(endpoint.Host)) return AgentEndpointKind.NotManaged;
        var matches = runningSessions.Where(candidate => candidate.LaunchSettings.Port == endpoint.Port).ToArray();
        if (matches.Length == 1)
        {
            session = matches[0];
            return AgentEndpointKind.Session;
        }
        return endpoint.Port == gatewayPort ? AgentEndpointKind.Gateway : AgentEndpointKind.NotManaged;
    }

    /// <summary>True for loopback and wildcard addresses, localhost names, this computer's name, and its interface addresses.</summary>
    public static bool IsThisMachine(string host)
    {
        var name = (host ?? "").Trim().Trim('[', ']').TrimEnd('.');
        if (name.Length == 0) return false;
        if (name.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || name.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || name.StartsWith(Environment.MachineName + ".", StringComparison.OrdinalIgnoreCase))
            return true;
        if (!IPAddress.TryParse(name, out var address)) return false;
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .SelectMany(network => network.GetIPProperties().UnicastAddresses)
                .Any(unicast => unicast.Address.Equals(address));
        }
        catch (NetworkInformationException)
        {
            return false;
        }
    }

    public static bool MatchesModelHint(
        LoadedModelSessionSnapshot session,
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        string modelHint)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(models);
        ArgumentNullException.ThrowIfNull(profiles);

        var candidates = ModelHintCandidates(modelHint).ToArray();
        var routes = ModelGatewayRouteId.EnsureUnique(models
            .SelectMany(model => profiles
                .Where(profile => profile.ModelId.Equals(model.Id, StringComparison.OrdinalIgnoreCase))
                .Select(profile => new ModelGatewayModelRoute(model, profile)))
            .ToArray());
        var route = candidates
            .Select(candidate => ModelGatewayRequestResolver.ResolveModel(routes, candidate))
            .FirstOrDefault(candidate => candidate is not null);
        if (route is not null)
        {
            return session.ModelId.Equals(route.Model.Id, StringComparison.OrdinalIgnoreCase)
                && (route.Profile.IsDefault
                    || session.LaunchProfileId.Equals(route.Profile.Id, StringComparison.OrdinalIgnoreCase));
        }

        var model = candidates
            .Select(candidate => ModelGatewayRequestResolver.ResolveModel(models, candidate))
            .FirstOrDefault(candidate => candidate is not null);
        return session.ModelId.Equals(model?.Id ?? modelHint, StringComparison.OrdinalIgnoreCase)
            || candidates.Any(candidate => session.ModelName.Equals(candidate, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ModelHintCandidates(string value)
    {
        var candidate = (value ?? "").Trim();
        if (candidate.Length == 0) yield break;
        yield return candidate;
        var separator = candidate.IndexOf('/');
        if (separator >= 0 && separator + 1 < candidate.Length)
            yield return candidate[(separator + 1)..];
    }
}
