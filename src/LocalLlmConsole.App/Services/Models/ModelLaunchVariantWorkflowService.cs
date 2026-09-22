namespace LocalLlmConsole.Services;

public sealed record ModelLaunchVariantWorkflowRequest(
    ModelRecord SourceModel,
    string RequestedName,
    AppSettings LaunchSettings,
    string RuntimeId,
    AppSettings Defaults);

public sealed record ModelLaunchProfileCopyRequest(
    ModelRecord SourceModel,
    ModelRecord TargetModel,
    NamedModelLaunchProfile SourceProfile,
    string RequestedName,
    string RuntimeId,
    AppSettings Defaults);

public sealed record ModelLaunchVariantWorkflowResult(
    bool Success,
    string StatusMessage,
    NamedModelLaunchProfile? Profile = null,
    ModelLaunchSettings? SavedSettings = null)
{
    public int Port => SavedSettings?.Port ?? 0;
}

public sealed class ModelLaunchVariantWorkflowService
{
    private readonly ModelLaunchProfileService _launchProfiles;

    public ModelLaunchVariantWorkflowService(ModelLaunchProfileService launchProfiles)
    {
        _launchProfiles = launchProfiles ?? throw new ArgumentNullException(nameof(launchProfiles));
    }

    public async Task<ModelLaunchVariantWorkflowResult> SaveAsNewAsync(
        ModelLaunchVariantWorkflowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedName = (request.RequestedName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requestedName))
            return Failed("Enter a name for the launch profile.");

        var existing = await _launchProfiles.ListNamedAsync(request.SourceModel);
        if (existing.Any(profile => string.Equals(profile.Name, requestedName, StringComparison.OrdinalIgnoreCase)))
            return Failed($"A launch profile named {requestedName} already exists for {request.SourceModel.Name}.");

        NamedModelLaunchProfile? profile = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profileId = $"profile-{Guid.NewGuid():N}";
            var profilePort = await _launchProfiles.NextAvailablePortAsync(request.SourceModel.Id, request.Defaults, profileId);
            cancellationToken.ThrowIfCancellationRequested();

            var saved = ModelLaunchSettings.FromAppSettings(
                request.LaunchSettings with { Port = profilePort },
                request.RuntimeId);
            profile = new NamedModelLaunchProfile(
                profileId,
                request.SourceModel.Id,
                requestedName,
                saved,
                DateTimeOffset.UtcNow);
            await _launchProfiles.SaveNamedAsync(profile);
            return new ModelLaunchVariantWorkflowResult(
                true,
                $"Saved launch profile {profile.Name} for {request.SourceModel.Name} on port {profilePort}.",
                profile,
                saved);
        }
        catch (OperationCanceledException) when (profile is not null)
        {
            await TryRemoveIncompleteProfileAsync(profile);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            if (profile is not null)
                await TryRemoveIncompleteProfileAsync(profile);
            return Failed(ex.Message);
        }
        catch (Exception) when (profile is not null)
        {
            await TryRemoveIncompleteProfileAsync(profile);
            throw;
        }
    }

    public async Task<ModelLaunchVariantWorkflowResult> CopyProfileAsync(
        ModelLaunchProfileCopyRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var requestedName = (request.RequestedName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requestedName))
            return Failed("Enter a name for the launch profile.");

        var existing = await _launchProfiles.ListNamedAsync(request.TargetModel);
        if (existing.Any(profile => string.Equals(profile.Name, requestedName, StringComparison.OrdinalIgnoreCase)))
            return Failed($"A launch profile named {requestedName} already exists for {request.TargetModel.Name}.");

        NamedModelLaunchProfile? profile = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var profileId = $"profile-{Guid.NewGuid():N}";
            var profilePort = await _launchProfiles.NextAvailablePortAsync(request.TargetModel.Id, request.Defaults, profileId);
            cancellationToken.ThrowIfCancellationRequested();

            var saved = request.SourceProfile.Settings with
            {
                VisionProjectorPath = "",
                SpecDraftModelPath = "",
                MtpHeadPath = "",
                RuntimeId = request.RuntimeId,
                Port = profilePort
            };
            profile = new NamedModelLaunchProfile(
                profileId,
                request.TargetModel.Id,
                requestedName,
                saved,
                DateTimeOffset.UtcNow);
            await _launchProfiles.SaveNamedAsync(profile);
            return new ModelLaunchVariantWorkflowResult(
                true,
                $"Saved launch profile {profile.Name} for {request.TargetModel.Name} on port {profilePort}.",
                profile,
                saved);
        }
        catch (OperationCanceledException) when (profile is not null)
        {
            await TryRemoveIncompleteProfileAsync(profile);
            throw;
        }
        catch (InvalidOperationException ex)
        {
            if (profile is not null)
                await TryRemoveIncompleteProfileAsync(profile);
            return Failed(ex.Message);
        }
        catch (Exception) when (profile is not null)
        {
            await TryRemoveIncompleteProfileAsync(profile);
            throw;
        }
    }

    private static ModelLaunchVariantWorkflowResult Failed(string message)
        => new(false, message);

    private async Task TryRemoveIncompleteProfileAsync(NamedModelLaunchProfile profile)
    {
        try { await _launchProfiles.DeleteNamedAsync(profile.Id); }
        catch (Exception ex)
        {
            Trace.TraceWarning($"Could not remove incomplete launch profile {profile.Id}: {ex.Message}");
        }
    }
}
