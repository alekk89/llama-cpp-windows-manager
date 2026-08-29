namespace LocalLlmConsole.Services;

public sealed record LaunchSettingsSaveStateRequest(
    ModelRecord? SelectedModel,
    bool HasSavedProfile,
    ModelLaunchSettings? SavedProfile,
    bool CurrentProfileReadable,
    ModelLaunchSettings? CurrentProfile,
    string RequestedVariantName);

public sealed record LaunchSettingsSaveState(
    string SaveForModelContent,
    bool CanSaveForModel,
    bool CanSaveAsNewVariant);

public static class LaunchSettingsSaveStateService
{
    public const string SaveForModelText = "Save For Model";
    public const string SavedText = "Saved";

    public static LaunchSettingsSaveState Evaluate(LaunchSettingsSaveStateRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var canSaveAsNewVariant = CanSaveAsNewVariant(request.SelectedModel, request.RequestedVariantName);
        if (request.SelectedModel is null)
            return new LaunchSettingsSaveState(SaveForModelText, false, canSaveAsNewVariant);

        if (!request.HasSavedProfile || request.SavedProfile is null)
            return new LaunchSettingsSaveState(SaveForModelText, true, canSaveAsNewVariant);

        if (!request.CurrentProfileReadable || request.CurrentProfile is null)
            return new LaunchSettingsSaveState(SaveForModelText, true, canSaveAsNewVariant);

        var currentMatchesSavedProfile = Equals(request.CurrentProfile, request.SavedProfile);
        return new LaunchSettingsSaveState(
            currentMatchesSavedProfile ? SavedText : SaveForModelText,
            !currentMatchesSavedProfile,
            canSaveAsNewVariant);
    }

    public static bool CanSaveAsNewVariant(ModelRecord? selectedModel, string requestedVariantName)
    {
        if (selectedModel is null) return false;

        var normalizedName = (requestedVariantName ?? "").Trim();
        return !string.IsNullOrWhiteSpace(normalizedName);
    }
}
