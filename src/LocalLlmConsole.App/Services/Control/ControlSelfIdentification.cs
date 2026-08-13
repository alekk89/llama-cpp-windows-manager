namespace LocalLlmConsole.Services;

public static class ControlSelfIdentification
{
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
