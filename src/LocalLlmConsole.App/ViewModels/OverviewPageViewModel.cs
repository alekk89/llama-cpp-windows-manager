using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed record GatewayRoutingOverviewStatus(
    bool Visible,
    bool Enabled,
    string Endpoint,
    string State,
    string Policy,
    string Exposure,
    int RunningSessions)
{
    public static GatewayRoutingOverviewStatus Hidden { get; } = new(false, false, "", "", "", "", 0);

    public static GatewayRoutingOverviewStatus FromEndpoint(string endpoint)
        => string.IsNullOrWhiteSpace(endpoint)
            ? Hidden
            : new(true, true, endpoint.Trim(), "Listening", "", "", 0);
}

public sealed record OverviewLaunchProfileChoice(string Id, string Name);

public enum OverviewModelChoiceKind
{
    Model,
    Group
}

public sealed record OverviewModelChoice(
    string Id,
    string Name,
    OverviewModelChoiceKind Kind,
    ModelRecord? Model = null,
    ModelGroupRecord? Group = null,
    int LaunchProfileCount = 0,
    IReadOnlyList<string>? LaunchProfileIds = null,
    string SizeLabel = "")
{
    public bool IsMissing => Kind == OverviewModelChoiceKind.Model
        && string.Equals(SizeLabel, "Missing", StringComparison.OrdinalIgnoreCase);

    public string DisplayName => Kind == OverviewModelChoiceKind.Group
        ? $"Group · {Name} ({LaunchProfileCount})"
        : string.IsNullOrWhiteSpace(SizeLabel) ? Name : $"{Name} · {SizeLabel}";
}

public sealed class OverviewPageViewModel
{
    private GatewayRoutingOverviewStatus _lastSessionGateway = GatewayRoutingOverviewStatus.Hidden;
    private SessionRowSource[] _lastSessionRowSources = [];

    public ObservableCollection<OverviewModelChoice> ModelChoices { get; } = new();
    public ObservableCollection<OverviewLaunchProfileChoice> LaunchProfileChoices { get; } = new();
    public ObservableCollection<OverviewSessionRow> SessionRows { get; } = new();

    public void ReplaceModels(IEnumerable<ModelRecord> models)
        => ReplaceModels(models, [], new Dictionary<string, ModelGroupAssignment>(), []);

    public void ReplaceModels(
        IEnumerable<ModelRecord> models,
        IEnumerable<ModelGroupRecord> groups,
        IReadOnlyDictionary<string, ModelGroupAssignment> assignments,
        IEnumerable<NamedModelLaunchProfile> profiles,
        IReadOnlyDictionary<string, string>? modelSizeLabels = null)
    {
        var modelChoices = models
            .Where(model => !ModelAliasService.IsLaunchAlias(model))
            .OrderBy(model => model.Name, StringComparer.OrdinalIgnoreCase)
            .Select(model => new OverviewModelChoice(
                model.Id,
                model.Name,
                OverviewModelChoiceKind.Model,
                Model: model,
                SizeLabel: modelSizeLabels?.GetValueOrDefault(model.Id) ?? ""));
        var assignedProfileIds = profiles.Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var profilesByGroup = assignments.Values
            .Where(assignment => assignedProfileIds.Contains(assignment.LaunchProfileId))
            .GroupBy(assignment => assignment.GroupId, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(assignment => assignment.LaunchProfileId).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                StringComparer.OrdinalIgnoreCase);
        var groupChoices = groups
            .OrderBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => new OverviewModelChoice(
                group.Id,
                group.Name,
                OverviewModelChoiceKind.Group,
                Group: group,
                LaunchProfileCount: profilesByGroup.GetValueOrDefault(group.Id)?.Length ?? 0,
                LaunchProfileIds: profilesByGroup.GetValueOrDefault(group.Id) ?? []));
        var ordered = modelChoices.Concat(groupChoices)
            .ToArray();
        if (ModelChoices.SequenceEqual(ordered)) return;

        ModelChoices.Clear();
        foreach (var model in ordered)
            ModelChoices.Add(model);
    }

    public void ReplaceGroupLaunchProfileSummary(ModelGroupRecord group, int launchProfileCount)
    {
        var label = launchProfileCount == 1 ? "1 launch profile" : $"{launchProfileCount} launch profiles";
        var choices = new[] { new OverviewLaunchProfileChoice(group.Id, label) };
        if (LaunchProfileChoices.SequenceEqual(choices)) return;
        LaunchProfileChoices.Clear();
        LaunchProfileChoices.Add(choices[0]);
    }

    public void ReplaceLaunchProfiles(IEnumerable<NamedModelLaunchProfile> profiles)
    {
        var choices = profiles
            .OrderByDescending(profile => profile.IsDefault)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new OverviewLaunchProfileChoice(profile.Id, profile.Name))
            .ToArray();
        if (LaunchProfileChoices.SequenceEqual(choices)) return;

        LaunchProfileChoices.Clear();
        foreach (var choice in choices)
            LaunchProfileChoices.Add(choice);
    }

    public void ReplaceSessions(IEnumerable<LoadedModelSessionSnapshot> sessions, string gatewayEndpoint = "")
        => _ = ReplaceSessionsIfChanged(sessions, GatewayRoutingOverviewStatus.FromEndpoint(gatewayEndpoint));

    public bool ReplaceSessionsIfChanged(IEnumerable<LoadedModelSessionSnapshot> sessions, string gatewayEndpoint = "")
        => ReplaceSessionsIfChanged(sessions, GatewayRoutingOverviewStatus.FromEndpoint(gatewayEndpoint));

    public void ReplaceSessions(IEnumerable<LoadedModelSessionSnapshot> sessions, GatewayRoutingOverviewStatus gateway)
        => _ = ReplaceSessionsIfChanged(sessions, gateway);

    public bool ReplaceSessionsIfChanged(IEnumerable<LoadedModelSessionSnapshot> sessions, GatewayRoutingOverviewStatus gateway)
    {
        var sessionRows = sessions.ToArray();
        var sources = sessionRows
            .OrderBy(session => session.ModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.LaunchProfileName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase)
            .Select(SessionRowSource.FromSnapshot)
            .ToArray();
        if (gateway == _lastSessionGateway
            && sources.SequenceEqual(_lastSessionRowSources))
            return false;

        var rows = BuildSessionRows(sessionRows, gateway).ToArray();
        SessionRows.Clear();
        foreach (var row in rows)
            SessionRows.Add(row);
        _lastSessionGateway = gateway;
        _lastSessionRowSources = sources;
        return true;
    }

    private static IEnumerable<OverviewSessionRow> BuildSessionRows(
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        GatewayRoutingOverviewStatus gateway)
    {
        if (gateway.Visible)
            yield return GatewayRow(gateway);

        foreach (var session in sessions
                     .OrderBy(session => session.ModelName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(session => session.LaunchProfileName, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(session => session.SessionId, StringComparer.OrdinalIgnoreCase))
        {
            yield return new OverviewSessionRow
            {
                Kind = OverviewEndpointKind.Session,
                ModelName = session.IsSelected ? $"{session.ModelName} (selected)" : session.ModelName,
                ProfileName = string.IsNullOrWhiteSpace(session.LaunchProfileName) ? "Unknown" : session.LaunchProfileName,
                Size = session.ModelSize,
                State = SessionStatusLabel(session),
                Endpoint = session.Endpoint,
                Runtime = session.RuntimeName,
                Backend = $"{session.Backend} {session.Mode}",
                ActionLabel = session.IsRunning && session.Status != LoadedModelSessionStatus.Stopping ? "Unload" : "",
                CanUnload = session.IsRunning && session.Status != LoadedModelSessionStatus.Stopping,
                CanInspect = session.IsRunning && session.Status is LoadedModelSessionStatus.Running or LoadedModelSessionStatus.Warm or LoadedModelSessionStatus.Unreachable,
                SessionId = session.SessionId,
                ModelId = session.ModelId
            };
        }
    }

    private static OverviewSessionRow GatewayRow(GatewayRoutingOverviewStatus gateway)
        => new()
        {
            Kind = OverviewEndpointKind.Gateway,
            ModelName = gateway.Enabled ? "Gateway (shared endpoint)" : "Gateway (off)",
            ProfileName = "—",
            Size = "Shared router",
            State = string.IsNullOrWhiteSpace(gateway.State) ? (gateway.Enabled ? "Enabled" : "Off") : gateway.State,
            Endpoint = gateway.Enabled ? gateway.Endpoint : "Gateway disabled",
            Runtime = string.IsNullOrWhiteSpace(gateway.Policy) ? "" : gateway.Policy,
            Backend = string.IsNullOrWhiteSpace(gateway.Exposure) ? "" : gateway.Exposure,
            CanInspect = gateway.Enabled
        };

    private sealed record SessionRowSource(
        string SessionId,
        string ModelId,
        string ModelName,
        string LaunchProfileName,
        long ModelSizeBytes,
        LoadedModelSessionStatus Status,
        string StatusReason,
        bool IsRunning,
        bool IsSelected,
        string Endpoint,
        string RuntimeName,
        RuntimeBackend Backend,
        RuntimeMode Mode)
    {
        public static SessionRowSource FromSnapshot(LoadedModelSessionSnapshot session)
            => new(
                session.SessionId,
                session.ModelId,
                session.ModelName,
                session.LaunchProfileName,
                session.ModelSizeBytes,
                session.Status,
                session.StatusReason,
                session.IsRunning,
                session.IsSelected,
                session.Endpoint,
                session.RuntimeName,
                session.Backend,
                session.Mode);
    }

    private static string SessionStatusLabel(LoadedModelSessionSnapshot session) => session.Status switch
    {
        LoadedModelSessionStatus.Running or LoadedModelSessionStatus.Warm => "Loaded",
        LoadedModelSessionStatus.Loading => "Loading",
        LoadedModelSessionStatus.Unreachable => "Unreachable",
        LoadedModelSessionStatus.Stopping => "Stopping",
        LoadedModelSessionStatus.Failed => string.IsNullOrWhiteSpace(session.StatusReason) ? "Failed" : $"Failed — {session.StatusReason}",
        _ => string.IsNullOrWhiteSpace(session.StatusReason) ? "Unloaded" : $"Unloaded — {session.StatusReason}"
    };

}
