using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Reflection;

namespace LocalLlmConsole.Tests;


public sealed class ReleaseRepositoryTests : ManagerRegressionTestBase
{
    [Fact]
    public void ApplicationHasNoOpenCodeIntegrationSurface()
    {
        var appRoot = Path.GetDirectoryName(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"))!;
        var source = string.Join(
            "\n",
            Directory.EnumerateFiles(appRoot, "*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".xaml", StringComparison.OrdinalIgnoreCase))
                .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                    && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
                .Select(File.ReadAllText));

        var serviceDirectory = Path.Combine(appRoot, "Services", "OpenCode");
        var pageDirectory = Path.Combine(appRoot, "Ui", "Pages", "OpenCode");
        Assert.False(Directory.Exists(serviceDirectory) && Directory.EnumerateFiles(serviceDirectory, "*", SearchOption.AllDirectories).Any());
        Assert.False(Directory.Exists(pageDirectory) && Directory.EnumerateFiles(pageDirectory, "*", SearchOption.AllDirectories).Any());
        Assert.DoesNotContain("OpenCode", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ProjectDeclaresVersionTwoThreeMetadata()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));

        Assert.Contains("<Version>2.5.0</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>2.5.0.0</AssemblyVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>2.5.0.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>v2.5.0</InformationalVersion>", project, StringComparison.Ordinal);
    }

    [Fact]
    public void ThirdPartyNoticesMatchDeclaredDatabaseDependencies()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var notices = File.ReadAllText(FindRepositoryFile("THIRD-PARTY-NOTICES.md"));
        var apache = File.ReadAllText(FindRepositoryFile("licenses", "Apache-2.0.txt"));

        Assert.Contains("Microsoft.Data.Sqlite\" Version=\"10.0.11\"", project, StringComparison.Ordinal);
        Assert.Contains("SQLitePCLRaw.bundle_e_sqlite3\" Version=\"3.0.5\"", project, StringComparison.Ordinal);
        Assert.Contains("Microsoft.Data.Sqlite 10.0.11", notices, StringComparison.Ordinal);
        Assert.Contains("SQLitePCLRaw.bundle_e_sqlite3", notices, StringComparison.Ordinal);
        Assert.Contains("3.0.5", notices, StringComparison.Ordinal);
        Assert.Contains("SQLite 3.53.4", notices, StringComparison.Ordinal);
        Assert.Contains("licenses/Apache-2.0.txt", notices, StringComparison.Ordinal);
        Assert.Contains("TERMS AND CONDITIONS FOR USE, REPRODUCTION, AND DISTRIBUTION", apache, StringComparison.Ordinal);
        Assert.Contains("END OF TERMS AND CONDITIONS", apache, StringComparison.Ordinal);
    }


    [Fact]
    public void ReleaseDocsAndScriptsUseLaunchBranding()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var buildScript = File.ReadAllText(FindRepositoryFile("scripts", "build-app.ps1"));
        var publishScript = File.ReadAllText(FindRepositoryFile("scripts", "publish-app.ps1"));
        var startScript = File.ReadAllText(FindRepositoryFile("scripts", "start-app.ps1"));
        var architecture = File.ReadAllText(FindRepositoryFile("docs", "ARCHITECTURE.md"));
        var license = File.ReadAllText(FindRepositoryFile("LICENSE"));
        var publicDocs = string.Join(
            "\n",
            Directory.EnumerateFiles(Path.GetDirectoryName(FindRepositoryFile("docs", "ARCHITECTURE.md"))!, "*.md", SearchOption.TopDirectoryOnly)
                .Select(File.ReadAllText));

        Assert.StartsWith("# llama.cpp Windows Manager", readme, StringComparison.Ordinal);
        Assert.Contains("unofficial community project", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LlamaCppWindowsManager.exe", readme, StringComparison.Ordinal);
        Assert.Contains("MIT License", license, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_WINDOWS_MANAGER_DOTNET", buildScript, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_CONSOLE_DOTNET", buildScript, StringComparison.Ordinal);
        Assert.Contains("LlamaCppWindowsManager-$Runtime", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("LlamaCppConsole.exe", publishScript, StringComparison.Ordinal);
        Assert.Contains("LlamaCppWindowsManager-$Runtime.zip", publishScript, StringComparison.Ordinal);
        Assert.Contains("[string] $TimestampServer = \"https://timestamp.digicert.com\"", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("http://timestamp.digicert.com", publishScript, StringComparison.Ordinal);
        Assert.Contains("sha256", publishScript, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LlamaCppWindowsManager.exe", startScript, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_WINDOWS_MANAGER_DOTNET", publishScript, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_CONSOLE_DOTNET", publishScript, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_WINDOWS_MANAGER_WORKSPACE", architecture, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_CONSOLE_WORKSPACE", architecture, StringComparison.Ordinal);
        Assert.Equal("LlamaCppWindowsManager.exe", AppUpdateService.PortableExeName);
        Assert.DoesNotContain("MainWindow.RuntimeJobLogPreview.cs", architecture, StringComparison.Ordinal);
        Assert.DoesNotContain("# Local LLM Console", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Local LLM Console", buildScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Local LLM Console", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Local LLM Console", startScript, StringComparison.Ordinal);
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("LocalLlmConsole.sln"))!;
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.DoesNotContain(repositoryRoot, publicDocs, StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(userProfile))
            Assert.DoesNotContain(userProfile, publicDocs, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepositoryDefinesAutomatedCiGate()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "ci.yml"));
        var releaseWorkflow = File.ReadAllText(FindRepositoryFile(".github", "workflows", "release.yml"));
        var globalJson = File.ReadAllText(FindRepositoryFile("global.json"));
        var editorConfig = File.ReadAllText(FindRepositoryFile(".editorconfig"));
        var gitAttributes = File.ReadAllText(FindRepositoryFile(".gitattributes"));
        var solution = File.ReadAllText(FindRepositoryFile("LocalLlmConsole.sln"));
        var releaseGate = File.ReadAllText(FindRepositoryFile("scripts", "test-release-gate.ps1"));
        var previousVersionUpgrade = File.ReadAllText(FindRepositoryFile("scripts", "test-previous-version-upgrade.ps1"));
        var installerSmoke = File.ReadAllText(FindRepositoryFile("scripts", "test-installer-smoke.ps1"));
        var development = File.ReadAllText(FindRepositoryFile("docs", "DEVELOPMENT.md"));

        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.Matches(@"actions/checkout@[0-9a-f]{40}\s+# v7", workflow);
        Assert.Matches(@"actions/setup-dotnet@[0-9a-f]{40}\s+# v6", workflow);
        Assert.Contains("permissions:", workflow, StringComparison.Ordinal);
        Assert.Contains("contents: read", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: true", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\build-app.ps1 -Restore -LockedRestore", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\test-coverage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet format LocalLlmConsole.sln --verify-no-changes --verbosity minimal", workflow, StringComparison.Ordinal);
        Assert.Contains("git diff --check", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\test-vulnerabilities.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("checksum was not produced", workflow, StringComparison.Ordinal);
        Assert.Contains("package --vulnerable --include-transitive --format json", File.ReadAllText(FindRepositoryFile("scripts", "test-vulnerabilities.ps1")), StringComparison.Ordinal);
        Assert.Contains("package --outdated --format json", File.ReadAllText(FindRepositoryFile("scripts", "test-vulnerabilities.ps1")), StringComparison.Ordinal);
        Assert.Contains("package --deprecated --include-transitive --format json", File.ReadAllText(FindRepositoryFile("scripts", "test-vulnerabilities.ps1")), StringComparison.Ordinal);
        Assert.Contains(".\\scripts\\publish-app.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("test-installer-smoke.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("sbom.spdx.json", workflow, StringComparison.Ordinal);
        Assert.True(File.Exists(FindRepositoryFile(".github", "workflows", "codeql.yml")));
        Assert.True(File.Exists(FindRepositoryFile(".github", "workflows", "dependency-review.yml")));
        Assert.True(File.Exists(FindRepositoryFile(".github", "dependabot.yml")));
        Assert.Contains("\"version\": \"10.0.400\"", globalJson, StringComparison.Ordinal);
        Assert.Contains("\"runner\": \"Microsoft.Testing.Platform\"", globalJson, StringComparison.Ordinal);
        Assert.Contains("TreatWarningsAsErrors", File.ReadAllText(FindRepositoryFile("Directory.Build.props")), StringComparison.Ordinal);
        Assert.Contains("root = true", editorConfig, StringComparison.Ordinal);
        Assert.Contains("*.ps1 text eol=lf", gitAttributes, StringComparison.Ordinal);
        Assert.Contains("*.iss text eol=lf", gitAttributes, StringComparison.Ordinal);
        Assert.Contains(".gitattributes text eol=lf", gitAttributes, StringComparison.Ordinal);
        Assert.Contains("[*.{ps1,iss}]", editorConfig, StringComparison.Ordinal);
        Assert.Contains("LocalLlmConsole.App.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("LocalLlmConsole.Tests.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("LocalLlmConsole.UiTests.csproj", solution, StringComparison.Ordinal);
        Assert.Contains("build-app.ps1", releaseGate, StringComparison.Ordinal);
        Assert.Contains("test-coverage.ps1", releaseGate, StringComparison.Ordinal);
        Assert.Contains("MinimumServiceLineCoverage = 80.0", File.ReadAllText(FindRepositoryFile("scripts", "test-coverage.ps1")), StringComparison.Ordinal);
        Assert.Contains("MinimumModelLineCoverage = 95.0", File.ReadAllText(FindRepositoryFile("scripts", "test-coverage.ps1")), StringComparison.Ordinal);
        Assert.Contains("Skipped or not-executed tests are not allowed", File.ReadAllText(FindRepositoryFile("scripts", "test-coverage.ps1")), StringComparison.Ordinal);
        Assert.Contains("LocalLlmConsole.App/", File.ReadAllText(FindRepositoryFile("scripts", "test-coverage.ps1")), StringComparison.Ordinal);
        Assert.Contains("dotnet format", releaseGate, StringComparison.Ordinal);
        Assert.Contains("git -C $RepoRoot diff --check", releaseGate, StringComparison.Ordinal);
        Assert.Contains("test-vulnerabilities.ps1", releaseGate, StringComparison.Ordinal);
        Assert.Contains("IncludePublish", releaseGate, StringComparison.Ordinal);
        Assert.Contains("publish-app.ps1", releaseGate, StringComparison.Ordinal);
        Assert.Contains("IncludeInstaller", releaseGate, StringComparison.Ordinal);
        Assert.Contains("build-installer.ps1", releaseGate, StringComparison.Ordinal);
        Assert.Contains("CertificateThumbprint", releaseGate, StringComparison.Ordinal);
        Assert.Contains("RequireSigned", releaseGate, StringComparison.Ordinal);
        Assert.Contains("Verify publish artifacts", releaseGate, StringComparison.Ordinal);
        Assert.Contains("Assert-PublishArtifacts", releaseGate, StringComparison.Ordinal);
        Assert.Contains("removed legacy executable alias", releaseGate, StringComparison.Ordinal);
        Assert.Contains("Verify installer artifacts", releaseGate, StringComparison.Ordinal);
        Assert.Contains("Assert-InstallerArtifacts", releaseGate, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $PreviousInstaller -Algorithm SHA256", previousVersionUpgrade, StringComparison.Ordinal);
        Assert.Contains("\"self\", \"--process-id\"", previousVersionUpgrade, StringComparison.Ordinal);
        Assert.Contains("\"--allow-self-stop\", \"--process-id\"", previousVersionUpgrade, StringComparison.Ordinal);
        Assert.Contains("showOverviewHardware=false", previousVersionUpgrade, StringComparison.Ordinal);
        Assert.Contains("Restarted candidate Manager", previousVersionUpgrade, StringComparison.Ordinal);
        Assert.Contains("Remove-TestRootWithRetry", previousVersionUpgrade, StringComparison.Ordinal);
        Assert.Contains("external-preserve.canary", previousVersionUpgrade, StringComparison.Ordinal);
        foreach (var installerTest in new[] { installerSmoke, previousVersionUpgrade })
        {
            Assert.Contains("{5C6D440C-0EE0-4FEC-8D86-6AADEAA24620}_is1", installerTest, StringComparison.Ordinal);
            Assert.Contains("production installer identity is already registered", installerTest, StringComparison.Ordinal);
            Assert.Contains("production Start Menu shortcut", installerTest, StringComparison.Ordinal);
            Assert.Contains("temporary installer registration may require manual cleanup", installerTest, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("Preserving the temporary", installerTest, StringComparison.Ordinal);
        }
        Assert.Contains("WINDOWS_SIGNING_PFX_BASE64", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("git verify-tag", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("new-release-manifest.ps1", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("attest-build-provenance", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RequireSigned", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("workflow_dispatch:", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("tags:", releaseWorkflow, StringComparison.Ordinal);
        Assert.Matches(@"actions/checkout@[0-9a-f]{40}\s+# v7", releaseWorkflow);
        Assert.Matches(@"actions/setup-dotnet@[0-9a-f]{40}\s+# v6", releaseWorkflow);
        Assert.Matches(@"actions/upload-artifact@[0-9a-f]{40}\s+# v7", releaseWorkflow);
        Assert.Contains(".\\scripts\\test-release-gate.ps1", development, StringComparison.Ordinal);
        Assert.Contains("-IncludePublish -IncludeInstaller", development, StringComparison.Ordinal);
    }

    [Fact]
    public void RepositoryKeepsAutomationScriptsOutOfRoot()
    {
        var repositoryRoot = Path.GetDirectoryName(FindRepositoryFile("README.md"))!;
        var rootScripts = Directory.EnumerateFiles(repositoryRoot, "*.ps1", SearchOption.TopDirectoryOnly);
        var automationScripts = Directory.EnumerateFiles(Path.Combine(repositoryRoot, "scripts"), "*.ps1", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Empty(rootScripts);
        Assert.Equal(
            [
                "build-app.ps1",
                "build-installer.ps1",
                "check-code-shape.ps1",
                "clean-repo.ps1",
                "new-release-manifest.ps1",
                "new-sbom.ps1",
                "publish-app.ps1",
                "start-app.ps1",
                "test-app.ps1",
                "test-coverage.ps1",
                "test-docs.ps1",
                "test-environment-integration.ps1",
                "test-installer-smoke.ps1",
                "test-portable-update.ps1",
                "test-previous-version-upgrade.ps1",
                "test-release-gate.ps1",
                "test-vulnerabilities.ps1"
            ],
            automationScripts);
    }

    [Fact]
    public void BuildInstallerRequiresSignedPublishedExecutableForSignedInstaller()
    {
        var buildInstaller = File.ReadAllText(FindRepositoryFile("scripts", "build-installer.ps1"));

        Assert.Contains("[string] $TimestampServer = \"https://timestamp.digicert.com\"", buildInstaller, StringComparison.Ordinal);
        Assert.DoesNotContain("http://timestamp.digicert.com", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("function Assert-SignedIfRequired", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("Assert-SignedIfRequired $PublishedExe $RequireSigned.IsPresent \"Published executable\"", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("$ArtifactLabel is not signed", buildInstaller, StringComparison.Ordinal);
        Assert.True(
            buildInstaller.IndexOf("Assert-SignedIfRequired $PublishedExe", StringComparison.Ordinal)
            < buildInstaller.IndexOf("& $Iscc @isccArgs", StringComparison.Ordinal));
    }

    [Fact]
    public void ReleaseScriptsCanRequireCleanGitTree()
    {
        var releaseGate = File.ReadAllText(FindRepositoryFile("scripts", "test-release-gate.ps1"));
        var publishScript = File.ReadAllText(FindRepositoryFile("scripts", "publish-app.ps1"));
        var buildInstaller = File.ReadAllText(FindRepositoryFile("scripts", "build-installer.ps1"));
        var development = File.ReadAllText(FindRepositoryFile("docs", "DEVELOPMENT.md"));
        var releaseReadiness = File.ReadAllText(FindRepositoryFile("docs", "RELEASE_READINESS.md"));

        foreach (var script in new[] { releaseGate, publishScript, buildInstaller })
        {
            Assert.Contains("[switch] $RequireCleanTree", script, StringComparison.Ordinal);
            Assert.Contains("function Assert-CleanGitTree", script, StringComparison.Ordinal);
            Assert.Contains("Select-Object -First 1", script, StringComparison.Ordinal);
            Assert.Contains("status --porcelain --untracked-files=all", script, StringComparison.Ordinal);
            Assert.Contains("Release requires a clean Git worktree", script, StringComparison.Ordinal);
        }

        Assert.Contains("[string] $TimestampServer = \"https://timestamp.digicert.com\"", releaseGate, StringComparison.Ordinal);
        Assert.DoesNotContain("http://timestamp.digicert.com", releaseGate, StringComparison.Ordinal);
        Assert.Contains("Verify clean Git worktree", releaseGate, StringComparison.Ordinal);
        Assert.Contains("$publishArgs += \"-RequireCleanTree\"", releaseGate, StringComparison.Ordinal);
        Assert.Contains("$installerArgs += \"-RequireCleanTree\"", releaseGate, StringComparison.Ordinal);
        Assert.Contains("$publishArgs.RequireCleanTree = $true", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("-RequireCleanTree", development, StringComparison.Ordinal);
        Assert.Contains("git status --porcelain --untracked-files=all", releaseReadiness, StringComparison.Ordinal);
    }


    [Fact]
    public void PublishScriptUsesSafeDistCleanup()
    {
        var publishScript = File.ReadAllText(FindRepositoryFile("scripts", "publish-app.ps1"));

        Assert.Contains("$publishArgs = @(", publishScript, StringComparison.Ordinal);
        Assert.Contains("& $Dotnet @publishArgs", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("& $Dotnet publish $Project `", publishScript, StringComparison.Ordinal);
        Assert.Contains("function Remove-DistPath", publishScript, StringComparison.Ordinal);
        Assert.Contains("Refusing to remove $Label outside the dist folder", publishScript, StringComparison.Ordinal);
        Assert.Contains("System.IO.FileAttributes]::ReparsePoint", publishScript, StringComparison.Ordinal);
        Assert.Contains("Remove-DistPath -Path $PublishDir -Label \"publish folder\" -Recurse", publishScript, StringComparison.Ordinal);
        Assert.Contains("Remove-DistPath -Path $ZipPath -Label \"portable release archive\"", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $PublishDir -Recurse -Force", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Remove-Item -LiteralPath $ZipPath -Force", publishScript, StringComparison.Ordinal);
    }

    [Fact]
    public void PublishedControlCliUsesDedicatedNativeAotDependencies()
    {
        var publishScript = File.ReadAllText(FindRepositoryFile("scripts", "publish-app.ps1"));
        var normalLock = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.ControlCli", "packages.lock.json"));
        var publishLock = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.ControlCli", "packages.publish.lock.json"));
        var jsonContext = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.ControlCli", "ControlCliJsonContext.cs"));

        Assert.Contains("-p:PublishAot=true", publishScript, StringComparison.Ordinal);
        Assert.Contains("NuGetLockFilePath=packages.publish.lock.json", publishScript, StringComparison.Ordinal);
        Assert.DoesNotContain("Microsoft.DotNet.ILCompiler", normalLock, StringComparison.Ordinal);
        Assert.Contains("Microsoft.DotNet.ILCompiler", publishLock, StringComparison.Ordinal);
        Assert.Contains("JsonSerializable(typeof(DiscoveryDocument))", jsonContext, StringComparison.Ordinal);
        Assert.Contains("JsonSerializable(typeof(JsonElement))", jsonContext, StringComparison.Ordinal);
    }


    [Fact]
    public void InstallerKeepsUserDataUnlessExplicitlyRequested()
    {
        var installer = File.ReadAllText(FindRepositoryFile("installer", "LlamaCppWindowsManager.iss"));
        var buildInstaller = File.ReadAllText(FindRepositoryFile("scripts", "build-installer.ps1"));
        var installerDocs = File.ReadAllText(FindRepositoryFile("docs", "INSTALLER.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var development = File.ReadAllText(FindRepositoryFile("docs", "DEVELOPMENT.md"));
        var releaseReadiness = File.ReadAllText(FindRepositoryFile("docs", "RELEASE_READINESS.md"));

        Assert.Contains("AppId={{5C6D440C-0EE0-4FEC-8D86-6AADEAA24620}", installer, StringComparison.Ordinal);
        Assert.Contains("DefaultDirName={code:GetDefaultDirName}", installer, StringComparison.Ordinal);
        Assert.Contains(@"D:\LlamaCppWindowsManager", installer, StringComparison.Ordinal);
        Assert.Contains(@"DirExists('D:\')", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("IsWritableDirectory", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveStringToFile", installer, StringComparison.Ordinal);
        Assert.Contains("ArchitecturesAllowed=x64compatible", installer, StringComparison.Ordinal);
        Assert.Contains(@"Source: ""{#SourceDir}\{#AppExeName}""", installer, StringComparison.Ordinal);
        Assert.DoesNotContain(@"Source: ""{#SourceDir}\*""", installer, StringComparison.Ordinal);
        Assert.Contains(@"%LocalAppData%\Programs\LlamaCppWindowsManager", installerDocs, StringComparison.Ordinal);
        Assert.Contains("UsePreviousAppDir=yes", installer, StringComparison.Ordinal);
        Assert.Contains("UsePreviousGroup=no", installer, StringComparison.Ordinal);
        Assert.Contains("Start with Windows", installer, StringComparison.Ordinal);
        Assert.Contains(@"Software\Microsoft\Windows\CurrentVersion\Run", installer, StringComparison.Ordinal);
        Assert.Contains("ValueName: \"LlamaCppWindowsManager\"", installer, StringComparison.Ordinal);
        Assert.Contains("uninsdeletevalue", installer, StringComparison.Ordinal);
        Assert.Contains("AppMutex=Local\\llama.cpp-console-single-instance", installer, StringComparison.Ordinal);
        Assert.Contains("postinstall", installer, StringComparison.Ordinal);
        Assert.Contains("InitializeUninstall", installer, StringComparison.Ordinal);
        Assert.Contains("DeleteAppDataOnUninstall := False", installer, StringComparison.Ordinal);
        Assert.Contains("SuppressibleMsgBox", installer, StringComparison.Ordinal);
        Assert.Contains("MB_DEFBUTTON2", installer, StringComparison.Ordinal);
        Assert.Contains("IDNO) = IDYES", installer, StringComparison.Ordinal);
        Assert.Contains("DelTree(ExpandConstant('{app}\\data')", installer, StringComparison.Ordinal);
        Assert.Contains("[InstallDelete]", installer, StringComparison.Ordinal);
        Assert.Contains(@"{app}\LlamaCppConsole.exe", installer, StringComparison.Ordinal);
        Assert.Contains(@"{userprograms}\llama.cpp Console\llama.cpp Console.lnk", installer, StringComparison.Ordinal);
        Assert.Contains(@"{userdesktop}\llama.cpp Console.lnk", installer, StringComparison.Ordinal);
        Assert.DoesNotContain("[UninstallDelete]", installer, StringComparison.Ordinal);
        Assert.DoesNotContain(@"{app}\data\*", installer, StringComparison.Ordinal);

        Assert.Contains("publish-app.ps1", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("ISCC.exe", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_WINDOWS_MANAGER_INNO_SETUP", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("LLAMA_CPP_CONSOLE_INNO_SETUP", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("Programs\\Inno Setup 6\\ISCC.exe", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("Set-AuthenticodeSignature", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("LlamaCppWindowsManager-Setup-$AppVersion-$Runtime.exe", buildInstaller, StringComparison.Ordinal);
        Assert.Contains("docs/DEVELOPMENT.md", readme, StringComparison.Ordinal);
        Assert.Contains("build-installer.ps1", development, StringComparison.Ordinal);
        Assert.Contains("Uninstall keeps `data` by default", installerDocs, StringComparison.Ordinal);
        Assert.Contains("Start with Windows", installerDocs, StringComparison.Ordinal);
        Assert.Contains("Any installer uninstall, repair, or update path that deletes models", releaseReadiness, StringComparison.Ordinal);
    }


}
