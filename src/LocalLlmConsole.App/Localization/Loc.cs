namespace LocalLlmConsole.Localization;

public static class Loc
{
    private sealed record LocalizationSnapshot(
        string CurrentLanguage,
        CultureInfo FormatCulture,
        IReadOnlyDictionary<string, string> Strings,
        IReadOnlyDictionary<string, string> Fallback);

    private static readonly HashSet<string> PreviewLanguages = new(["ar", "hi"], StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RightToLeftLanguages = new(["ar", "fa"], StringComparer.OrdinalIgnoreCase);
    private static LocalizationSnapshot _snapshot = new(
        "en",
        CultureInfo.GetCultureInfo("en"),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    public static string CurrentLanguage => Volatile.Read(ref _snapshot).CurrentLanguage;
    public static CultureInfo FormatCulture => Volatile.Read(ref _snapshot).FormatCulture;

    /// <summary>Lookup a localized string by key. Returns the key itself if not found.</summary>
    public static string T(string key)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        return Translate(snapshot, key);
    }

    private static string Translate(LocalizationSnapshot snapshot, string key)
    {
        if (snapshot.Strings.TryGetValue(key, out var value))
            return value;
        if (snapshot.Fallback.TryGetValue(key, out var fallback))
        {
#if DEBUG
            if (ShowMissingKeys && !string.Equals(snapshot.CurrentLanguage, "en", StringComparison.OrdinalIgnoreCase))
                return $"«{key}»"; // Visual indicator that key is missing from translation
#endif
            return fallback;
        }
#if DEBUG
        if (ShowMissingKeys)
            return $"???{key}???";
#endif
        return key;
    }

    /// <summary>Lookup with String.Format placeholders. {0}, {1}, etc.</summary>
    public static string T(string key, params object[] args)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        var template = Translate(snapshot, key);
        return args.Length > 0 ? string.Format(snapshot.FormatCulture, template, args) : template;
    }

#if DEBUG
    /// <summary>Show missing keys with «key» notation instead of English fallback (debug only).</summary>
    public static bool ShowMissingKeys { get; set; } = false;
#endif

    /// <summary>Load a language by code ("en", "bg", "de"). Falls back to English on any failure.</summary>
    public static void LoadLanguage(string languageCode)
    {
        var requested = string.IsNullOrWhiteSpace(languageCode)
            ? "en"
            : languageCode.Trim().ToLowerInvariant();
        var resolved = AvailableLanguages().Contains(requested, StringComparer.OrdinalIgnoreCase)
            ? requested
            : "en";

        var strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fallback = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Always load English fallback first
        TryLoadJson("Strings.en.json", fallback);

        if (!string.Equals(resolved, "en", StringComparison.OrdinalIgnoreCase)
            && !TryLoadJson($"Strings.{resolved}.json", strings))
        {
            resolved = "en";
            strings.Clear();
        }

        Volatile.Write(
            ref _snapshot,
            new LocalizationSnapshot(
                resolved,
                CultureInfo.GetCultureInfo(resolved),
                strings,
                fallback));
    }

    private static bool TryLoadJson(string resourceName, Dictionary<string, string> target)
    {
        try
        {
            var assembly = Assembly.GetExecutingAssembly();
            var fullName = $"LocalLlmConsole.Localization.{resourceName}";

            using var stream = assembly.GetManifestResourceStream(fullName);
            if (stream is null) return false;

            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (dict is null) return false;
            foreach (var kvp in dict)
                target[kvp.Key] = kvp.Value;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Trace.TraceWarning($"Could not load localization resource '{resourceName}': {ex.Message}");
            return false;
        }
    }

    /// <summary>Discover available languages by scanning embedded resources.</summary>
    public static IReadOnlyList<string> AvailableLanguages()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var prefix = "LocalLlmConsole.Localization.Strings.";
        return assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && n.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            .Select(n => n[prefix.Length..^5]) // Extract "en", "bg", etc.
            .OrderBy(c => c == "en" ? 0 : 1).ThenBy(c => c)
            .ToList();
    }

    public static bool IsPreviewLanguage(string? code)
        => !string.IsNullOrWhiteSpace(code) && PreviewLanguages.Contains(code);

    public static bool IsRightToLeft(string? code)
        => !string.IsNullOrWhiteSpace(code) && RightToLeftLanguages.Contains(code);

    /// <summary>Human-readable name for a language code (for the ComboBox).</summary>
    public static string LanguageDisplayName(string code) => code switch
    {
        "en" => "English",
        "bg" => "Български",
        "de" => "Deutsch",
        "es" => "Español",
        "fr" => "Français",
        "ru" => "Русский",
        "ja" => "日本語",
        "pt" => "Português",
        "zh" => "中文 (简体)",
        "it" => "Italiano",
        "tr" => "Türkçe",
        "fa" => "فارسی",
        "pl" => "Polski",
        "nl" => "Nederlands",
        "vi" => "Tiếng Việt",
        "ko" => "한국어",
        "ar" => "العربية — ترجمة جزئية",
        "id" => "Bahasa Indonesia",
        "hi" => "हिन्दी — आंशिक अनुवाद",
        "cs" => "Čeština",
        "sv" => "Svenska",
        _ => code
    };
}
