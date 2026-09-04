using System.Collections.Concurrent;

namespace LocalLlmConsole.Services;

internal sealed record ControlEndpointContext(
    LocalControlDependencies Dependencies,
    ModelGroupService ModelGroups)
{
    public ControlAppSettingsMutationService SettingsMutations { get; } = new();
    public ConcurrentDictionary<string, SemaphoreSlim> ModelOperationGates { get; } = new(StringComparer.OrdinalIgnoreCase);
}

internal abstract class ControlEndpointHandler
{
    protected static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    protected ControlEndpointHandler(ControlEndpointContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }

    protected ControlEndpointContext Context { get; }
    protected LocalControlDependencies _deps => Context.Dependencies;
    protected ModelGroupService _modelGroups => Context.ModelGroups;
    protected ControlAppSettingsMutationService _settingsMutations => Context.SettingsMutations;
    protected ConcurrentDictionary<string, SemaphoreSlim> _modelOperationGates => Context.ModelOperationGates;

    protected async Task<ModelRecord> ResolveModelAsync(string identifier)
    {
        var models = await _deps.StateStore.ListModelsAsync();
        return ModelGatewayRequestResolver.ResolveModel(models, identifier)
            ?? throw new KeyNotFoundException($"Model '{identifier}' was not found. Use GET /api/v1/models to list registered identifiers.");
    }

    protected LoadedModelSessionSnapshot ResolveSession(string identifier)
    {
        var sessions = _deps.Sessions.Snapshots();
        var exact = sessions.FirstOrDefault(session =>
            session.SessionId.Equals(identifier, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        var matches = sessions.Where(session =>
                session.ModelId.Equals(identifier, StringComparison.OrdinalIgnoreCase)
                || session.ModelName.Equals(identifier, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            > 1 => throw new InvalidOperationException(
                $"More than one session serves model '{identifier}'. Use the exact session id."),
            _ => throw new KeyNotFoundException($"Session '{identifier}' was not found.")
        };
    }

    protected async Task SaveProfileAsync(ModelRecord model, NamedModelLaunchProfile profile)
    {
        _ = RuntimeVulkanEnvironment.Value(RuntimeBackend.Vulkan, profile.Settings.VulkanAllocationBlockSizeMiB);
        var profiles = await _deps.LaunchProfiles.ListNamedAsync(model);
        if (profile.IsDefault)
        {
            foreach (var other in profiles.Where(other => other.IsDefault && !other.Id.Equals(profile.Id, StringComparison.OrdinalIgnoreCase)))
                await _deps.LaunchProfiles.SaveNamedAsync(other with { IsDefault = false, UpdatedAt = DateTimeOffset.UtcNow });
        }
        await _deps.LaunchProfiles.SaveNamedAsync(profile);
    }

    protected static NamedModelLaunchProfile? ResolveProfile(
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        string profileId,
        string profileName)
    {
        if (!string.IsNullOrWhiteSpace(profileId))
            return profiles.FirstOrDefault(profile => profile.Id.Equals(profileId, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Launch profile '{profileId}' was not found.");
        if (!string.IsNullOrWhiteSpace(profileName))
            return profiles.FirstOrDefault(profile => profile.Name.Equals(profileName, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException($"Launch profile '{profileName}' was not found.");
        return profiles.FirstOrDefault(profile => profile.IsDefault);
    }

    protected static RuntimeRecord? ResolveRuntime(IReadOnlyList<RuntimeRecord> runtimes, string runtimeId)
        => string.IsNullOrWhiteSpace(runtimeId)
            ? runtimes.FirstOrDefault(runtime => RuntimeAvailabilityService.IsAvailable(runtime)) ?? runtimes.FirstOrDefault()
            : runtimes.FirstOrDefault(runtime => runtime.Id.Equals(runtimeId, StringComparison.OrdinalIgnoreCase)
                || runtime.Name.Equals(runtimeId, StringComparison.OrdinalIgnoreCase));

    protected static ModelLaunchSettings ProfileSettings(ModelLaunchSettings source, JsonObject? settings, bool replace)
    {
        if (!replace) return ControlJsonPatch.Apply(source, settings);
        if (settings is null) throw new InvalidOperationException("Replacing profile settings requires a complete settings object.");
        try
        {
            return settings.Deserialize<ModelLaunchSettings>(JsonOptions)
                ?? throw new InvalidOperationException("Profile settings were empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid complete profile settings: {ex.Message}", ex);
        }
    }

    protected static object ModelView(
        ModelRecord model,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        ModelGroupSnapshot? groupSnapshot = null,
        int globalIdleMinutes = 0)
        => new
        {
            model.Id,
            model.Name,
            model.ModelPath,
            ownership = model.Ownership.ToString(),
            metadata = ParseJson(model.MetadataJson),
            model.UpdatedAt,
            profiles = profiles.Select(profile => ProfileView(profile, groupSnapshot, globalIdleMinutes)).ToArray()
        };

    protected static object ProfileView(
        NamedModelLaunchProfile profile,
        ModelGroupSnapshot? snapshot = null,
        int globalIdleMinutes = 0)
        => new
        {
            profile.Id,
            profile.ModelId,
            profile.Name,
            profile.Settings,
            profile.UpdatedAt,
            profile.IsDefault,
            group = ModelGroupDetails(snapshot?.GroupForProfile(profile.Id)),
            effectivePolicy = snapshot is null
                ? null
                : ModelGroupPolicyView(ModelGroupService.EffectivePolicy(snapshot, profile.Id, globalIdleMinutes))
        };

    protected static object ModelGroupView(ModelGroupRecord group, ModelGroupSnapshot snapshot)
    {
        var profileIds = snapshot.Assignments.Values
            .Where(assignment => assignment.GroupId.Equals(group.Id, StringComparison.OrdinalIgnoreCase))
            .Select(assignment => assignment.LaunchProfileId)
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new
        {
            group.Id,
            group.Name,
            retentionMode = group.RetentionMode.ToString(),
            group.IdleMinutes,
            evictionPriority = group.EvictionPriority.ToString(),
            group.UpdatedAt,
            profileCount = profileIds.Length,
            profileIds
        };
    }

    protected static object? ModelGroupDetails(ModelGroupRecord? group)
        => group is null ? null : new
        {
            group.Id,
            group.Name,
            retentionMode = group.RetentionMode.ToString(),
            group.IdleMinutes,
            evictionPriority = group.EvictionPriority.ToString(),
            group.UpdatedAt
        };

    protected static object ModelGroupPolicyView(EffectiveModelRetentionPolicy policy)
        => new
        {
            policy.AllowsIdleUnload,
            policy.IdleMinutes,
            evictionPriority = policy.EvictionPriority.ToString(),
            policy.GroupId,
            policy.GroupName
        };

    internal static object SessionView(LoadedModelSessionSnapshot session)
        => new
        {
            session.SessionId,
            session.ModelId,
            session.ModelName,
            session.RuntimeId,
            session.RuntimeName,
            mode = session.Mode.ToString(),
            backend = session.Backend.ToString(),
            status = session.Status.ToString(),
            session.IsRunning,
            session.IsSelected,
            session.ProcessId,
            session.StartedAt,
            session.StoppedAt,
            session.Endpoint,
            session.EndpointHealth,
            session.StatusReason,
            session.LogPath,
            session.LaunchProfileId,
            session.LaunchProfileName,
            settings = ModelLaunchSettings.FromAppSettings(session.LaunchSettings, session.RuntimeId)
        };

    internal static object SettingsSchema<T>()
        => typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Select(property => new
            {
                name = JsonNamingPolicy.CamelCase.ConvertName(property.Name),
                clrName = property.Name,
                type = FriendlyType(property.PropertyType)
            }).ToArray();

    private static string FriendlyType(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying == typeof(string)) return "string";
        if (underlying == typeof(bool)) return "boolean";
        if (underlying == typeof(int) || underlying == typeof(long)) return "integer";
        if (underlying == typeof(double) || underlying == typeof(decimal)) return "number";
        return underlying.IsEnum ? $"enum:{string.Join('|', Enum.GetNames(underlying))}" : underlying.Name;
    }

    protected static JsonNode? ParseJson(string json)
    {
        try { return JsonNode.Parse(json); }
        catch { return JsonValue.Create(json); }
    }

    protected static T Body<T>(JsonObject? body)
        => (body ?? new JsonObject()).Deserialize<T>(JsonOptions)
            ?? throw new InvalidOperationException($"Request body could not be read as {typeof(T).Name}.");

    protected static string RequiredString(JsonObject? body, string name)
    {
        var value = body?[name]?.ToString()?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(value)) throw new InvalidOperationException($"'{name}' is required.");
        return value;
    }

    protected static TEnum EnumRequest<TEnum>(string value, string field)
        where TEnum : struct, Enum
    {
        var normalized = (value ?? "").Replace("-", "", StringComparison.Ordinal).Replace("_", "", StringComparison.Ordinal).Trim();
        foreach (var name in Enum.GetNames<TEnum>())
        {
            if (name.Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return Enum.Parse<TEnum>(name);
        }
        throw new InvalidOperationException($"'{field}' must be one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
    }

    protected static bool BoolQuery(IReadOnlyDictionary<string, string> query, string name)
        => query.TryGetValue(name, out var value) && bool.TryParse(value, out var parsed) && parsed;

    protected static int IntQuery(IReadOnlyDictionary<string, string> query, string name, int fallback, int min, int max)
        => TryQueryInt(query, name, out var value) ? Math.Clamp(value, min, max) : fallback;

    protected static bool TryQueryInt(IReadOnlyDictionary<string, string> query, string name, out int value)
    {
        value = 0;
        return query.TryGetValue(name, out var text)
            && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    protected static LocalControlApiResponse Ok(object body) => ControlApiResponses.Ok(body);
    protected static LocalControlApiResponse Error(int status, string error) => ControlApiResponses.Error(status, error);
}

internal static class ControlApiResponses
{
    public static LocalControlApiResponse Ok(object body) => new(200, body);
    public static LocalControlApiResponse Error(int status, string error) => new(status, new { ok = false, error });
}
