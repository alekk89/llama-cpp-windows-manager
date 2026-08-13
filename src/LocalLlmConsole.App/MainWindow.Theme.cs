using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private static void ApplyTheme(string mode)
    {
        var dark = AppPreferenceService.ThemeMode(mode) switch
        {
            "light" => false,
            "dark" => true,
            _ => IsSystemDarkTheme()
        };

        foreach (var (key, color) in dark ? DarkThemeColors() : LightThemeColors())
        {
            var resolved = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color);
            if (WpfApplication.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = resolved;
            }
            else
            {
                WpfApplication.Current.Resources[key] = new SolidColorBrush(resolved);
            }
        }
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch
        {
            return true;
        }
    }

    private static (string Key, string Color)[] DarkThemeColors() =>
    [
        ("AppBack", "#191918"),
        ("SidebarBack", "#151514"),
        ("PanelBack", "#1F1F1D"),
        ("PanelBackAlt", "#252523"),
        ("SurfaceRaised", "#222220"),
        ("SectionHeaderBack", "#272725"),
        ("PanelBorder", "#30302E"),
        ("PanelBorderStrong", "#42423F"),
        ("ControlBack", "#2A2A28"),
        ("ControlHover", "#343431"),
        ("ControlPressed", "#1D1D1B"),
        ("InputBack", "#181817"),
        ("ReadOnlyBack", "#20201E"),
        ("GridRowBack", "#1F1F1D"),
        ("GridRowAlt", "#232321"),
        ("TextMain", "#F3F3EF"),
        ("TextMuted", "#92928E"),
        ("TextSoft", "#C9C9C4"),
        ("Accent", "#E8E8E3"),
        ("AccentStrong", "#D6D6D0"),
        ("AccentHover", "#FFFFFF"),
        ("AccentForeground", "#171716"),
        ("AccentSoft", "#30302D"),
        ("DisabledPrimaryBack", "#343431"),
        ("DisabledPrimaryBorder", "#555550"),
        ("DisabledPrimaryForeground", "#C7C7C1"),
        ("DisabledControlBack", "#242422"),
        ("DisabledControlBorder", "#3B3B38"),
        ("DisabledControlForeground", "#9B9B95"),
        ("AccentPressed", "#BDBDB7"),
        ("AccentBlue", "#AEB8FF"),
        ("InfoSoft", "#2A2A28"),
        ("SelectionBack", "#383835"),
        ("FocusRing", "#A8A8A3"),
        ("Success", "#61C991"),
        ("SuccessSoft", "#20372A"),
        ("Warning", "#E8B95A"),
        ("WarningSoft", "#3B3120"),
        ("Danger", "#F07A82"),
        ("DangerHover", "#FF969C"),
        ("DangerSoft", "#3D2528"),
        ("StatusBack", "#202422"),
        ("ShadowColor", "#000000"),
        ("StatusQueued", "#30302E"),
        ("StatusRunning", "#20372A"),
        ("StatusFailed", "#42282D"),
        ("StatusCancelled", "#342B31")
    ];

    private static (string Key, string Color)[] LightThemeColors() =>
    [
        ("AppBack", "#F7F7F5"),
        ("SidebarBack", "#EFEFEC"),
        ("PanelBack", "#FFFFFF"),
        ("PanelBackAlt", "#F5F5F2"),
        ("SurfaceRaised", "#FAFAF8"),
        ("SectionHeaderBack", "#F0F0ED"),
        ("PanelBorder", "#E1E1DC"),
        ("PanelBorderStrong", "#C7C7C0"),
        ("ControlBack", "#F0F0ED"),
        ("ControlHover", "#E8E8E4"),
        ("ControlPressed", "#DCDCD6"),
        ("InputBack", "#FFFFFF"),
        ("ReadOnlyBack", "#F1F1EE"),
        ("GridRowBack", "#FFFFFF"),
        ("GridRowAlt", "#F8F8F5"),
        ("TextMain", "#1F1F1D"),
        ("TextMuted", "#6F6F69"),
        ("TextSoft", "#444440"),
        ("Accent", "#1F1F1D"),
        ("AccentStrong", "#292927"),
        ("AccentHover", "#000000"),
        ("AccentForeground", "#FFFFFF"),
        ("AccentSoft", "#E7E7E2"),
        ("DisabledPrimaryBack", "#E3E3DE"),
        ("DisabledPrimaryBorder", "#B4B4AD"),
        ("DisabledPrimaryForeground", "#555550"),
        ("DisabledControlBack", "#F2F2EF"),
        ("DisabledControlBorder", "#D0D0CA"),
        ("DisabledControlForeground", "#666660"),
        ("AccentPressed", "#3A3A37"),
        ("AccentBlue", "#4F63D8"),
        ("InfoSoft", "#EEEEEA"),
        ("SelectionBack", "#E2E2DD"),
        ("FocusRing", "#73736E"),
        ("Success", "#187A45"),
        ("SuccessSoft", "#DDF3E6"),
        ("Warning", "#8A5100"),
        ("WarningSoft", "#F8EACB"),
        ("Danger", "#B42335"),
        ("DangerHover", "#D13245"),
        ("DangerSoft", "#FAE1E5"),
        ("StatusBack", "#F0F2EF"),
        ("ShadowColor", "#5A5A55"),
        ("StatusQueued", "#E7E7E2"),
        ("StatusRunning", "#D7EFE4"),
        ("StatusFailed", "#F8DFE4"),
        ("StatusCancelled", "#ECE3EA")
    ];
}
