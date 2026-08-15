using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    private static readonly Dictionary<string, byte[]> AgentSidecarFiles = new(StringComparer.OrdinalIgnoreCase)
    {
        ["llwmctl.exe"] = Encoding.UTF8.GetBytes("cli-binary"),
        ["AGENTS.md"] = Encoding.UTF8.GetBytes("# comprehensive agent instructions"),
        ["agent.md"] = Encoding.UTF8.GetBytes("# quick agent instructions"),
        ["docs/CONTROL_API.md"] = Encoding.UTF8.GetBytes("# control API"),
        ["LICENSE"] = Encoding.UTF8.GetBytes("project license"),
        ["THIRD-PARTY-NOTICES.md"] = Encoding.UTF8.GetBytes("third-party notices"),
        ["licenses/Apache-2.0.txt"] = Encoding.UTF8.GetBytes("Apache License 2.0"),
        ["licenses/dotnet/LICENSE.txt"] = Encoding.UTF8.GetBytes(".NET license"),
        ["licenses/dotnet/ThirdPartyNotices.txt"] = Encoding.UTF8.GetBytes(".NET notices")
    };

    [Fact]
    public void AgentSidecarBootstrapInstallsEveryVerifiedFile()
    {
        var root = CreateTempRoot();
        try
        {
            using var bundle = CreateAgentSidecarBundle();

            var result = new AgentSidecarBootstrapService().Install(bundle, root);

            Assert.Equal(AgentSidecarBootstrapStatus.Installed, result.Status);
            Assert.Equal(AgentSidecarFiles.Keys.Order(), result.InstalledFiles.Order());
            Assert.Empty(result.CurrentFiles);
            Assert.Null(result.Error);
            foreach (var file in AgentSidecarFiles)
            {
                Assert.Equal(file.Value, File.ReadAllBytes(Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar))));
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentSidecarBootstrapReplacesOutdatedFilesAndSkipsCurrentFiles()
    {
        var root = CreateTempRoot();
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "docs"));
            File.WriteAllBytes(Path.Combine(root, "llwmctl.exe"), AgentSidecarFiles["llwmctl.exe"]);
            File.WriteAllText(Path.Combine(root, "AGENTS.md"), "outdated");
            using var bundle = CreateAgentSidecarBundle();

            var result = new AgentSidecarBootstrapService().Install(bundle, root);

            Assert.Equal(AgentSidecarBootstrapStatus.Installed, result.Status);
            Assert.Contains("llwmctl.exe", result.CurrentFiles);
            Assert.DoesNotContain("llwmctl.exe", result.InstalledFiles);
            Assert.Contains("AGENTS.md", result.InstalledFiles);
            Assert.Equal(AgentSidecarFiles["AGENTS.md"], File.ReadAllBytes(Path.Combine(root, "AGENTS.md")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentSidecarBootstrapRejectsCorruptBundleBeforeInstallingAnything()
    {
        var root = CreateTempRoot();
        try
        {
            var corrupt = AgentSidecarFiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            corrupt["llwmctl.exe"] = Encoding.UTF8.GetBytes("bad-binary");
            using var bundle = CreateAgentSidecarBundle(corrupt, manifestFiles: AgentSidecarFiles);

            var result = new AgentSidecarBootstrapService().Install(bundle, root);

            Assert.Equal(AgentSidecarBootstrapStatus.Failed, result.Status);
            Assert.Contains("SHA-256", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.InstalledFiles);
            Assert.False(File.Exists(Path.Combine(root, "llwmctl.exe")));
            Assert.Empty(Directory.EnumerateDirectories(root, ".llwm-sidecars-*"));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentSidecarBootstrapRejectsUnexpectedArchivePaths()
    {
        var root = CreateTempRoot();
        try
        {
            var archiveFiles = AgentSidecarFiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            archiveFiles["../escape.txt"] = Encoding.UTF8.GetBytes("escape");
            using var bundle = CreateAgentSidecarBundle(archiveFiles, manifestFiles: AgentSidecarFiles);

            var result = new AgentSidecarBootstrapService().Install(bundle, root);

            Assert.Equal(AgentSidecarBootstrapStatus.Failed, result.Status);
            Assert.Contains("unsafe path", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(result.InstalledFiles);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentSidecarBootstrapIsOptionalForDevelopmentBuilds()
    {
        var root = CreateTempRoot();
        try
        {
            var result = new AgentSidecarBootstrapService().InstallEmbedded(typeof(AppUpdateService).Assembly, root);

            Assert.Equal(AgentSidecarBootstrapStatus.BundleUnavailable, result.Status);
            Assert.Empty(result.InstalledFiles);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(AgentSidecarBootstrapStatus.Current, 0)]
    [InlineData(AgentSidecarBootstrapStatus.Installed, 0)]
    [InlineData(AgentSidecarBootstrapStatus.BundleUnavailable, 1)]
    [InlineData(AgentSidecarBootstrapStatus.Failed, 1)]
    public void SidecarVerificationExitCodeReportsFailures(AgentSidecarBootstrapStatus status, int expected)
    {
        Assert.Equal(expected, AgentSidecarBootstrapService.VerificationExitCode(status));
    }

    [Fact]
    public void PortableReleaseEmbedsAndShipsAgentControlSidecars()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var startup = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "App.xaml.cs"));
        var publish = File.ReadAllText(FindRepositoryFile("publish-app.ps1"));
        var releaseGate = File.ReadAllText(FindRepositoryFile("test-release-gate.ps1"));
        var releaseWorkflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "release.yml"));
        var installer = File.ReadAllText(FindRepositoryFile("installer", "LlamaCppWindowsManager.iss"));

        Assert.Contains("LocalLlmConsole.AgentBootstrap.zip", project, StringComparison.Ordinal);
        Assert.Contains("--bootstrap-agent-sidecars-only", startup, StringComparison.Ordinal);
        Assert.Contains("-p:AgentBootstrapBundlePath=$BundleZip", publish, StringComparison.Ordinal);
        Assert.Contains("docs/CONTROL_API.md", publish, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", publish, StringComparison.Ordinal);
        Assert.Contains("licenses/Apache-2.0.txt", publish, StringComparison.Ordinal);
        Assert.Contains("Assert-HashCompanion -Path $controlCli", releaseGate, StringComparison.Ordinal);
        Assert.Contains("-ExpectedEntry \"docs/CONTROL_API.md\"", releaseGate, StringComparison.Ordinal);
        Assert.Contains("-ExpectedEntry \"LICENSE\"", releaseGate, StringComparison.Ordinal);
        Assert.Contains("-IncludePublish -IncludeInstaller", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("dist/installer/LlamaCppWindowsManager-Setup-*-win-x64.exe", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("choco install innosetup --version=6.7.1", releaseWorkflow, StringComparison.Ordinal);
        Assert.True(
            releaseWorkflow.IndexOf("Install pinned Inno Setup", StringComparison.Ordinal)
            < releaseWorkflow.IndexOf("Import code-signing certificate", StringComparison.Ordinal));
        Assert.Contains("\\AGENTS.md\"; DestDir: \"{app}\"", installer, StringComparison.Ordinal);
        Assert.Contains("\\docs\\CONTROL_API.md\"; DestDir: \"{app}\\docs\"", installer, StringComparison.Ordinal);
        Assert.Contains("\\LICENSE\"; DestDir: \"{app}\"", installer, StringComparison.Ordinal);
    }

    [Fact]
    public void AgentInstructionsCoverColdStartRecoveryAndRepositoryWorkflows()
    {
        var instructions = File.ReadAllText(FindRepositoryFile("AGENTS.md"));
        var quickGuide = File.ReadAllText(FindRepositoryFile("agent.md"));

        Assert.Contains("## First contact and cold start", instructions, StringComparison.Ordinal);
        Assert.Contains("## Restart and recovery", instructions, StringComparison.Ordinal);
        Assert.Contains("## Troubleshooting", instructions, StringComparison.Ordinal);
        Assert.Contains("## Working from GitHub or source", instructions, StringComparison.Ordinal);
        Assert.Contains("https://github.com/alekk89/llama-cpp-windows-manager", instructions, StringComparison.Ordinal);
        Assert.Contains("GitHub Releases", instructions, StringComparison.Ordinal);
        Assert.Contains("Release checksum mismatch", instructions, StringComparison.Ordinal);
        Assert.Contains("single-instance", instructions, StringComparison.Ordinal);
        Assert.Contains("Do not launch `llama-server`", instructions, StringComparison.Ordinal);
        Assert.Contains("canonical source repository", quickGuide, StringComparison.Ordinal);
        Assert.DoesNotContain(@"D:\LlamaCppConsole", instructions, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"D:\LlamaCppConsole", quickGuide, StringComparison.OrdinalIgnoreCase);
    }

    private static MemoryStream CreateAgentSidecarBundle(
        IReadOnlyDictionary<string, byte[]>? archiveFiles = null,
        IReadOnlyDictionary<string, byte[]>? manifestFiles = null)
    {
        archiveFiles ??= AgentSidecarFiles;
        manifestFiles ??= archiveFiles;
        var manifest = new
        {
            version = "2.2.0",
            files = manifestFiles.Select(file => new
            {
                path = file.Key,
                size = file.Value.LongLength,
                sha256 = Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant()
            })
        };

        var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in archiveFiles)
            {
                using var output = archive.CreateEntry(file.Key).Open();
                output.Write(file.Value);
            }

            using var manifestOutput = archive.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(manifestOutput, manifest);
        }

        memory.Position = 0;
        return memory;
    }
}
