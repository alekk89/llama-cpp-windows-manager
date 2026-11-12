using System.Text;
using LocalLlmConsole.Services;
using static LocalLlmConsole.Tests.AgentSidecarsTests;

namespace LocalLlmConsole.Tests;

public sealed class AgentSidecarOwnershipTests : ManagerRegressionTestBase
{
    private const string LegacyInstructions = "# llama.cpp Windows Manager operator instructions\n\nold release text";

    [Fact]
    public void ForeignFilesBesideTheExecutableArePreservedAndReported()
    {
        var root = CreateTempRoot();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "# my repository instructions");
        File.WriteAllText(Path.Combine(root, "LICENSE"), "my project license");
        Directory.CreateDirectory(Path.Combine(root, "licenses", "Apache-2.0.txt"));

        var first = Install(root);
        var second = Install(root);

        Assert.Equal(AgentSidecarBootstrapStatus.ForeignFilesPreserved, first.Status);
        Assert.Equal(["AGENTS.md", "LICENSE", "licenses/Apache-2.0.txt"], first.PreservedFiles.Order(StringComparer.Ordinal));
        Assert.Equal(AgentSidecarFiles.Count - 3, first.InstalledFiles.Count);
        Assert.Equal("# my repository instructions", File.ReadAllText(Path.Combine(root, "AGENTS.md")));
        Assert.Equal("my project license", File.ReadAllText(Path.Combine(root, "LICENSE")));
        Assert.True(Directory.Exists(Path.Combine(root, "licenses", "Apache-2.0.txt")));
        Assert.Equal(AgentSidecarFiles["agent.md"], File.ReadAllBytes(Path.Combine(root, "agent.md")));
        Assert.Equal(1, AgentSidecarBootstrapService.VerificationExitCode(first.Status));
        var problem = AgentSidecarBootstrapService.DescribeProblem(first);
        Assert.NotNull(problem);
        Assert.Contains("AGENTS.md", problem, StringComparison.Ordinal);
        Assert.Contains("LICENSE", problem, StringComparison.Ordinal);

        Assert.Equal(AgentSidecarBootstrapStatus.ForeignFilesPreserved, second.Status);
        Assert.Empty(second.InstalledFiles);
        Assert.Equal(AgentSidecarFiles.Count - 3, second.CurrentFiles.Count);
        Assert.Equal("my project license", File.ReadAllText(Path.Combine(root, "LICENSE")));
        var record = File.ReadAllText(Path.Combine(root, AgentSidecarOwnership.RecordFileName));
        Assert.Contains("agent.md", record, StringComparison.Ordinal);
        Assert.Contains("\"preserved\"", record, StringComparison.Ordinal);
    }

    [Fact]
    public void ManagerInstalledFileEditedByTheUserIsNotReplacedByLaterRelease()
    {
        var root = CreateTempRoot();
        Assert.Equal(AgentSidecarBootstrapStatus.Installed, Install(root).Status);
        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "# my local notes");

        var upgrade = Install(root, Release("AGENTS.md", "agent.md", "llwmctl.exe"));

        Assert.Equal(AgentSidecarBootstrapStatus.ForeignFilesPreserved, upgrade.Status);
        Assert.Equal(["AGENTS.md"], upgrade.PreservedFiles);
        Assert.Equal(["agent.md", "llwmctl.exe"], upgrade.InstalledFiles.Order(StringComparer.Ordinal));
        Assert.Equal("# my local notes", File.ReadAllText(Path.Combine(root, "AGENTS.md")));
        Assert.Equal("agent.md v2", File.ReadAllText(Path.Combine(root, "agent.md")));
    }

    [Fact]
    public void RecordedSidecarsAreReplacedAcrossSuccessiveReleases()
    {
        var root = CreateTempRoot();
        Assert.Equal(AgentSidecarBootstrapStatus.Installed, Install(root).Status);

        var second = Install(root, Release("AGENTS.md", "LICENSE"));
        var third = Install(root, Release(3, "AGENTS.md", "docs/CONTROL_API.md"));

        Assert.Equal(AgentSidecarBootstrapStatus.Installed, second.Status);
        Assert.Equal(["AGENTS.md", "LICENSE"], second.InstalledFiles.Order(StringComparer.Ordinal));
        Assert.Equal(AgentSidecarBootstrapStatus.Installed, third.Status);
        // LICENSE returns to the original text because the Manager recorded installing its v2 content.
        Assert.Equal(["AGENTS.md", "LICENSE", "docs/CONTROL_API.md"], third.InstalledFiles.Order(StringComparer.Ordinal));
        Assert.Equal("project license", File.ReadAllText(Path.Combine(root, "LICENSE")));
        Assert.Equal("AGENTS.md v3", File.ReadAllText(Path.Combine(root, "AGENTS.md")));
        Assert.Empty(third.PreservedFiles);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(".git", false)]
    [InlineData("LocalLlmConsole.sln", false)]
    [InlineData("global.json", false)]
    public void PreviousReleaseWithoutOwnershipRecordIsAdoptedOnlyOutsideSourceCheckouts(string? sourceMarker, bool adopted)
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "llwmctl.exe"), "old cli");
        File.WriteAllText(Path.Combine(root, "AGENTS.md"), LegacyInstructions);
        File.WriteAllText(Path.Combine(root, "LICENSE"), "old license");
        if (sourceMarker == ".git") Directory.CreateDirectory(Path.Combine(root, ".git"));
        else if (sourceMarker is not null) File.WriteAllText(Path.Combine(root, sourceMarker), "{}");

        var result = Install(root);

        if (adopted)
        {
            Assert.Equal(AgentSidecarBootstrapStatus.Installed, result.Status);
            Assert.Empty(result.PreservedFiles);
            Assert.Equal(AgentSidecarFiles["llwmctl.exe"], File.ReadAllBytes(Path.Combine(root, "llwmctl.exe")));
            Assert.Equal(AgentSidecarFiles["LICENSE"], File.ReadAllBytes(Path.Combine(root, "LICENSE")));
            Assert.True(File.Exists(Path.Combine(root, AgentSidecarOwnership.RecordFileName)));
        }
        else
        {
            Assert.Equal(AgentSidecarBootstrapStatus.ForeignFilesPreserved, result.Status);
            Assert.Equal(["AGENTS.md", "LICENSE", "llwmctl.exe"], result.PreservedFiles.Order(StringComparer.Ordinal));
            Assert.Equal("old cli", File.ReadAllText(Path.Combine(root, "llwmctl.exe")));
            Assert.Equal(LegacyInstructions, File.ReadAllText(Path.Combine(root, "AGENTS.md")));
        }
    }

    [Fact]
    public void PreviousReleaseWithOnlyControlCliIsAdopted()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "llwmctl.exe"), "old cli");

        var result = Install(root);

        Assert.Equal(AgentSidecarBootstrapStatus.Installed, result.Status);
        Assert.Equal(AgentSidecarFiles["llwmctl.exe"], File.ReadAllBytes(Path.Combine(root, "llwmctl.exe")));
    }

    [Fact]
    public void FolderWithoutManagerInstructionsIsNotTreatedAsPreviousRelease()
    {
        var root = CreateTempRoot();
        File.WriteAllText(Path.Combine(root, "llwmctl.exe"), "some other tool");
        File.WriteAllText(Path.Combine(root, "AGENTS.md"), "# Another project");

        var result = Install(root);

        Assert.Equal(["AGENTS.md", "llwmctl.exe"], result.PreservedFiles.Order(StringComparer.Ordinal));
        Assert.Equal("some other tool", File.ReadAllText(Path.Combine(root, "llwmctl.exe")));
    }

    [Fact]
    public void UnreadableOwnershipRecordGrantsNoOwnership()
    {
        var root = CreateTempRoot();
        Assert.Equal(AgentSidecarBootstrapStatus.Installed, Install(root).Status);
        File.WriteAllText(Path.Combine(root, AgentSidecarOwnership.RecordFileName), "{ not json");
        File.WriteAllText(Path.Combine(root, "agent.md"), "user content");

        var result = Install(root);

        Assert.Equal(["agent.md"], result.PreservedFiles);
        Assert.Equal("user content", File.ReadAllText(Path.Combine(root, "agent.md")));
    }

    private static AgentSidecarBootstrapResult Install(string root, IReadOnlyDictionary<string, byte[]>? files = null)
    {
        using var bundle = CreateAgentSidecarBundle(files);
        return new AgentSidecarBootstrapService().Install(bundle, root);
    }

    private static Dictionary<string, byte[]> Release(params string[] changed) => Release(2, changed);

    private static Dictionary<string, byte[]> Release(int version, params string[] changed)
    {
        var files = new Dictionary<string, byte[]>(AgentSidecarFiles, StringComparer.OrdinalIgnoreCase);
        foreach (var path in changed)
            files[path] = Encoding.UTF8.GetBytes($"{path} v{version}");
        return files;
    }
}
