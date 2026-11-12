using System.Text.Json;
using System.Xml.Linq;

namespace LocalLlmConsole.Tests;

public sealed class SdkSelectionTests : ManagerRegressionTestBase
{
    [Fact]
    public void GlobalJsonSelectsExactSdkForLockedAotPublish()
    {
        using var globalJson = JsonDocument.Parse(File.ReadAllText(FindRepositoryFile("global.json")));
        var sdk = globalJson.RootElement.GetProperty("sdk");

        Assert.Matches(@"^\d+\.\d+\.\d{3}$", sdk.GetProperty("version").GetString());
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());
    }

    [Fact]
    public void ControlCliPublishLockPinsOneIlCompilerVersionMatchingServicingPackages()
    {
        using var lockFile = JsonDocument.Parse(File.ReadAllText(
            FindRepositoryFile("src", "LocalLlmConsole.ControlCli", "packages.publish.lock.json")));
        var compilerVersions = lockFile.RootElement.GetProperty("dependencies")
            .EnumerateObject()
            .SelectMany(target => target.Value.EnumerateObject())
            .Where(package => package.Name.EndsWith("Microsoft.DotNet.ILCompiler", StringComparison.Ordinal))
            .Select(package => package.Value.GetProperty("resolved").GetString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var compilerVersion = Assert.Single(compilerVersions);
        var project = XDocument.Load(FindRepositoryFile("src", "LocalLlmConsole.ControlCli", "LocalLlmConsole.ControlCli.csproj"));
        var servicingVersion = project.Descendants("PackageReference")
            .Single(reference => (string?)reference.Attribute("Include") == "System.Security.Cryptography.ProtectedData")
            .Attribute("Version")!.Value;

        // The implicit ILCompiler version follows the pinned SDK's runtime. SDK bumps
        // must move the runtime-versioned package references and locks together.
        Assert.Equal(servicingVersion, compilerVersion);
    }
}
