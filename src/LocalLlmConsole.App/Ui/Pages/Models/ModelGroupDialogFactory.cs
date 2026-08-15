namespace LocalLlmConsole;

public sealed class ModelGroupEditorRow
{
    public string EditorKey { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public ModelGroupRetentionMode RetentionMode { get; set; } = ModelGroupRetentionMode.Inherit;
    public int IdleMinutes { get; set; } = ModelGroupService.DefaultIdleMinutes;
    public ModelGroupEvictionPriority EvictionPriority { get; set; } = ModelGroupEvictionPriority.Normal;
    public int ProfileCount { get; set; }
    public string RetentionLabel => RetentionMode switch
    {
        ModelGroupRetentionMode.Pinned => Loc.T("ModelGroups.Retention.Pinned"),
        ModelGroupRetentionMode.IdleTimeout => Loc.T("ModelGroups.Retention.IdleTimeout"),
        _ => Loc.T("ModelGroups.Retention.Inherit")
    };
    public string IdleMinutesLabel => RetentionMode == ModelGroupRetentionMode.IdleTimeout
        ? IdleMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "—";
    public string EvictionPriorityLabel => EvictionPriority switch
    {
        ModelGroupEvictionPriority.Low => Loc.T("ModelGroups.Priority.Low"),
        ModelGroupEvictionPriority.High => Loc.T("ModelGroups.Priority.High"),
        _ => Loc.T("ModelGroups.Priority.Normal")
    };
}

public sealed record ModelGroupManagerResult(
    IReadOnlyList<ModelGroupEditorRow> Groups,
    IReadOnlyDictionary<string, string> Assignments);

public sealed record LaunchProfileGroupChoice(
    string ProfileId,
    string ModelName,
    string ProfileName,
    string CurrentGroup,
    bool IsInSelectedGroup);

public static partial class ModelGroupDialogFactory
{
    private sealed record EnumChoice<T>(T Value, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record AssignmentChoice(string Id, string Label)
    {
        public override string ToString() => Label;
    }

    private sealed record ProfileMembershipChange(
        IReadOnlyList<string> ProfileIds,
        bool Remove);
}
