namespace LocalLlmConsole.Services;

public sealed record ModelGroupEditDefinition(
    string EditorKey,
    string Id,
    string Name,
    ModelGroupRetentionMode RetentionMode,
    int IdleMinutes,
    ModelGroupEvictionPriority EvictionPriority);

public sealed class ModelGroupService
{
    public const int MinimumIdleMinutes = 1;
    public const int MaximumIdleMinutes = 10080;
    public const int DefaultIdleMinutes = 30;

    private readonly StateStore _stateStore;
    private readonly object _policyCacheLock = new();
    private ModelGroupSnapshot? _policySnapshot;
    private DateTimeOffset _policySnapshotExpiresAt;

    public ModelGroupService(StateStore stateStore)
    {
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
    }

    public Task<ModelGroupSnapshot> SnapshotAsync()
        => _stateStore.GetModelGroupSnapshotAsync();

    public async Task<ModelGroupSnapshot> PolicySnapshotAsync(DateTimeOffset now)
    {
        lock (_policyCacheLock)
        {
            if (_policySnapshot is not null && now < _policySnapshotExpiresAt)
                return _policySnapshot;
        }

        var loaded = await SnapshotAsync();
        lock (_policyCacheLock)
        {
            _policySnapshot = loaded;
            _policySnapshotExpiresAt = now.AddSeconds(5);
            return loaded;
        }
    }

    public async Task<ModelGroupRecord> CreateAsync(
        string name,
        ModelGroupRetentionMode retentionMode = ModelGroupRetentionMode.Inherit,
        int idleMinutes = DefaultIdleMinutes,
        ModelGroupEvictionPriority evictionPriority = ModelGroupEvictionPriority.Normal)
    {
        var snapshot = await SnapshotAsync();
        var normalizedName = ValidateName(name);
        EnsureUniqueName(snapshot.Groups, normalizedName);
        ValidatePolicy(retentionMode, idleMinutes, evictionPriority);
        var group = new ModelGroupRecord(
            $"group:{Guid.NewGuid():N}",
            normalizedName,
            retentionMode,
            idleMinutes,
            evictionPriority,
            DateTimeOffset.UtcNow);
        await _stateStore.UpsertModelGroupAsync(group);
        InvalidatePolicySnapshot();
        return group;
    }

    public async Task<ModelGroupRecord> UpdateAsync(
        string identifier,
        string name,
        ModelGroupRetentionMode retentionMode,
        int idleMinutes,
        ModelGroupEvictionPriority evictionPriority)
    {
        var snapshot = await SnapshotAsync();
        var existing = Resolve(snapshot, identifier);
        var normalizedName = ValidateName(name);
        EnsureUniqueName(snapshot.Groups, normalizedName, existing.Id);
        ValidatePolicy(retentionMode, idleMinutes, evictionPriority);
        var updated = existing with
        {
            Name = normalizedName,
            RetentionMode = retentionMode,
            IdleMinutes = idleMinutes,
            EvictionPriority = evictionPriority,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        await _stateStore.UpsertModelGroupAsync(updated);
        InvalidatePolicySnapshot();
        return updated;
    }

    public async Task DeleteAsync(string identifier)
    {
        var group = Resolve(await SnapshotAsync(), identifier);
        await _stateStore.DeleteModelGroupAsync(group.Id);
        InvalidatePolicySnapshot();
    }

    public async Task<ModelGroupAssignment> AssignAsync(string launchProfileId, string groupIdentifier)
    {
        var profile = await _stateStore.GetNamedModelLaunchProfileAsync(launchProfileId)
            ?? throw new KeyNotFoundException($"Launch profile '{launchProfileId}' was not found.");
        var group = Resolve(await SnapshotAsync(), groupIdentifier);
        var assignment = new ModelGroupAssignment(profile.Id, group.Id, DateTimeOffset.UtcNow);
        await _stateStore.AssignLaunchProfileGroupAsync(assignment);
        InvalidatePolicySnapshot();
        return assignment;
    }

    public async Task UnassignAsync(string launchProfileId)
    {
        await _stateStore.UnassignLaunchProfileGroupAsync(launchProfileId);
        InvalidatePolicySnapshot();
    }

    public async Task<ModelGroupSnapshot> ReplaceAsync(
        IReadOnlyList<ModelGroupEditDefinition> edits,
        IReadOnlyDictionary<string, string> assignments)
    {
        ArgumentNullException.ThrowIfNull(edits);
        ArgumentNullException.ThrowIfNull(assignments);
        var snapshot = await SnapshotAsync();
        var existingById = snapshot.Groups.ToDictionary(group => group.Id, StringComparer.OrdinalIgnoreCase);
        var editorKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var groupIdsByEditorKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var groups = new List<ModelGroupRecord>(edits.Count);
        var now = DateTimeOffset.UtcNow;

        foreach (var edit in edits)
        {
            var editorKey = (edit.EditorKey ?? "").Trim();
            if (editorKey.Length == 0 || !editorKeys.Add(editorKey))
                throw new InvalidOperationException("Every model group edit must have a unique editor key.");
            var name = ValidateName(edit.Name);
            if (!groupNames.Add(name))
                throw new InvalidOperationException($"A model group named '{name}' already exists.");
            ValidatePolicy(edit.RetentionMode, edit.IdleMinutes, edit.EvictionPriority);

            var id = (edit.Id ?? "").Trim();
            if (id.Length == 0)
                id = $"group:{Guid.NewGuid():N}";
            else if (!existingById.ContainsKey(id))
                throw new KeyNotFoundException($"Model group '{id}' was not found.");
            if (!groupIds.Add(id))
                throw new InvalidOperationException($"Model group '{id}' appears more than once.");

            groups.Add(new ModelGroupRecord(
                id,
                name,
                edit.RetentionMode,
                edit.IdleMinutes,
                edit.EvictionPriority,
                now));
            groupIdsByEditorKey[editorKey] = id;
        }

        var profileIds = (await _stateStore.ListNamedModelLaunchProfilesAsync())
            .Select(profile => profile.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var desiredAssignments = new List<ModelGroupAssignment>(assignments.Count);
        foreach (var (profileId, editorKey) in assignments)
        {
            if (!profileIds.Contains(profileId))
                throw new KeyNotFoundException($"Launch profile '{profileId}' was not found.");
            if (!groupIdsByEditorKey.TryGetValue(editorKey, out var groupId))
                throw new KeyNotFoundException($"The selected model group '{editorKey}' was not found.");
            desiredAssignments.Add(new ModelGroupAssignment(profileId, groupId, now));
        }

        await _stateStore.ReplaceModelGroupsAsync(groups, desiredAssignments);
        InvalidatePolicySnapshot();
        return await SnapshotAsync();
    }

    public static ModelGroupRecord Resolve(ModelGroupSnapshot snapshot, string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new InvalidOperationException("A model group id or name is required.");
        var normalized = identifier.Trim();
        return snapshot.Groups.FirstOrDefault(group =>
                   group.Id.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? snapshot.Groups.FirstOrDefault(group =>
                   group.Name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
               ?? throw new KeyNotFoundException($"Model group '{identifier}' was not found.");
    }

    public static EffectiveModelRetentionPolicy EffectivePolicy(
        ModelGroupSnapshot snapshot,
        string launchProfileId,
        int globalIdleMinutes)
    {
        var group = snapshot.GroupForProfile(launchProfileId);
        if (group is null)
            return FromGlobal(globalIdleMinutes);
        return group.RetentionMode switch
        {
            ModelGroupRetentionMode.Pinned => new(false, 0, group.EvictionPriority, group.Id, group.Name),
            ModelGroupRetentionMode.IdleTimeout => new(true, group.IdleMinutes, group.EvictionPriority, group.Id, group.Name),
            _ => FromGlobal(globalIdleMinutes) with
            {
                EvictionPriority = group.EvictionPriority,
                GroupId = group.Id,
                GroupName = group.Name
            }
        };
    }

    private static EffectiveModelRetentionPolicy FromGlobal(int globalIdleMinutes)
        => new(
            globalIdleMinutes > 0,
            Math.Clamp(globalIdleMinutes, 0, MaximumIdleMinutes),
            ModelGroupEvictionPriority.Normal);

    private static string ValidateName(string name)
    {
        var normalized = (name ?? "").Trim();
        if (normalized.Length == 0) throw new InvalidOperationException("Model group name is required.");
        if (normalized.Length > 80) throw new InvalidOperationException("Model group name must be 80 characters or fewer.");
        return normalized;
    }

    private static void EnsureUniqueName(
        IReadOnlyList<ModelGroupRecord> groups,
        string name,
        string exceptId = "")
    {
        if (groups.Any(group =>
                !group.Id.Equals(exceptId, StringComparison.OrdinalIgnoreCase)
                && group.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"A model group named '{name}' already exists.");
    }

    private static void ValidatePolicy(
        ModelGroupRetentionMode retentionMode,
        int idleMinutes,
        ModelGroupEvictionPriority evictionPriority)
    {
        if (!Enum.IsDefined(retentionMode)) throw new InvalidOperationException("Unknown model group retention mode.");
        if (!Enum.IsDefined(evictionPriority)) throw new InvalidOperationException("Unknown model group eviction priority.");
        if (idleMinutes is < MinimumIdleMinutes or > MaximumIdleMinutes)
            throw new InvalidOperationException($"Model group idle minutes must be between {MinimumIdleMinutes} and {MaximumIdleMinutes}.");
    }

    private void InvalidatePolicySnapshot()
    {
        lock (_policyCacheLock)
        {
            _policySnapshot = null;
            _policySnapshotExpiresAt = default;
        }
    }
}
