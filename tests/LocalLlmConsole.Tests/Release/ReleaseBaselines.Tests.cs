using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LocalLlmConsole.Tests;

public sealed class ReleaseBaselinesTests : ManagerRegressionTestBase
{
    private static readonly Regex DefaultBaselinePath =
        new(@"\[string\] \$BaselinePath = ""(?<path>tests/release-baselines/[^""]+)""", RegexOptions.CultureInvariant);

    [Fact]
    public void InstallerFallbackVersionMatchesProjectVersions()
    {
        var installer = File.ReadAllText(FindRepositoryFile("installer", "LlamaCppWindowsManager.iss"));
        var fallback = Regex.Match(installer, @"#define AppVersion ""(?<version>[^""]+)""");
        Assert.True(fallback.Success, "Installer must define a fallback AppVersion.");

        var appVersion = ProjectVersion("LocalLlmConsole.App", "LocalLlmConsole.App.csproj");
        Assert.Equal(appVersion, ProjectVersion("LocalLlmConsole.ControlCli", "LocalLlmConsole.ControlCli.csproj"));
        Assert.Equal(appVersion, fallback.Groups["version"].Value);
    }

    [Fact]
    public void UpgradeScriptsDefaultToNewestPublishedBaseline()
    {
        var newest = Baselines().MaxBy(baseline => baseline.Version)!;
        var projectVersion = Version.Parse(ProjectVersion("LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        Assert.True(newest.Version < projectVersion, $"Baseline {newest.Version} must precede project version {projectVersion}.");

        foreach (var script in new[] { "test-previous-version-upgrade.ps1", "test-portable-update.ps1" })
        {
            var match = DefaultBaselinePath.Match(File.ReadAllText(FindRepositoryFile("scripts", script)));
            Assert.True(match.Success, $"{script} must declare a default baseline.");
            Assert.Equal($"tests/release-baselines/v{newest.Version}.json", match.Groups["path"].Value);
        }
    }

    [Fact]
    public void EveryBaselinePinsPublishedInstallerAndOnePortableAsset()
    {
        foreach (var baseline in Baselines())
        {
            var root = baseline.Document.RootElement;
            var tag = $"v{baseline.Version}";
            Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
            Assert.Equal(tag, root.GetProperty("tag").GetString());
            Assert.True(DateTimeOffset.TryParse(root.GetProperty("publishedAtUtc").GetString(), out _), tag);
            AssertPinnedAsset(tag, root.GetProperty("installer"));

            var portableAssets = new[] { "portableExecutable", "portableArchive" }
                .Where(name => root.TryGetProperty(name, out _))
                .ToArray();
            var portableKind = Assert.Single(portableAssets);
            var portable = root.GetProperty(portableKind);
            AssertPinnedAsset(tag, portable);
            if (portableKind == "portableExecutable")
                Assert.Equal("LlamaCppWindowsManager.exe", portable.GetProperty("name").GetString());
            else
                Assert.EndsWith(".zip", portable.GetProperty("name").GetString(), StringComparison.Ordinal);
        }
    }

    private static void AssertPinnedAsset(string tag, JsonElement asset)
    {
        var name = asset.GetProperty("name").GetString()!;
        Assert.Equal(
            $"https://github.com/alekk89/llama-cpp-windows-manager/releases/download/{tag}/{name}",
            asset.GetProperty("url").GetString());
        Assert.True(asset.GetProperty("size").GetInt64() > 1_000_000, $"{tag} {name} size");
        Assert.Matches("^[0-9a-f]{64}$", asset.GetProperty("sha256").GetString());
    }

    private static string ProjectVersion(params string[] projectPath) =>
        XDocument.Load(FindRepositoryFile(["src", .. projectPath])).Descendants("Version").First().Value.Trim();

    private static List<Baseline> Baselines()
    {
        var directory = Path.GetDirectoryName(FindRepositoryFile("tests", "release-baselines", "v2.6.0.json"))!;
        var baselines = Directory.EnumerateFiles(directory, "v*.json")
            .Select(path =>
            {
                var document = JsonDocument.Parse(File.ReadAllText(path));
                var version = Version.Parse(document.RootElement.GetProperty("version").GetString()!);
                Assert.Equal($"v{version}.json", Path.GetFileName(path));
                return new Baseline(version, document);
            })
            .ToList();
        Assert.NotEmpty(baselines);
        return baselines;
    }

    private sealed record Baseline(Version Version, JsonDocument Document);
}
