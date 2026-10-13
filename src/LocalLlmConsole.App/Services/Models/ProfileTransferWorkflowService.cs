namespace LocalLlmConsole.Services;

public sealed partial class ProfileTransferWorkflowService
{
    private readonly StateStore _store;
    private readonly LoadedModelSessionManager _sessions;
    private readonly Func<Task<IReadOnlyList<string>>> _gpuNames;

    public ProfileTransferWorkflowService(StateStore store, LoadedModelSessionManager sessions,
        Func<Task<IReadOnlyList<string>>>? gpuNames = null)
    {
        _store = store;
        _sessions = sessions;
        _gpuNames = gpuNames ?? (() => Task.FromResult<IReadOnlyList<string>>([]));
    }

    public async Task<ProfileBundle> ExportAsync(ModelRecord model, string? profileId = null)
    {
        var profiles = await _store.ListNamedModelLaunchProfilesAsync(model.Id);
        if (!string.IsNullOrEmpty(profileId)) profiles = profiles.Where(p => p.Id == profileId).ToArray();
        if (profiles.Count == 0) throw new InvalidOperationException("No saved profiles were found for this model.");
        var runtimes = await _store.ListRuntimesAsync();
        var gpuNames = await _gpuNames();
        return new ProfileBundle(ProfileBundleCodec.SchemaVersion, ModelHint(model), profiles.Select(profile =>
        {
            var runtime = runtimes.FirstOrDefault(r => r.Id == profile.Settings.RuntimeId);
            return new PortableLaunchProfile(profile.Name, ProfileBundleCodec.PortableSettings(profile.Settings),
                runtime is null ? null : new ProfileRuntimeHint(runtime.Name, runtime.Backend.ToString(), runtime.Mode.ToString()),
                profile.Annotations, gpuNames);
        }).ToArray());
    }

    public async Task<ProfileImportPreview> PreviewAsync(ModelRecord model, ProfileBundle bundle, AppSettings defaults,
        IReadOnlyList<ProfileImportChoice>? choices = null)
    {
        // Round-tripping the external contract also validates in-process copy requests.
        bundle = ProfileBundleCodec.Read(ProfileBundleCodec.Write(bundle));
        if (!(await _store.ListModelsAsync()).Any(candidate => candidate.Id == model.Id))
            throw new InvalidOperationException("The target model no longer exists.");
        var revision = _store.CatalogRevision;
        var allProfiles = await _store.ListNamedModelLaunchProfilesAsync();
        var existing = allProfiles.Where(p => p.ModelId == model.Id).ToArray();
        var runtimes = await _store.ListRuntimesAsync();
        var gpuNames = await _gpuNames();
        var usedPorts = allProfiles.Select(p => p.Settings.Port).Concat(_sessions.Snapshots().Select(s => s.LaunchSettings.Port))
            .Append(defaults.AutoLoadGatewayPort).Where(port => port > 0).ToHashSet();
        var usedNames = existing.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var replaced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var rows = new List<ProfileImportRow>();
        if (choices?.GroupBy(c => c.Index).Any(group => group.Count() > 1) == true
            || choices?.Any(c => c.Index < 0 || c.Index >= bundle.Profiles.Count || !Enum.IsDefined(c.Action)) == true)
            throw new InvalidOperationException("Invalid or duplicate import choices.");
        for (var index = 0; index < bundle.Profiles.Count; index++)
        {
            var source = bundle.Profiles[index];
            var choice = choices?.FirstOrDefault(c => c.Index == index) ?? new ProfileImportChoice(index);
            if (choice.Action == ProfileConflictAction.Skip) continue;
            var name = (choice.Name ?? source.Name).Trim();
            var match = existing.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            var replace = choice.Action == ProfileConflictAction.Replace ? match : null;
            var warnings = new List<string>();
            var errors = new List<string>();
            if (name.Length is < 1 or > 200) errors.Add("Enter a profile name between 1 and 200 characters.");
            if (replace is not null && !replaced.Add(replace.Id)) errors.Add("Two imported profiles cannot replace the same profile.");
            if (choice.Action == ProfileConflictAction.KeepBoth)
            {
                var stem = name;
                for (var suffix = 2; usedNames.Contains(name); suffix++) name = $"{stem} ({suffix})";
            }
            else if (replace is null && usedNames.Contains(name)) errors.Add("Duplicate name in this import.");
            usedNames.Add(name);
            var settings = ModelLaunchSettings.FromAppSettings(defaults);
            try
            {
                settings = ProfileBundleCodec.ApplySettings(settings, source.Settings, portable: true);
                settings = ResolveSettings(model, bundle.Model, source, choice, settings, runtimes, gpuNames, warnings, errors);
                _ = RuntimeVulkanEnvironment.Value(RuntimeBackend.Vulkan, settings.VulkanAllocationBlockSizeMiB);
                var availableOwnPort = replace is not null && settings.Port == replace.Settings.Port
                    && !_sessions.Snapshots().Any(s => s.LaunchSettings.Port == settings.Port)
                    && !rows.Any(row => row.Settings.Port == settings.Port);
                if (settings.Port == 0 || (usedPorts.Contains(settings.Port) && !availableOwnPort))
                {
                    var port = ModelPortAllocator.NextAvailable(defaults.Port, usedPorts);
                    warnings.Add($"Port {settings.Port} is unavailable; using {port}.");
                    settings = settings with { Port = port };
                }
                usedPorts.Add(settings.Port);
            }
            catch (InvalidOperationException ex) { errors.Add(ex.Message); }
            rows.Add(new ProfileImportRow(index, name, replace?.Id, settings, source.Annotations, warnings, errors));
        }
        if (_store.CatalogRevision != revision) throw new InvalidOperationException("The catalog changed. Preview the import again.");
        return new ProfileImportPreview(revision, model.Id, rows);
    }

    public Task<IReadOnlyList<NamedModelLaunchProfile>> ApplyAsync(ModelRecord model, ProfileBundle bundle,
        AppSettings defaults, IReadOnlyList<ProfileImportChoice> choices, ProfileImportPreview reviewed)
        => _sessions.ExecuteLifecycleAsync(() => ApplyCoreAsync(model, bundle, defaults, choices, reviewed));

    private async Task<IReadOnlyList<NamedModelLaunchProfile>> ApplyCoreAsync(ModelRecord model, ProfileBundle bundle,
        AppSettings defaults, IReadOnlyList<ProfileImportChoice> choices, ProfileImportPreview reviewed)
    {
        var current = await PreviewAsync(model, bundle, defaults, choices);
        if (!current.CanApply) throw new InvalidOperationException("Resolve import errors before applying.");
        if (reviewed.ModelId != model.Id || current.CatalogRevision != reviewed.CatalogRevision
            || JsonSerializer.Serialize(current.Rows) != JsonSerializer.Serialize(reviewed.Rows))
            throw new InvalidOperationException("The import changed after preview. Review it again.");
        var existing = await _store.ListNamedModelLaunchProfilesAsync(model.Id);
        var imported = current.Rows.Select((row, index) => new NamedModelLaunchProfile(
            row.ReplaceId ?? $"profile:{Guid.NewGuid():N}", model.Id, row.Name, row.Settings, DateTimeOffset.UtcNow,
            row.ReplaceId is not null ? existing.Any(p => p.Id == row.ReplaceId && p.IsDefault) : existing.Count == 0 && index == 0,
            row.Annotations ?? new ProfileAnnotations())).ToArray();
        await _store.ApplyProfileImportAsync(model.Id, current.CatalogRevision, imported);
        return imported;
    }

    private static ProfileModelHint ModelHint(ModelRecord model)
    {
        string architecture = "";
        try
        {
            var node = JsonNode.Parse(model.MetadataJson);
            architecture = node?["ggufArchitecture"]?.ToString() ?? node?["general.architecture"]?.ToString() ?? node?["architecture"]?.ToString() ?? "";
        }
        catch (JsonException) { }
        return new ProfileModelHint(model.Name, Path.GetFileName(model.ModelPath), architecture);
    }
}
