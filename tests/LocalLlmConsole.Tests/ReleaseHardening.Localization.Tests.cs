using System.Text.Json;
using System.Text.RegularExpressions;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
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
            Assert.Equal(english.Keys.OrderBy(key => key, StringComparer.Ordinal), pack.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.DoesNotContain(pack, pair => string.IsNullOrWhiteSpace(pair.Value));

            foreach (var key in english.Keys)
            {
                Assert.Equal(ExtractPlaceholders(english[key]), ExtractPlaceholders(pack[key]));
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
