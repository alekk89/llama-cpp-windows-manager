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
using WpfSystemColors = System.Windows.SystemColors;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public static class ApplicationThemeService
{
    private static string _lastMode = "system";
    private static int _systemPreferenceHooked;

    public static void Apply(string mode)
    {
        _lastMode = mode;
        EnsureSystemPreferenceHook();
        var dark = AppPreferenceService.ThemeMode(mode) switch
        {
            "light" => false,
            "dark" => true,
            _ => IsSystemDarkTheme()
        };

        var colors = SystemParameters.HighContrast
            ? HighContrastThemeColors()
            : (dark ? DarkThemeColors() : LightThemeColors())
                .Select(item => (item.Key, (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(item.Color)))
                .ToArray();
        var replacements = new Dictionary<SolidColorBrush, SolidColorBrush>(ReferenceEqualityComparer.Instance);
        foreach (var (key, resolved) in colors)
        {
            if (WpfApplication.Current.Resources[key] is SolidColorBrush brush && !brush.IsFrozen)
            {
                brush.Color = resolved;
            }
            else
            {
                var replacement = new SolidColorBrush(resolved);
                if (WpfApplication.Current.Resources[key] is SolidColorBrush previous)
                    replacements[previous] = replacement;
                WpfApplication.Current.Resources[key] = replacement;
            }
        }

        if (replacements.Count > 0)
            RefreshProgrammaticBrushReferences(replacements);
    }

    private static void EnsureSystemPreferenceHook()
    {
        if (Interlocked.Exchange(ref _systemPreferenceHooked, 1) != 0) return;
        SystemParameters.StaticPropertyChanged += (_, args) =>
        {
            if (!string.Equals(args.PropertyName, nameof(SystemParameters.HighContrast), StringComparison.Ordinal)) return;
            var dispatcher = WpfApplication.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;
            _ = dispatcher.BeginInvoke(() => Apply(_lastMode));
        };
    }

    private static (string Key, System.Windows.Media.Color Color)[] HighContrastThemeColors()
        => DarkThemeColors()
            .Select(item => (item.Key, HighContrastColor(item.Key)))
            .ToArray();

    private static System.Windows.Media.Color HighContrastColor(string key)
        => key switch
        {
            "TextMuted" or "DisabledPrimaryForeground" or "DisabledControlForeground" => WpfSystemColors.GrayTextColor,
            "AccentForeground" => WpfSystemColors.HighlightTextColor,
            "Accent" or "AccentStrong" or "AccentHover" or "AccentPressed" or "AccentBlue"
                or "FocusRing" or "Success" or "Warning" or "DestructiveAction"
                or "DestructiveActionHover" or "Danger" or "DangerHover" => WpfSystemColors.HighlightColor,
            "PanelBorder" or "PanelBorderStrong" or "DisabledPrimaryBorder" or "DisabledControlBorder"
                => WpfSystemColors.WindowTextColor,
            "SelectionBack" or "AccentSoft" or "SuccessSoft" or "WarningSoft" or "DestructiveActionSoft"
                or "DangerSoft" or "StatusRunning" or "StatusFailed" => WpfSystemColors.HighlightColor,
            "TextMain" or "TextSoft" => WpfSystemColors.WindowTextColor,
            _ => WpfSystemColors.WindowColor
        };

    private static void RefreshProgrammaticBrushReferences(
        IReadOnlyDictionary<SolidColorBrush, SolidColorBrush> replacements)
    {
        var visited = new HashSet<DependencyObject>(ReferenceEqualityComparer.Instance);
        foreach (Window window in WpfApplication.Current.Windows)
            RefreshBrushReferences(window, replacements, visited);
    }

    private static void RefreshBrushReferences(
        DependencyObject target,
        IReadOnlyDictionary<SolidColorBrush, SolidColorBrush> replacements,
        ISet<DependencyObject> visited)
    {
        if (!visited.Add(target)) return;

        var localValues = target.GetLocalValueEnumerator();
        var updates = new List<(DependencyProperty Property, SolidColorBrush Brush)>();
        while (localValues.MoveNext())
        {
            var entry = localValues.Current;
            if (entry.Value is SolidColorBrush current && replacements.TryGetValue(current, out var replacement))
                updates.Add((entry.Property, replacement));
        }
        foreach (var (property, brush) in updates)
            target.SetValue(property, brush);

        if (target is Visual or System.Windows.Media.Media3D.Visual3D)
        {
            for (var index = 0; index < VisualTreeHelper.GetChildrenCount(target); index++)
                RefreshBrushReferences(VisualTreeHelper.GetChild(target, index), replacements, visited);
        }
        foreach (var child in LogicalTreeHelper.GetChildren(target).OfType<DependencyObject>())
            RefreshBrushReferences(child, replacements, visited);
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
        ("DestructiveAction", "#B9A6D3"),
        ("DestructiveActionHover", "#CDBCE0"),
        ("DestructiveActionSoft", "#302C36"),
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
        ("AppBack", "#F3F5F8"),
        ("SidebarBack", "#E9EDF2"),
        ("PanelBack", "#FFFFFF"),
        ("PanelBackAlt", "#F7F9FB"),
        ("SurfaceRaised", "#FFFFFF"),
        ("SectionHeaderBack", "#EDF1F5"),
        ("PanelBorder", "#D5DCE5"),
        ("PanelBorderStrong", "#B8C3D0"),
        ("ControlBack", "#F1F4F7"),
        ("ControlHover", "#E4EAF1"),
        ("ControlPressed", "#D7E0EA"),
        ("InputBack", "#FFFFFF"),
        ("ReadOnlyBack", "#EDF1F5"),
        ("GridRowBack", "#FFFFFF"),
        ("GridRowAlt", "#F6F8FA"),
        ("TextMain", "#17212D"),
        ("TextMuted", "#667487"),
        ("TextSoft", "#3D4B5C"),
        ("Accent", "#263545"),
        ("AccentStrong", "#1E2C3A"),
        ("AccentHover", "#34485C"),
        ("AccentForeground", "#FFFFFF"),
        ("AccentSoft", "#DFE6EE"),
        ("DisabledPrimaryBack", "#DCE2E8"),
        ("DisabledPrimaryBorder", "#B6C0CB"),
        ("DisabledPrimaryForeground", "#526171"),
        ("DisabledControlBack", "#EEF1F4"),
        ("DisabledControlBorder", "#CED5DD"),
        ("DisabledControlForeground", "#687687"),
        ("AccentPressed", "#172430"),
        ("AccentBlue", "#405BC8"),
        ("InfoSoft", "#E8EEF5"),
        ("SelectionBack", "#DDE7F3"),
        ("FocusRing", "#526A84"),
        ("Success", "#177447"),
        ("SuccessSoft", "#DCEFE5"),
        ("Warning", "#875300"),
        ("WarningSoft", "#F6E9CA"),
        ("DestructiveAction", "#66527A"),
        ("DestructiveActionHover", "#755F8B"),
        ("DestructiveActionSoft", "#EEEAF2"),
        ("Danger", "#B4233A"),
        ("DangerHover", "#CE3048"),
        ("DangerSoft", "#F7E0E5"),
        ("StatusBack", "#EAF0F4"),
        ("ShadowColor", "#526273"),
        ("StatusQueued", "#E1E7ED"),
        ("StatusRunning", "#D7EDE2"),
        ("StatusFailed", "#F5DEE4"),
        ("StatusCancelled", "#E9E1E8")
    ];
}
