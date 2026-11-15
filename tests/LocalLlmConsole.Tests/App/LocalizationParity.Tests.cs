using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalLlmConsole.Tests;

/// <summary>
/// Keeps every language pack in key parity with English. Known gaps live in
/// tests/LocalLlmConsole.Tests/App/missing-translation-baseline.json and may only shrink.
/// Set LLWM_UPDATE_TRANSLATION_BASELINE=1 and run this test to regenerate the baseline.
/// </summary>
public sealed class LocalizationParityTests : ManagerRegressionTestBase
{
    private const string UpdateBaselineVariable = "LLWM_UPDATE_TRANSLATION_BASELINE";

    [Fact]
    public void EveryLanguagePackHasEveryEnglishKeyExceptTheShrinkingBaseline()
    {
        var localizationRoot = LocalizationRoot();
        var english = ReadKeys(Path.Combine(localizationRoot, "Strings.en.json"));
        var actual = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        var extraKeys = new List<string>();
        foreach (var path in Directory.GetFiles(localizationRoot, "Strings.*.json").Order(StringComparer.Ordinal))
        {
            var code = Path.GetFileNameWithoutExtension(path).Split('.')[1];
            if (code == "en") continue;
            var pack = ReadKeys(path);
            extraKeys.AddRange(pack.Where(key => !english.Contains(key)).Select(key => $"{code}: {key}"));
            actual[code] = english.Where(key => !pack.Contains(key)).Order(StringComparer.Ordinal).ToArray();
        }

        Assert.True(extraKeys.Count == 0, "Language packs contain keys missing from Strings.en.json:\n" + string.Join("\n", extraKeys));

        var baselinePath = BaselinePath();
        if (Environment.GetEnvironmentVariable(UpdateBaselineVariable) == "1")
        {
            File.WriteAllText(baselinePath, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
            return;
        }

        var baseline = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(baselinePath))
                       ?? throw new InvalidOperationException("The missing-translation baseline is empty.");
        var newGaps = new List<string>();
        var resolvedGaps = new List<string>();
        foreach (var (code, missing) in actual)
        {
            var allowed = baseline.TryGetValue(code, out var keys) ? keys.ToHashSet(StringComparer.Ordinal) : [];
            newGaps.AddRange(missing.Where(key => !allowed.Contains(key)).Select(key => $"{code}: {key}"));
            resolvedGaps.AddRange(allowed.Where(key => !missing.Contains(key, StringComparer.Ordinal)).Select(key => $"{code}: {key}"));
        }
        resolvedGaps.AddRange(baseline.Keys.Where(code => !actual.ContainsKey(code)).Select(code => $"{code}: (language pack removed)"));

        Assert.True(newGaps.Count == 0,
            "Add translations for these keys (or, only if unavoidable, regenerate the baseline as described in docs/DEVELOPMENT.md):\n"
            + string.Join("\n", newGaps));
        Assert.True(resolvedGaps.Count == 0,
            $"These keys are now translated; remove them from the baseline (set {UpdateBaselineVariable}=1 and rerun this test):\n"
            + string.Join("\n", resolvedGaps));
    }

    [Fact]
    public void BenchmarksUiDoesNotIntroduceHardCodedEnglish()
    {
        var uiRoot = Path.Combine(RepositoryRoot.Value, "src", "LocalLlmConsole.App", "Ui");
        var files = Directory.GetFiles(Path.Combine(uiRoot, "Pages", "Benchmarks"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(uiRoot, "Shell", "MainWindow", "Benchmarks"), "*.cs"))
            .ToArray();
        // Speculative type labels mirror llama.cpp --spec-type names and are intentionally not translated.
        var technicalNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Atomic MTP", "Draft MTP", "Draft simple", "Draft Eagle 3", "Draft DFlash", "Draft DFlash2", "Draft LiliCorr",
            "Draft DSpark", "N-gram simple", "N-gram map K", "N-gram map K4V", "N-gram mod", "N-gram cache"
        };
        var literal = new Regex("(?<![@\\w])\\$?\"((?:[^\"\\\\\\n]|\\\\.)*)\"", RegexOptions.CultureInvariant);
        var prose = new Regex("[A-Za-z]{2,}[ ,]+[A-Za-z]{2,}", RegexOptions.CultureInvariant);
        var uiCall = new Regex(
            "(?<!BenchmarkFieldDescriptions\\.)\\b(?:Button|Action|Field|Label|Muted|Check|MenuAction|Combo|Choice|MatrixChoice|BooleanChoice|Section|FramedSection|GridSection|SetStatus|Text)\\(\\s*\\$?\"[A-Za-z]"
            + "|\\bSet(?:Name|HelpText)\\([^,]+,\\s*\\$?\"[A-Za-z]"
            + "|\\(\\s*\\$?\"[A-Za-z][^\"]*\",\\s*nameof\\("
            + "|\\b(?:Text|Content|Header|ToolTip|Title)\\s*=\\s*\\$?\"[A-Za-z]",
            RegexOptions.CultureInvariant);
        var violations = new List<string>();
        foreach (var path in files)
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                var line = lines[index];
                if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                var location = $"{Path.GetFileName(path)}:{index + 1}";
                violations.AddRange(literal.Matches(line)
                    .Where(match => !technicalNames.Contains(match.Groups[1].Value)
                                    && prose.IsMatch(Regex.Replace(match.Groups[1].Value, "\\{[^}]*\\}", "")))
                    .Select(match => $"{location}: {match.Value}"));
                violations.AddRange(uiCall.Matches(line).Select(match => $"{location}: {match.Value}"));
            }
        }

        Assert.True(violations.Count == 0, "Move these Benchmarks UI strings to Loc.T keys:\n" + string.Join("\n", violations));
    }

    [Fact]
    public void BenchmarkKeysReferencedByIdentifierExistInEnglish()
    {
        var english = ReadKeys(Path.Combine(LocalizationRoot(), "Strings.en.json"));
        var uiRoot = Path.Combine(RepositoryRoot.Value, "src", "LocalLlmConsole.App", "Ui", "Pages", "Benchmarks");
        var fieldId = new Regex(
            "(?:AddGuidedField\\([^,]+,\\s*\\d+,\\s*|AddGuidedWideField\\([^,]+,\\s*|BenchmarkFieldDescriptions\\.(?:Label|Get)\\()\"(?<id>\\w+)\"",
            RegexOptions.CultureInvariant);
        var ids = Directory.GetFiles(uiRoot, "*.cs")
            .SelectMany(path => fieldId.Matches(File.ReadAllText(path)).Select(match => match.Groups["id"].Value))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var expected = ids.SelectMany(id => new[] { $"Benchmarks.Field.{id}", $"Benchmarks.Field.{id}.Help" })
            .Concat(new[] { "QuickCheck", "Standard", "Short", "Medium", "Long", "Custom" }.Select(id => $"Benchmarks.Preset.{id}"))
            .Concat(Enum.GetNames<LocalLlmConsole.Models.BenchmarkFailurePolicy>().Select(name => $"Benchmarks.FailurePolicy.{name}"))
            .Concat(Enum.GetNames<LocalLlmConsole.Models.JobStatus>().Select(name => $"Benchmarks.Status.{name}"))
            .Concat(new[] { "ContextLength", "BatchSize", "MicroBatchSize", "FlashAttention", "CacheType", "GpuLayer", "ThreadCount", "KvOffload", "MultiGpu", "Speculative" }
                .SelectMany(id => new[] { $"Benchmarks.Dimension.{id}.One", $"Benchmarks.Dimension.{id}.Other" }));

        Assert.NotEmpty(ids);
        Assert.DoesNotContain(expected, key => !english.Contains(key));
    }

    /// <summary>
    /// A value identical to English is untranslated unless it is legitimately the same in that
    /// language (product names, units, flags, shared technical terms). Those reviewed values live in
    /// identical-to-english-allowlist.json, which may only shrink; anything else must be translated.
    /// </summary>
    [Fact]
    public void ValuesRepeatEnglishOnlyWhereReviewed()
    {
        var localizationRoot = LocalizationRoot();
        var english = ReadValues(Path.Combine(localizationRoot, "Strings.en.json"));
        var actual = new SortedDictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var path in Directory.GetFiles(localizationRoot, "Strings.*.json").Order(StringComparer.Ordinal))
        {
            var code = Path.GetFileNameWithoutExtension(path).Split('.')[1];
            if (code == "en") continue;
            var pack = ReadValues(path);
            actual[code] = pack.Where(pair => english.TryGetValue(pair.Key, out var value) && value == pair.Value)
                .Select(pair => pair.Key).Order(StringComparer.Ordinal).ToArray();
        }

        var allowlistPath = IdenticalAllowlistPath();
        if (Environment.GetEnvironmentVariable(UpdateBaselineVariable) == "1")
        {
            File.WriteAllText(allowlistPath, JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true, NewLine = "\n" }) + "\n");
            return;
        }

        var allowlist = JsonSerializer.Deserialize<Dictionary<string, string[]>>(File.ReadAllText(allowlistPath))
                        ?? throw new InvalidOperationException("The identical-to-English allowlist is empty.");
        var untranslated = new List<string>();
        var nowTranslated = new List<string>();
        foreach (var (code, identical) in actual)
        {
            var allowed = allowlist.TryGetValue(code, out var keys) ? keys.ToHashSet(StringComparer.Ordinal) : [];
            untranslated.AddRange(identical.Where(key => !allowed.Contains(key)).Select(key => $"{code}: {key} = \"{english[key]}\""));
            nowTranslated.AddRange(allowed.Where(key => !identical.Contains(key, StringComparer.Ordinal)).Select(key => $"{code}: {key}"));
        }

        Assert.True(untranslated.Count == 0,
            "These values are copied from English; translate them (or, only if the text is genuinely the same in that language, regenerate the allowlist as described in docs/DEVELOPMENT.md):\n"
            + string.Join("\n", untranslated));
        Assert.True(nowTranslated.Count == 0,
            $"These values are no longer identical to English; remove them from the allowlist (set {UpdateBaselineVariable}=1 and rerun):\n"
            + string.Join("\n", nowTranslated));
    }

    private static string IdenticalAllowlistPath()
        => Path.Combine(RepositoryRoot.Value, "tests", "LocalLlmConsole.Tests", "App", "identical-to-english-allowlist.json");

    private static Dictionary<string, string> ReadValues(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value.GetString() ?? "", StringComparer.Ordinal);
    }

    private static string LocalizationRoot()
        => Path.GetDirectoryName(FindRepositoryFile("src", "LocalLlmConsole.App", "Localization", "Strings.en.json"))!;

    private static string BaselinePath()
        => Path.Combine(RepositoryRoot.Value, "tests", "LocalLlmConsole.Tests", "App", "missing-translation-baseline.json");

    private static HashSet<string> ReadKeys(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        return document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
    }
}
