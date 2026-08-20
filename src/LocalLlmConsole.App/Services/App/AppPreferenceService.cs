namespace LocalLlmConsole.Services;

public static class AppPreferenceService
{


    public static string ThemeMode(string text)
    {
        var value = (text ?? "").Trim().ToLowerInvariant();
        return value is "light" or "dark" or "system" ? value : "system";
    }

    public static string MinimizeBehavior(string text)
    {
        if (LocalizedEquals(text, "Pref.TrayOnly")) return "trayOnly";
        if (LocalizedEquals(text, "Pref.TrayAndTaskbar")) return "trayAndTaskbar";
        if (LocalizedEquals(text, "Pref.TaskbarOnly")) return "taskbarOnly";

        var value = (text ?? "").Trim()
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return value switch
        {
            "tray" or "trayonly" => "trayOnly",
            "traytaskbar" or "trayandtaskbar" or "trayplustaskbar" or "tray+taskbar" or "traywhenrunning" or "running" => "trayAndTaskbar",
            _ => "taskbarOnly"
        };
    }

    public static string MinimizeBehaviorLabel(string text) => MinimizeBehavior(text) switch
    {
        "trayOnly" => Loc.T("Pref.TrayOnly"),
        "trayAndTaskbar" => Loc.T("Pref.TrayAndTaskbar"),
        _ => Loc.T("Pref.TaskbarOnly")
    };

    public static IEnumerable<string> MinimizeBehaviorOptions() =>
    [
        Loc.T("Pref.TaskbarOnly"),
        Loc.T("Pref.TrayOnly"),
        Loc.T("Pref.TrayAndTaskbar")
    ];

    public static string ModelAccessMode(string text)
    {
        if (LocalizedEquals(text, "Pref.GatewayLanOnly")) return "gateway";
        if (LocalizedEquals(text, "Pref.DirectModelsLanOnly")) return "models";
        if (LocalizedEquals(text, "Pref.GatewayAndDirectLan")) return "both";
        if (LocalizedEquals(text, "Pref.LocalOnly")) return "local";
        return ModelAccessPolicy.Normalize(text);
    }

    public static string ModelAccessModeLabel(string text) => ModelAccessMode(text) switch
    {
        "gateway" => Loc.T("Pref.GatewayLanOnly"),
        "models" => Loc.T("Pref.DirectModelsLanOnly"),
        "both" => Loc.T("Pref.GatewayAndDirectLan"),
        _ => Loc.T("Pref.LocalOnly")
    };

    public static IEnumerable<string> ModelAccessModeOptions() =>
    [
        Loc.T("Pref.LocalOnly"),
        Loc.T("Pref.GatewayLanOnly"),
        Loc.T("Pref.DirectModelsLanOnly"),
        Loc.T("Pref.GatewayAndDirectLan")
    ];

    public static bool GatewayAllowsLanAccess(string text)
        => ModelAccessPolicy.GatewayAllowsLanAccess(ModelAccessMode(text));

    public static bool DirectModelsAllowLanAccess(string text)
        => ModelAccessPolicy.DirectModelsAllowLanAccess(ModelAccessMode(text));

    public static string GatewaySwapPolicy(string text)
    {
        if (LocalizedEquals(text, "Pref.SingleActiveModel")) return "singleActive";
        if (LocalizedEquals(text, "Pref.PreferKeepingLoaded")) return "keepLoaded";

        var value = (text ?? "").Trim()
            .Replace("-", "", StringComparison.OrdinalIgnoreCase)
            .Replace("_", "", StringComparison.OrdinalIgnoreCase)
            .Replace(" ", "", StringComparison.OrdinalIgnoreCase)
            .ToLowerInvariant();
        return value is "single" or "singleactive" or "singleactivemodel" or "swap" or "swaponrequest"
            ? "singleActive"
            : "keepLoaded";
    }

    public static string GatewaySwapPolicyLabel(string text)
        => GatewaySwapPolicy(text) == "singleActive" ? Loc.T("Pref.SingleActiveModel") : Loc.T("Pref.PreferKeepingLoaded");

    public static IEnumerable<string> GatewaySwapPolicyOptions() =>
    [
        Loc.T("Pref.PreferKeepingLoaded"),
        Loc.T("Pref.SingleActiveModel")
    ];

    public static string CudaPackagePreference(string text)
    {
        if (LocalizedEquals(text, "Pref.Compatibility")) return "compatibility";
        if (LocalizedEquals(text, "Pref.Latest")) return "latest";
        return PackagePreferencePolicy.NormalizeCuda(text);
    }

    public static string CudaPackagePreferenceLabel(string text)
        => CudaPackagePreference(text) == "compatibility" ? Loc.T("Pref.Compatibility") : Loc.T("Pref.Latest");

    public static IEnumerable<string> CudaPackagePreferenceOptions() =>
    [
        Loc.T("Pref.Latest"),
        Loc.T("Pref.Compatibility")
    ];

    public static string YesNoLabel(bool value) => value ? Loc.T("Pref.Yes") : Loc.T("Pref.No");

    public static IEnumerable<string> YesNoOptions() =>
    [
        Loc.T("Pref.Yes"),
        Loc.T("Pref.No")
    ];

    public static string ShowHideLabel(bool value) => value ? Loc.T("Pref.Show") : Loc.T("Pref.Hide");

    public static IEnumerable<string> ShowHideOptions() =>
    [
        Loc.T("Pref.Show"),
        Loc.T("Pref.Hide")
    ];

    public static bool ShowHideValue(string text, bool fallback)
    {
        if (LocalizedEquals(text, "Pref.Show")) return true;
        if (LocalizedEquals(text, "Pref.Hide")) return false;

        var value = (text ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "show" or "shown" or "visible" => true,
            "hide" or "hidden" => false,
            _ => YesNoValue(text ?? "", fallback)
        };
    }

    public static bool YesNoValue(string text, bool fallback)
    {
        if (LocalizedEquals(text, "Pref.Yes")) return true;
        if (LocalizedEquals(text, "Pref.No")) return false;

        var value = (text ?? "").Trim().ToLowerInvariant();
        return value switch
        {
            "yes" or "true" or "1" or "on" => true,
            "no" or "false" or "0" or "off" => false,
            _ => fallback
        };
    }

    private static bool LocalizedEquals(string? value, string localizationKey)
        => string.Equals((value ?? "").Trim(), Loc.T(localizationKey).Trim(), StringComparison.CurrentCultureIgnoreCase);

    public static string RuntimeHostForAccessMode(string accessMode)
        => ModelAccessPolicy.RuntimeHost(ModelAccessMode(accessMode));

    public static bool TryIntValue(string text, out int value)
        => int.TryParse((text ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    public static int ClampedIntValue(string text, int fallback, int min, int max)
        => Math.Clamp(TryIntValue(text, out var value) ? value : fallback, min, max);
}
