using System.Text.Json;
using System.Text.RegularExpressions;
using LocalLlmConsole.Localization;

namespace LocalLlmConsole.Tests;

[Collection(LocalizationStateTestCollection.Name)]
public sealed class LocalizationTests : ManagerRegressionTestBase
{
    [Fact]
    public void LocalizationPacksMatchEnglishContract()
    {
        var localizationRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src",
            "LocalLlmConsole.App",
            "Localization",
            "Strings.en.json"))!;
        var packs = Directory.GetFiles(localizationRoot, "Strings.*.json").OrderBy(path => path, StringComparer.Ordinal).ToArray();
        var english = ReadLocalizationPack(packs.Single(path => path.EndsWith("Strings.en.json", StringComparison.Ordinal)));

        Assert.NotEmpty(english);
        foreach (var path in packs)
        {
            var pack = ReadLocalizationPack(path);
            Assert.DoesNotContain(pack.Keys, key => !english.ContainsKey(key));
            Assert.DoesNotContain(pack, pair => string.IsNullOrWhiteSpace(pair.Value));

            foreach (var key in pack.Keys)
            {
                Assert.Equal(ExtractPlaceholders(english[key]), ExtractPlaceholders(pack[key]));
            }
        }
    }

    [Fact]
    public void ProductionLocalizationPacksMeetCoverageFloorAndPartialPacksAreDisclosed()
    {
        var localizationRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src",
            "LocalLlmConsole.App",
            "Localization",
            "Strings.en.json"))!;
        var english = ReadLocalizationPack(Path.Combine(localizationRoot, "Strings.en.json"));
        var previewCodes = new[] { "ar", "hi" };
        var helpFallbackCodes = new[] { "ko", "nl", "pl", "pt", "ru", "sv", "tr", "vi", "zh" };
        const double productionFallbackCeiling = .28;
        const double helpFallbackCeiling = .37;

        foreach (var path in Directory.GetFiles(localizationRoot, "Strings.*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path).Split('.')[1];
            if (code == "en") continue;
            var pack = ReadLocalizationPack(path);
            var fallbackCount = english.Count(pair => !pack.TryGetValue(pair.Key, out var value) || value == pair.Value);
            var identicalRatio = fallbackCount / (double)english.Count;

            if (previewCodes.Contains(code, StringComparer.Ordinal))
            {
                Assert.True(Loc.IsPreviewLanguage(code));
                Assert.True(identicalRatio > .25, $"Preview pack '{code}' unexpectedly meets the production coverage floor; promote it explicitly.");
            }
            else if (helpFallbackCodes.Contains(code, StringComparer.Ordinal))
            {
                Assert.False(Loc.IsPreviewLanguage(code));
                Assert.True(identicalRatio <= helpFallbackCeiling, $"Production pack '{code}' repeats {identicalRatio:P1} of English values.");
            }
            else
            {
                Assert.False(Loc.IsPreviewLanguage(code));
                Assert.True(identicalRatio <= productionFallbackCeiling, $"Production pack '{code}' repeats {identicalRatio:P1} of English values.");
            }
        }
    }

    [Fact]
    public void MissingLocalizedValuesUseTheDocumentedEnglishFallback()
    {
        try
        {
            Loc.LoadLanguage("fr");
            Assert.Equal("Add card", Loc.T("Dashboard.AddCard"));
        }
        finally
        {
            Loc.LoadLanguage("en");
        }
    }

    [Fact]
    public void LocalizationMetadataHandlesDirectionPreviewStatusAndInvalidCodes()
    {
        try
        {
            Assert.True(Loc.IsRightToLeft("ar"));
            Assert.True(Loc.IsRightToLeft("fa"));
            Assert.False(Loc.IsRightToLeft("hi"));
            Assert.Contains("جزئية", Loc.LanguageDisplayName("ar"), StringComparison.Ordinal);
            Assert.Contains("आंशिक", Loc.LanguageDisplayName("hi"), StringComparison.Ordinal);

            Loc.LoadLanguage("AR");
            Assert.Equal("ar", Loc.CurrentLanguage);
            Loc.LoadLanguage("not-a-supported-language");
            Assert.Equal("en", Loc.CurrentLanguage);
            Assert.Equal("Overview", Loc.T("Nav.Overview"));
        }
        finally
        {
            Loc.LoadLanguage("en");
        }
    }

    [Fact]
    public void LocalizationSnapshotsCanBeReloadedConcurrently()
    {
        try
        {
            var languages = new[] { "en", "ar", "de", "fr" };

            Parallel.For(0, 200, index =>
            {
                Loc.LoadLanguage(languages[index % languages.Length]);
                Assert.False(string.IsNullOrWhiteSpace(Loc.T("Nav.Overview")));
            });

            Loc.LoadLanguage("ar");
            Assert.Equal("ar", Loc.CurrentLanguage);
            Assert.False(string.IsNullOrWhiteSpace(Loc.T("Nav.Overview")));
        }
        finally
        {
            Loc.LoadLanguage("en");
        }
    }

    [Fact]
    public void StaticLocalizationLookupsAndLaunchTooltipsExistInEnglish()
    {
        var englishPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Localization", "Strings.en.json");
        var appRoot = Directory.GetParent(Path.GetDirectoryName(englishPath)!)!.FullName;
        var english = ReadLocalizationPack(englishPath);
        var lookupPattern = new Regex("Loc\\.T\\(\"(?<key>[^\"]+)\"", RegexOptions.CultureInvariant);
        var referenced = Directory.GetFiles(appRoot, "*.cs", SearchOption.AllDirectories)
            .SelectMany(path => lookupPattern.Matches(File.ReadAllText(path)).Select(match => match.Groups["key"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.DoesNotContain(referenced, key => !english.ContainsKey(key));
        foreach (var definition in LaunchSettingUiSchema.Definitions)
        {
            var standardTooltip = definition.LabelKey.Replace("Launch.Field.", "Tooltip.Field.", StringComparison.Ordinal);
            Assert.True(english.ContainsKey(standardTooltip), $"Missing launch tooltip '{standardTooltip}'.");
        }
    }

    [Fact]
    public void VisibleUiPropertiesDoNotRegressToLiteralEnglish()
    {
        var mainWindowPath = FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs");
        var appRoot = Path.GetDirectoryName(mainWindowPath)!;
        var candidates = Directory.GetFiles(Path.Combine(appRoot, "Ui"), "*.cs", SearchOption.AllDirectories)
            .Concat(new[]
            {
                Path.Combine(appRoot, "Ui", "Common", "ThemedMessageBox.cs"),
                Path.Combine(appRoot, "Services", "App", "CacheClearApplicationService.cs")
            });
        var literalProperty = new Regex(
            "\\b(?:Text|Content|Header|Title|ToolTip)\\s*=\\s*\"[A-Za-z]",
            RegexOptions.CultureInvariant);
        var violations = candidates
            .SelectMany(path => literalProperty.Matches(File.ReadAllText(path))
                .Select(match => $"{Path.GetRelativePath(appRoot, path)}: {match.Value}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void ModelGroupAndEndpointInspectionSurfacesAreLocalizedInEveryPack()
    {
        var localizationRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src",
            "LocalLlmConsole.App",
            "Localization",
            "Strings.en.json"))!;
        var english = ReadLocalizationPack(Path.Combine(localizationRoot, "Strings.en.json"));
        var requiredKeys = english.Keys
            .Where(key => key.StartsWith("ModelGroups.", StringComparison.Ordinal)
                          || key.StartsWith("EndpointInspection.", StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(131, requiredKeys.Length);
        foreach (var path in Directory.GetFiles(localizationRoot, "Strings.*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path).Split('.')[1];
            if (code == "en") continue;
            var pack = ReadLocalizationPack(path);
            Assert.DoesNotContain(requiredKeys, key => pack[key] == key);
            Assert.NotEqual(english["ModelGroups.Title"], pack["ModelGroups.Title"]);
            Assert.NotEqual(english["EndpointInspection.DialogTitle"], pack["EndpointInspection.DialogTitle"]);
        }
    }

    [Fact]
    public void HelpAndRemainingVisibleUiSurfacesAreLocalizedInEveryPack()
    {
        var localizationRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src",
            "LocalLlmConsole.App",
            "Localization",
            "Strings.en.json"))!;
        var english = ReadLocalizationPack(Path.Combine(localizationRoot, "Strings.en.json"));
        var fixedKeys = new[]
        {
            "Help.Page.Intro", "Help.Search.ResultsAutomationName", "Help.Search.Tooltip",
            "Help.Search.AutomationName", "Help.Search.AutomationHelp", "Help.Search.Placeholder",
            "Help.Search.ClearTooltip", "Help.Search.ClearAutomationName", "Help.Section.AutomationName",
            "Help.Action.NavigateTooltip", "Help.Search.NoMatchTitle", "Help.Search.NoMatchCategory",
            "Help.Search.NoMatchQuery", "Help.Search.ResultsFor", "Help.Search.TopicCount",
            "Overview.LaunchProfileTooltip", "Launch.Command.Tooltip", "Launch.Command.ApplyAddedFlags",
            "Launch.Command.ApplyTooltip", "Launch.Command.Hint", "Launch.Search.Placeholder",
            "ModelGroups.ChangeGroup", "ModelGroups.RemoveFromGroup", "ModelGroups.Column.Group",
            "ModelGroups.AssignAction", "ModelGroups.AssignTooltip", "Cache.Clear.Title",
            "Cache.Clear.Progress", "Cache.Clear.Success", "Dialog.Accessibility.Ok",
            "Dialog.Accessibility.Yes", "Dialog.Accessibility.No", "Dialog.Accessibility.Close",
            "Overview.EndpointInspectionTooltip", "Launch.SaveProfileTooltip"
        };
        // Preserve the translated release contract while new help uses documented English fallback.
        var articleKeys = english.Keys.Where(key => key.StartsWith("Help.Article.", StringComparison.Ordinal)
            && !key.StartsWith("Help.Article.benchmark-cleanup.", StringComparison.Ordinal)).ToArray();
        var requiredKeys = fixedKeys.Concat(articleKeys).Distinct(StringComparer.Ordinal).ToArray();
        var currentSettingsKeys = new[]
        {
            "Tooltip.Setting.ApiKeyAuth", "Settings.ApiKeyAuthDisabledTitle",
            "Settings.ApiKeyAuthDisabledMessage", "Settings.AutoApplyHint",
            "Pref.Enable", "Pref.Disable", "Help.Article.network-and-key.Detail.1"
        };
        var translatedHelpCodes = new[] { "ar", "bg", "cs", "de", "es", "fa", "fr", "hi", "id", "it", "ja" };

        Assert.Equal(148, articleKeys.Length);
        Assert.Equal(183, requiredKeys.Length);
        foreach (var path in Directory.GetFiles(localizationRoot, "Strings.*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path).Split('.')[1];
            var pack = ReadLocalizationPack(path);
            Assert.DoesNotContain(requiredKeys, key => !pack.ContainsKey(key));
            Assert.DoesNotContain(currentSettingsKeys, key => !pack.ContainsKey(key));
            Assert.DoesNotContain(pack, pair => pair.Value.Contains("[[[LLWM", StringComparison.Ordinal));
            if (code == "en") continue;
            Assert.DoesNotContain(currentSettingsKeys, key => pack[key] == english[key]);
            if (translatedHelpCodes.Contains(code, StringComparer.Ordinal))
            {
                Assert.NotEqual(english["Help.Article.quick-start.Title"], pack["Help.Article.quick-start.Title"]);
                Assert.NotEqual(english["Help.Search.Placeholder"], pack["Help.Search.Placeholder"]);
            }
            else
            {
                var englishFallbacks = requiredKeys.Count(key => pack[key] == english[key]);
                Assert.InRange(englishFallbacks, 1, requiredKeys.Length - 1);
            }
        }
    }

    [Fact]
    public void BenchmarkCleanupHelpUsesLocalizedValuesOrEnglishFallbackInEveryPack()
    {
        var localizationRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src", "LocalLlmConsole.App", "Localization", "Strings.en.json"))!;
        var english = ReadLocalizationPack(Path.Combine(localizationRoot, "Strings.en.json"));
        var keys = new[]
        {
            "Help.Article.benchmark-cleanup.Title", "Help.Article.benchmark-cleanup.Summary",
            "Help.Article.benchmark-cleanup.Detail.1", "Help.Article.benchmark-cleanup.Detail.2"
        };
        Assert.Equal(keys.Order(), english.Keys.Where(key => key.StartsWith("Help.Article.benchmark-cleanup.", StringComparison.Ordinal)).Order());
        try
        {
            foreach (var path in Directory.GetFiles(localizationRoot, "Strings.*.json"))
            {
                var code = Path.GetFileNameWithoutExtension(path).Split('.')[1];
                var pack = ReadLocalizationPack(path);
                Loc.LoadLanguage(code);
                foreach (var key in keys)
                {
                    Assert.Equal(pack.GetValueOrDefault(key, english[key]), Loc.T(key));
                }
            }
        }
        finally
        {
            Loc.LoadLanguage("en");
        }
    }

    [Fact]
    public void LocalizedHelpPreservesTechnicalProductNamesRoutesAndFlags()
    {
        var localizationRoot = Path.GetDirectoryName(FindRepositoryFile(
            "src",
            "LocalLlmConsole.App",
            "Localization",
            "Strings.en.json"))!;
        var english = ReadLocalizationPack(Path.Combine(localizationRoot, "Strings.en.json"));
        var helpKeys = english.Keys.Where(key => key.StartsWith("Help.Article.", StringComparison.Ordinal)).ToArray();
        var protectedTerms = new[]
        {
            "GGUF", "llama.cpp", "Hugging Face", "OpenAI", "API", "WSL", "CUDA", "Vulkan", "llwmctl",
            "/health", "/v1/models", "/props", "/slots", "/running", "--model-draft", "--mmproj",
            "llama-server.exe", "llama-server"
        };
        var translatedHelpCodes = new[] { "ar", "bg", "cs", "de", "es", "fa", "fr", "hi", "id", "it", "ja" };

        foreach (var path in Directory.GetFiles(localizationRoot, "Strings.*.json"))
        {
            var code = Path.GetFileNameWithoutExtension(path).Split('.')[1];
            if (!translatedHelpCodes.Contains(code, StringComparer.Ordinal)) continue;
            var pack = ReadLocalizationPack(path);
            foreach (var key in helpKeys)
            {
                foreach (var term in protectedTerms.Where(term => english[key].Contains(term, StringComparison.OrdinalIgnoreCase)))
                {
                    Assert.Contains(term, pack[key], StringComparison.OrdinalIgnoreCase);
                }
            }
        }
    }

    private static Dictionary<string, string> ReadLocalizationPack(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            Assert.True(values.TryAdd(property.Name, property.Value.GetString() ?? string.Empty), $"Duplicate localization key '{property.Name}' in {path}.");
        }

        return values;
    }

    private static string[] ExtractPlaceholders(string value) =>
        Regex.Matches(value, "\\{\\d+[^}]*\\}")
            .Select(match => match.Value)
            .OrderBy(placeholder => placeholder, StringComparer.Ordinal)
            .ToArray();
}
