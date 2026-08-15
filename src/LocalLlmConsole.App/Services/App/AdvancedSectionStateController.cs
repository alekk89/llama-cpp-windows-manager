namespace LocalLlmConsole.Services;

public sealed class AdvancedSectionStateController
{
    public bool ShowLaunchSettings { get; private set; }

    public void SetLaunchSettings(bool show)
        => ShowLaunchSettings = show;

}
