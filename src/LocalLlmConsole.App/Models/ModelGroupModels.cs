namespace LocalLlmConsole.Models;

public enum ModelGroupRetentionMode
{
    Inherit,
    Pinned,
    IdleTimeout
}

public enum ModelGroupEvictionPriority
{
    Low = 0,
    Normal = 1,
    High = 2
}

public sealed record ModelGroupRecord(
    string Id,
    string Name,
    ModelGroupRetentionMode RetentionMode,
    int IdleMinutes,
    ModelGroupEvictionPriority EvictionPriority,
    DateTimeOffset UpdatedAt);

public sealed record ModelGroupAssignment(
    string LaunchProfileId,
    string GroupId,
    DateTimeOffset UpdatedAt);

public sealed record ModelGroupSnapshot(
    IReadOnlyList<ModelGroupRecord> Groups,
    IReadOnlyDictionary<string, ModelGroupAssignment> Assignments)
{
    public ModelGroupRecord? GroupForProfile(string launchProfileId)
        => Assignments.TryGetValue(launchProfileId, out var assignment)
            ? Groups.FirstOrDefault(group => group.Id.Equals(assignment.GroupId, StringComparison.OrdinalIgnoreCase))
            : null;
}

public sealed record EffectiveModelRetentionPolicy(
    bool AllowsIdleUnload,
    int IdleMinutes,
    ModelGroupEvictionPriority EvictionPriority,
    string GroupId = "",
    string GroupName = "");
