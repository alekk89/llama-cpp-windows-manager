namespace LocalLlmConsole;

public partial class MainWindow
{
    private const double ExpandedNavigationWidth = 244;

    private void SetActiveNavigation(string title)
    {
        var accessibleTitle = title switch
        {
            "Overview" => Loc.T("Nav.Overview"),
            "Models" => Loc.T("Nav.Models"),
            "Runtimes" => Loc.T("Nav.Runtimes"),
            "Benchmarks" => Loc.T("Nav.Benchmarks"),
            "Windows" => Loc.T("Nav.Windows"),
            "WSL Linux" => Loc.T("Nav.WslLinux"),
            "Settings" => Loc.T("Nav.Settings"),
            "Metrics" => Loc.T("Nav.Lifetime"),
            "Logs" => Loc.T("Nav.Logs"),
            "Updates" => Loc.T("Nav.CheckForUpdates"),
            "Help" => Loc.T("Nav.Help"),
            _ => title
        };
        System.Windows.Automation.AutomationProperties.SetName(PageHost, accessibleTitle);
        foreach (var button in new[] { OverviewNavButton, ModelsNavButton, RuntimesNavButton, BenchmarksNavButton, WindowsNavButton, WslLinuxNavButton, SettingsNavButton, LifetimeNavButton, LogsNavButton, UpdatesNavButton, HelpNavButton })
            button.Tag = null;

        var active = title switch
        {
            "Overview" => OverviewNavButton,
            "Models" => ModelsNavButton,
            "Runtimes" => RuntimesNavButton,
            "Benchmarks" => BenchmarksNavButton,
            "Windows" => WindowsNavButton,
            "WSL Linux" => WslLinuxNavButton,
            "Settings" => SettingsNavButton,
            "Metrics" => LifetimeNavButton,
            "Logs" => LogsNavButton,
            "Updates" => UpdatesNavButton,
            "Help" => HelpNavButton,
            _ => null
        };
        if (active is not null) active.Tag = "Active";
    }

    private void ApplyStaticButtonToolTips()
    {
        UiAccessibility.SetButtonToolTip(MinimizeButton, Loc.T("Tooltip.MinimizeButton"));
        UiAccessibility.SetButtonToolTip(MaximizeButton, Loc.T("Tooltip.MaximizeRestoreButton"));
        UiAccessibility.SetButtonToolTip(CloseButton, Loc.T("Tooltip.CloseButton"));
        UiAccessibility.SetButtonToolTip(OverviewNavButton, Loc.T("Tooltip.NavOverview"));
        UiAccessibility.SetButtonToolTip(ModelsNavButton, Loc.T("Tooltip.NavModels"));
        UiAccessibility.SetButtonToolTip(RuntimesNavButton, Loc.T("Tooltip.NavRuntimes"));
        UiAccessibility.SetButtonToolTip(BenchmarksNavButton, Loc.T("Tooltip.NavBenchmarks"));
        UiAccessibility.SetButtonToolTip(WindowsNavButton, Loc.T("Tooltip.NavWindows"));
        UiAccessibility.SetButtonToolTip(WslLinuxNavButton, Loc.T("Tooltip.NavWslLinux"));
        UiAccessibility.SetButtonToolTip(SettingsNavButton, Loc.T("Tooltip.NavSettings"));
        UiAccessibility.SetButtonToolTip(LifetimeNavButton, Loc.T("Tooltip.NavLifetime"));
        UiAccessibility.SetButtonToolTip(LogsNavButton, Loc.T("Tooltip.NavLogs"));
        UiAccessibility.SetButtonToolTip(UpdatesNavButton, Loc.T("Tooltip.NavUpdates"));
        UiAccessibility.SetButtonToolTip(HelpNavButton, Loc.T("Tooltip.NavHelp"));
        System.Windows.Automation.AutomationProperties.SetName(MinimizeButton, Loc.T("Tooltip.MinimizeButton"));
        System.Windows.Automation.AutomationProperties.SetName(MaximizeButton, Loc.T("Tooltip.MaximizeRestoreButton"));
        System.Windows.Automation.AutomationProperties.SetName(CloseButton, Loc.T("Tooltip.CloseButton"));
    }

    private void NavigationToggleButton_Click(object sender, System.Windows.RoutedEventArgs e)
        => ApplyNavigationToggleState(SidebarNavigation.Visibility == System.Windows.Visibility.Visible);

    private void ApplyNavigationToggleState(bool collapsed)
    {
        SidebarColumn.Width = collapsed ? new System.Windows.GridLength(0) : new System.Windows.GridLength(ExpandedNavigationWidth);
        SidebarNavigation.Visibility = collapsed ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
        var accessibleText = Loc.T(collapsed ? "Navigation.ExpandMenu" : "Navigation.CollapseMenu");
        System.Windows.Automation.AutomationProperties.SetName(NavigationToggleButton, accessibleText);
        UiAccessibility.SetButtonToolTip(NavigationToggleButton, accessibleText);
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
            "Open Logs" => "Open log inspection.",
            "Open Metrics" => "Open usage and performance metrics.",
            "Check Updates" => "Open app update checks.",
            _ when label.StartsWith("Install ", StringComparison.OrdinalIgnoreCase) => $"Run {label}.",
            _ when label.StartsWith("Delete ", StringComparison.OrdinalIgnoreCase) => $"Run {label}.",
            _ when label.StartsWith("Check", StringComparison.OrdinalIgnoreCase) => label,
            _ => string.IsNullOrWhiteSpace(label) ? "" : Loc.T("Tooltip.RunAction", label)
        };
    }
}
