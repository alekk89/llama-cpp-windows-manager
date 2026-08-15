namespace LocalLlmConsole.Services;

public enum HelpNavigationDestination
{
    None,
    Overview,
    Models,
    Runtimes,
    Windows,
    WslLinux,
    Settings,
    Logs,
    Lifetime,
    Updates
}

public enum HelpNavigationFocusTarget
{
    None,
    LoadedSessionsGrid,
    ModelsGrid,
    HuggingFaceQueryBox,
    ModelCombo,
    LogsGrid
}

public sealed record HelpNavigationPlan(
    HelpNavigationDestination Destination,
    HelpNavigationFocusTarget FocusTarget,
    string StatusMessage)
{
    public bool ShouldNavigate => Destination != HelpNavigationDestination.None;
}

public sealed class HelpNavigationApplicationService
{
    public HelpNavigationPlan Plan(string? target)
        => Normalize(target) switch
        {
            "overview" => Overview(
                HelpNavigationFocusTarget.None,
                "Help: Overview is the model loading dashboard."),
            "loaded-sessions" => Overview(
                HelpNavigationFocusTarget.LoadedSessionsGrid,
                "Help: click a Loaded Model Sessions row to open Model Status for that model."),
            "models" => Models(
                HelpNavigationFocusTarget.ModelsGrid,
                "Help: local models, downloads, and launch settings live on Models."),
            "runtime-download" => Runtimes(
                HelpNavigationFocusTarget.None,
                "Help: in Runtime Downloads, choose the official Windows or WSL package for CUDA, CPU, Vulkan, or Intel Arc SYCL, then click Install."),
            "runtime-jobs" => Logs(
                "Help: Logs shows runtime download, source build, and setup job output."),
            "windows-tools" => Windows(
                "Help: Windows tools are advanced setup actions for native builds and toolchains."),
            "wsl-tools" => WslLinux(
                "Help: WSL Linux tools are advanced setup actions for Linux runtimes and source builds."),
            "model-download" => Models(
                HelpNavigationFocusTarget.HuggingFaceQueryBox,
                "Help: search Hugging Face, then click Download on the selected model row."),
            "launch-settings" => Models(
                HelpNavigationFocusTarget.ModelsGrid,
                "Help: select a model, tune launch settings, enter a profile name, then click Save New Profile."),
            "overview-load" => Overview(
                HelpNavigationFocusTarget.ModelCombo,
                "Help: choose a model and launch profile, then click Load. Running models can be unloaded from their session row."),
            "settings" => Settings(
                "Help: Settings stores app preferences, network behavior, secrets, gateway options, and log limits."),
            "gateway-settings" => Settings(
                "Help: Network settings include Auto-load gateway, Gateway port, and Gateway policy."),
            "logs" => Logs(
                "Help: open logs to inspect app, model runtime, and runtime job output."),
            "lifetime" => Lifetime(
                "Help: Lifetime shows persisted token counters by model session."),
            "updates" => Updates(
                "Help: Check For Updates looks for new app releases."),
            _ => new HelpNavigationPlan(
                HelpNavigationDestination.None,
                HelpNavigationFocusTarget.None,
                "")
        };

    private static string Normalize(string? target)
        => (target ?? "").Trim().ToLowerInvariant();

    private static HelpNavigationPlan Overview(HelpNavigationFocusTarget focus, string status)
        => new(HelpNavigationDestination.Overview, focus, status);

    private static HelpNavigationPlan Models(HelpNavigationFocusTarget focus, string status)
        => new(HelpNavigationDestination.Models, focus, status);

    private static HelpNavigationPlan Runtimes(HelpNavigationFocusTarget focus, string status)
        => new(HelpNavigationDestination.Runtimes, focus, status);

    private static HelpNavigationPlan Windows(string status)
        => new(HelpNavigationDestination.Windows, HelpNavigationFocusTarget.None, status);

    private static HelpNavigationPlan WslLinux(string status)
        => new(HelpNavigationDestination.WslLinux, HelpNavigationFocusTarget.None, status);

    private static HelpNavigationPlan Settings(string status)
        => new(HelpNavigationDestination.Settings, HelpNavigationFocusTarget.None, status);

    private static HelpNavigationPlan Logs(string status)
        => new(HelpNavigationDestination.Logs, HelpNavigationFocusTarget.LogsGrid, status);

    private static HelpNavigationPlan Lifetime(string status)
        => new(HelpNavigationDestination.Lifetime, HelpNavigationFocusTarget.None, status);

    private static HelpNavigationPlan Updates(string status)
        => new(HelpNavigationDestination.Updates, HelpNavigationFocusTarget.None, status);
}
