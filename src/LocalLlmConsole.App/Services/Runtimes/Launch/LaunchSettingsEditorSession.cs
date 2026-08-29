namespace LocalLlmConsole.Services;

public sealed class LaunchSettingsEditorSession
{
    private string _saveAsNewSourceId = "";

    public string ModelId { get; private set; } = "";

    public string ProfileId { get; private set; } = "";

    public ModelLaunchSettings? SavedProfile { get; private set; }

    public bool HasSavedProfile { get; private set; }

    public bool IsProgrammaticUpdate { get; private set; }

    public void Clear()
    {
        ModelId = "";
        ProfileId = "";
        SavedProfile = null;
        HasSavedProfile = false;
    }

    public void Load(ModelLaunchSettingsViewState viewState)
    {
        ArgumentNullException.ThrowIfNull(viewState);
        ModelId = viewState.ModelId;
        ProfileId = viewState.ProfileId;
        SavedProfile = viewState.SavedProfile;
        HasSavedProfile = viewState.HasSavedProfile;
    }

    public void MarkSaved(string modelId, string profileId, ModelLaunchSettings savedProfile)
    {
        ModelId = modelId ?? "";
        ProfileId = profileId ?? "";
        SavedProfile = savedProfile ?? throw new ArgumentNullException(nameof(savedProfile));
        HasSavedProfile = true;
    }

    public bool IsLoadedFor(string modelId, string profileId = "")
        => string.Equals(ModelId, modelId, StringComparison.OrdinalIgnoreCase)
           && string.Equals(ProfileId, profileId ?? "", StringComparison.OrdinalIgnoreCase);

    public bool TryChangeSaveAsNewSource(ModelRecord? model, string profileId = "")
    {
        var nextSourceModelId = $"{model?.Id ?? ""}|{profileId ?? ""}";
        if (string.Equals(_saveAsNewSourceId, nextSourceModelId, StringComparison.OrdinalIgnoreCase))
            return false;

        _saveAsNewSourceId = nextSourceModelId;
        return true;
    }

    public void RunProgrammaticUpdate(Action update)
    {
        ArgumentNullException.ThrowIfNull(update);

        IsProgrammaticUpdate = true;
        try
        {
            update();
        }
        finally
        {
            IsProgrammaticUpdate = false;
        }
    }
}
