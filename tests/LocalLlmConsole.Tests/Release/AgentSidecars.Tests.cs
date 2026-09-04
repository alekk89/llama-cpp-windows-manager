using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class AgentSidecarsTests : ManagerRegressionTestBase
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
    public void AgentSidecarBootstrapFullVerificationChecksBundleWhenTargetsAreCurrent()
    {
        var root = CreateTempRoot();
        try
        {
            foreach (var file in AgentSidecarFiles)
            {
                var path = Path.Combine(root, file.Key.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, file.Value);
            }
            var corrupt = AgentSidecarFiles.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            corrupt["llwmctl.exe"] = Encoding.UTF8.GetBytes("bad-binary");
            using var bundle = CreateAgentSidecarBundle(corrupt, manifestFiles: AgentSidecarFiles);

            var result = new AgentSidecarBootstrapService().Install(bundle, root, verifyBundleContents: true);

            Assert.Equal(AgentSidecarBootstrapStatus.Failed, result.Status);
            Assert.Contains("SHA-256", result.Error, StringComparison.OrdinalIgnoreCase);
            Assert.Equal(AgentSidecarFiles["llwmctl.exe"], File.ReadAllBytes(Path.Combine(root, "llwmctl.exe")));
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
    public void AgentSidecarBootstrapInstallsFromPackagedExecutable()
    {
        var root = CreateTempRoot();
        try
        {
            var executable = Path.Combine(root, "packaged.exe");
            using (var bundle = CreateAgentSidecarBundle())
                PackageAgentBundle(executable, bundle);
            var target = Path.Combine(root, "installed");

            var result = new AgentSidecarBootstrapService().InstallPackaged(executable, target);

            Assert.Equal(AgentSidecarBootstrapStatus.Installed, result.Status);
            Assert.Equal(AgentSidecarFiles.Keys.Order(), result.InstalledFiles.Order());
            Assert.Equal(
                AgentSidecarFiles["llwmctl.exe"],
                File.ReadAllBytes(Path.Combine(target, "llwmctl.exe")));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentSidecarBootstrapFindsPackageBeforeAuthenticodeCertificate()
    {
        var root = CreateTempRoot();
        try
        {
            var executable = Path.Combine(root, "signed-packaged.exe");
            using (var bundle = CreateAgentSidecarBundle())
                PackageAgentBundle(executable, bundle, appendFakeCertificate: true);

            var result = new AgentSidecarBootstrapService().InstallPackaged(
                executable,
                Path.Combine(root, "installed"),
                verifyBundleContents: true);

            Assert.Equal(AgentSidecarBootstrapStatus.Installed, result.Status);
            Assert.Equal(AgentSidecarFiles.Keys.Order(), result.InstalledFiles.Order());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void AgentSidecarBootstrapIsOptionalForDevelopmentExecutables()
    {
        var root = CreateTempRoot();
        try
        {
            var executable = Path.Combine(root, "development.exe");
            File.WriteAllText(executable, "development app without packaged sidecars");

            var result = new AgentSidecarBootstrapService().InstallPackaged(executable, root);

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
        var publish = File.ReadAllText(FindRepositoryFile("scripts", "publish-app.ps1"));
        var releaseGate = File.ReadAllText(FindRepositoryFile("scripts", "test-release-gate.ps1"));
        var releaseWorkflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "release.yml"));
        var installer = File.ReadAllText(FindRepositoryFile("installer", "LlamaCppWindowsManager.iss"));

        Assert.DoesNotContain("LocalLlmConsole.AgentBootstrap.zip", project, StringComparison.Ordinal);
        Assert.Contains("--bootstrap-agent-sidecars-only", startup, StringComparison.Ordinal);
        Assert.Contains("BootstrapPackagedSidecarsAsync", startup, StringComparison.Ordinal);
        Assert.True(
            startup.IndexOf("window.Show();", StringComparison.Ordinal)
            < startup.IndexOf("_ = BootstrapPackagedSidecarsAsync();", StringComparison.Ordinal));
        Assert.Contains("Add-AgentSidecarBundle -ExecutablePath $Exe -BundlePath $BundleZip", publish, StringComparison.Ordinal);
        Assert.Contains(AgentSidecarBootstrapService.PackageMarker, publish, StringComparison.Ordinal);
        Assert.DoesNotContain("AgentBootstrapBundlePath", publish, StringComparison.Ordinal);
        Assert.Contains("docs/CONTROL_API.md", publish, StringComparison.Ordinal);
        Assert.Contains("THIRD-PARTY-NOTICES.md", publish, StringComparison.Ordinal);
        Assert.Contains("licenses/Apache-2.0.txt", publish, StringComparison.Ordinal);
        Assert.Contains("Assert-HashCompanion -Path $controlCli", releaseGate, StringComparison.Ordinal);
        Assert.Contains("--bootstrap-agent-sidecars-only", releaseGate, StringComparison.Ordinal);
        Assert.Contains("\"docs/CONTROL_API.md\"", releaseGate, StringComparison.Ordinal);
        Assert.Contains("\"LICENSE\"", releaseGate, StringComparison.Ordinal);
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

    private static void PackageAgentBundle(
        string executablePath,
        Stream bundle,
        bool appendFakeCertificate = false)
    {
        using var executable = new FileStream(executablePath, FileMode.Create, FileAccess.Write, FileShare.None);
        if (appendFakeCertificate)
        {
            var header = new byte[512];
            header[0] = (byte)'M';
            header[1] = (byte)'Z';
            System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0x3c), 0x80);
            "PE\0\0"u8.CopyTo(header.AsSpan(0x80));
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x80 + 20), 240);
            System.Buffers.Binary.BinaryPrimitives.WriteUInt16LittleEndian(header.AsSpan(0x80 + 24), 0x20b);
            executable.Write(header);
        }
        else
        {
            executable.Write(Encoding.ASCII.GetBytes("test executable prefix"));
        }
        bundle.CopyTo(executable);
        Span<byte> length = stackalloc byte[sizeof(long)];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64LittleEndian(length, bundle.Length);
        executable.Write(length);
        executable.Write(Encoding.ASCII.GetBytes(AgentSidecarBootstrapService.PackageMarker));
        if (!appendFakeCertificate) return;

        while (executable.Position % 8 != 0) executable.WriteByte(0);
        var certificateOffset = checked((uint)executable.Position);
        const uint certificateSize = 16;
        executable.Write(new byte[certificateSize]);
        executable.Position = 0x80 + 24 + 112 + (4 * 8);
        Span<byte> certificateEntry = stackalloc byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(certificateEntry, certificateOffset);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(certificateEntry[4..], certificateSize);
        executable.Write(certificateEntry);
    }
}
