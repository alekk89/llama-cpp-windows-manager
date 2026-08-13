namespace LocalLlmConsole;

public partial class MainWindow
{
    private void SetActiveNavigation(string title)
    {
        foreach (var button in new[] { OverviewNavButton, ModelsNavButton, RuntimesNavButton, WindowsNavButton, WslLinuxNavButton, SettingsNavButton, LifetimeNavButton, LogsNavButton, UpdatesNavButton, HelpNavButton })
            button.Tag = null;

        var active = title switch
        {
            "Overview" => OverviewNavButton,
            "Models" => ModelsNavButton,
            "Runtimes" => RuntimesNavButton,
            "Windows" => WindowsNavButton,
            "WSL Linux" => WslLinuxNavButton,
            "Settings" => SettingsNavButton,
            "Lifetime" => LifetimeNavButton,
            "Logs" => LogsNavButton,
            "Updates" => UpdatesNavButton,
            "Help" => HelpNavButton,
            _ => null
        };
        if (active is not null) active.Tag = "Active";
    }

    private void ApplyStaticButtonToolTips()
    {
        SetButtonToolTip(MinimizeButton, "Minimize the app window.");
        SetButtonToolTip(MaximizeButton, "Maximize or restore the app window.");
        SetButtonToolTip(CloseButton, "Close the app. Running models and downloads will be handled safely.");
        SetButtonToolTip(OverviewNavButton, "Open the model loading dashboard.");
        SetButtonToolTip(ModelsNavButton, "Open local models, Hugging Face search, and launch settings.");
        SetButtonToolTip(RuntimesNavButton, "Open llama.cpp source downloads, builds, and runtime jobs.");
        SetButtonToolTip(WindowsNavButton, "Open advanced native Windows tool setup actions.");
        SetButtonToolTip(WslLinuxNavButton, "Open advanced WSL, Ubuntu, and toolkit setup actions.");
        SetButtonToolTip(SettingsNavButton, "Open app preferences.");
        SetButtonToolTip(LifetimeNavButton, "Open persisted lifetime token counters.");
        SetButtonToolTip(LogsNavButton, "Open app, runtime, and job logs.");
        SetButtonToolTip(UpdatesNavButton, "Check for app updates from GitHub releases.");
        SetButtonToolTip(HelpNavButton, "Open first-run setup and app help.");
    }

    private static string ButtonToolTip(string text)
    {
        var label = (text ?? "").Trim();
        return label switch
        {
            "Load" => "Load the selected model with its saved launch settings.",
            "Unload" => "Stop the currently loading or loaded model and free runtime resources.",
            "Save For Model" => "Save these launch settings for the selected model.",
            "Save Profile" => "Save changes to the selected named launch profile.",
            "Save App Defaults" => "Use the current form values as the app defaults when creating future model profiles.",
            "Reset Defaults" => "Restore launch settings to the app defaults.",
            "Refresh Logs" => "Reload the log file list.",
            "Open Selected" => "Open the selected log file.",
            "Open Logs Folder" => "Open the app logs folder in File Explorer.",
            "Delete Selected" => "Delete the selected log files when they are safe to remove.",
            "Delete All Logs" => "Delete all removable log files.",
            "Add" => "Add the selected item.",
            "Update" => "Update the selected item.",
            "Search Hugging Face" => "Search Hugging Face for GGUF model files.",
            "History" => "Show model download history and controls.",
            "Save Settings" => "Save the current app preferences.",
            "Open GitHub" => "Open the app's GitHub repository in your browser.",
            "Refresh" => "Refresh the current page.",
            "Choose" => "Choose a folder.",
            "Open" => "Open this folder.",
            "Scan Models Folder" => "Scan the models folder for local GGUF files.",
            "Install WSL" => "Install Windows Subsystem for Linux.",
            "Update WSL" => "Check for WSL updates.",
            "Delete WSL" => "Remove the WSL feature from this machine.",
            "Install Ubuntu" => "Install the recommended Ubuntu distro for WSL builds.",
            "Update Ubuntu" => "Update packages in the selected Ubuntu distro.",
            "Delete Ubuntu" => "Remove the selected Ubuntu distro.",
            "Install CPU Tools" => "Install CPU build tools.",
            "Install CUDA" => "Install NVIDIA CUDA Toolkit packages.",
            "Install Vulkan" => "Install Vulkan build and runtime tools.",
            "Repair CPU Tools" => "Repair CPU build tools.",
            "Repair CUDA" => "Repair NVIDIA CUDA Toolkit packages.",
            "Repair Vulkan" => "Repair Vulkan build and runtime tools.",
            "Open Windows" => "Open native Windows setup actions.",
            "Open WSL Linux" => "Open WSL Linux setup actions.",
            "Open Runtimes" => "Open runtime source download and build actions.",
            "Open Models" => "Open model search, download, and launch settings.",
            "Open Overview" => "Open the model loading dashboard.",
            "First Steps" => "Show first-run setup help.",
            "Overview" => "Show Overview help.",
            "Models" => "Show Models help.",
            "Runtimes" => "Show Runtimes help.",
            "Settings" => "Show Settings help.",
            "Logs & Updates" => "Show logs and updates help.",
            "Search Models" => "Open Models and focus Hugging Face search.",
            "Edit Launch Settings" => "Open Models and focus launch settings.",
            "Gateway Settings" => "Open Settings and show gateway options.",
            "Windows Tools" => "Open advanced Windows setup actions.",
            "WSL Tools" => "Open advanced WSL setup actions.",
            "Runtime Jobs" => "Open Runtimes and focus runtime jobs.",
            "Open Logs" => "Open log inspection.",
            "Open Lifetime" => "Open lifetime token counters.",
            "Check Updates" => "Open app update checks.",
            _ when label.StartsWith("Install ", StringComparison.OrdinalIgnoreCase) => $"Run {label}.",
            _ when label.StartsWith("Delete ", StringComparison.OrdinalIgnoreCase) => $"Run {label}.",
            _ when label.StartsWith("Check", StringComparison.OrdinalIgnoreCase) => label,
            _ => string.IsNullOrWhiteSpace(label) ? "" : $"Run {label}."
        };
    }
}
