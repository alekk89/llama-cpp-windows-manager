using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Reflection;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
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
    public void ProjectDeclaresVersionTwoTwoMetadata()
    {
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));

        Assert.Contains("<Version>2.1.0</Version>", project, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>2.1.0.0</AssemblyVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>2.1.0.0</FileVersion>", project, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>v2.1.0</InformationalVersion>", project, StringComparison.Ordinal);
    }


    [Fact]
    public void ReleaseDocsAndScriptsUseLaunchBranding()
    {
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
        var buildScript = File.ReadAllText(FindRepositoryFile("build-app.ps1"));
        var publishScript = File.ReadAllText(FindRepositoryFile("publish-app.ps1"));
        var startScript = File.ReadAllText(FindRepositoryFile("start-app.ps1"));
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
        Assert.DoesNotContain(@"D:\LLM", publicDocs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"C:\Users\ageor", publicDocs, StringComparison.OrdinalIgnoreCase);
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
        var releaseGate = File.ReadAllText(FindRepositoryFile("test-release-gate.ps1"));
        var development = File.ReadAllText(FindRepositoryFile("docs", "DEVELOPMENT.md"));

        Assert.Contains("windows-latest", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@v7", workflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@v6", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\build-app.ps1 -Restore", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\test-coverage.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet format LocalLlmConsole.sln --verify-no-changes --verbosity minimal", workflow, StringComparison.Ordinal);
        Assert.Contains("git diff --check", workflow, StringComparison.Ordinal);
        Assert.Contains(".\\test-vulnerabilities.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("checksum was not produced", workflow, StringComparison.Ordinal);
        Assert.Contains("package --vulnerable --include-transitive --format json", File.ReadAllText(FindRepositoryFile("test-vulnerabilities.ps1")), StringComparison.Ordinal);
        Assert.Contains("package --outdated --format json", File.ReadAllText(FindRepositoryFile("test-vulnerabilities.ps1")), StringComparison.Ordinal);
        Assert.Contains("package --deprecated --include-transitive --format json", File.ReadAllText(FindRepositoryFile("test-vulnerabilities.ps1")), StringComparison.Ordinal);
        Assert.Contains(".\\publish-app.ps1", workflow, StringComparison.Ordinal);
        Assert.Contains("\"version\": \"10.0.400\"", globalJson, StringComparison.Ordinal);
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
        Assert.Contains("MinimumServiceLineCoverage = 80.0", File.ReadAllText(FindRepositoryFile("test-coverage.ps1")), StringComparison.Ordinal);
        Assert.Contains("MinimumModelLineCoverage = 95.0", File.ReadAllText(FindRepositoryFile("test-coverage.ps1")), StringComparison.Ordinal);
        Assert.Contains("Skipped or not-executed tests are not allowed", File.ReadAllText(FindRepositoryFile("test-coverage.ps1")), StringComparison.Ordinal);
        Assert.Contains("LocalLlmConsole.App/", File.ReadAllText(FindRepositoryFile("test-coverage.ps1")), StringComparison.Ordinal);
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
        Assert.Contains("WINDOWS_SIGNING_PFX_BASE64", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("-RequireSigned", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions/checkout@v7", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions/setup-dotnet@v6", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains("actions/upload-artifact@v7", releaseWorkflow, StringComparison.Ordinal);
        Assert.Contains(".\\test-release-gate.ps1", development, StringComparison.Ordinal);
        Assert.Contains("-IncludePublish -IncludeInstaller", development, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildInstallerRequiresSignedPublishedExecutableForSignedInstaller()
    {
        var buildInstaller = File.ReadAllText(FindRepositoryFile("build-installer.ps1"));

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
        var releaseGate = File.ReadAllText(FindRepositoryFile("test-release-gate.ps1"));
        var publishScript = File.ReadAllText(FindRepositoryFile("publish-app.ps1"));
        var buildInstaller = File.ReadAllText(FindRepositoryFile("build-installer.ps1"));
        var development = File.ReadAllText(FindRepositoryFile("docs", "DEVELOPMENT.md"));
        var releaseReadiness = File.ReadAllText(FindRepositoryFile("docs", "RELEASE_READINESS.md"));

        foreach (var script in new[] { releaseGate, publishScript, buildInstaller })
        {
            Assert.Contains("[switch] $RequireCleanTree", script, StringComparison.Ordinal);
            Assert.Contains("function Assert-CleanGitTree", script, StringComparison.Ordinal);
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
        var publishScript = File.ReadAllText(FindRepositoryFile("publish-app.ps1"));

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
    public void InstallerKeepsUserDataUnlessExplicitlyRequested()
    {
        var installer = File.ReadAllText(FindRepositoryFile("installer", "LlamaCppWindowsManager.iss"));
        var buildInstaller = File.ReadAllText(FindRepositoryFile("build-installer.ps1"));
        var installerDocs = File.ReadAllText(FindRepositoryFile("docs", "INSTALLER.md"));
        var readme = File.ReadAllText(FindRepositoryFile("README.md"));
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
        Assert.Contains("MB_DEFBUTTON2", installer, StringComparison.Ordinal);
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
        Assert.Contains("build-installer.ps1", readme, StringComparison.Ordinal);
        Assert.Contains("Uninstall keeps `data` by default", installerDocs, StringComparison.Ordinal);
        Assert.Contains("Start with Windows", installerDocs, StringComparison.Ordinal);
        Assert.Contains("Any installer uninstall, repair, or update path that deletes models", releaseReadiness, StringComparison.Ordinal);
    }


    [Fact]
    public void AppUpdateServiceParsesGithubReleaseAndAsset()
    {
        var release = System.Text.Json.Nodes.JsonNode.Parse("""
        {
          "tag_name": "v1.1.2",
          "name": "v1.1.2",
          "body": "Added update checks.",
          "html_url": "https://github.com/alekk89/llama-cpp-windows-manager/releases/tag/v1.1.2",
          "assets": [
            { "name": "notes.txt", "browser_download_url": "https://example.invalid/notes.txt", "size": 10 },
            { "name": "LlamaCppWindowsManager-win-x64.zip", "browser_download_url": "https://example.invalid/app.zip", "size": 1234 },
            { "name": "LlamaCppWindowsManager-win-x64.zip.sha256", "browser_download_url": "https://example.invalid/app.zip.sha256", "size": 64 }
          ]
        }
        """)!.AsObject();

        var update = AppUpdateReleaseParser.ParseLatestRelease(release, "v1.0");

        Assert.True(update.IsAvailable);
        Assert.Equal("v1.0", update.CurrentVersion);
        Assert.Equal("v1.1.2", update.LatestVersion);
        Assert.Equal("LlamaCppWindowsManager-win-x64.zip", update.AssetName);
        Assert.Equal("https://example.invalid/app.zip", update.AssetUrl);
        Assert.Equal("LlamaCppWindowsManager-win-x64.zip.sha256", update.ChecksumAssetName);
        Assert.Equal("https://example.invalid/app.zip.sha256", update.ChecksumAssetUrl);
        Assert.Contains("update checks", update.ReleaseNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppUpdateServiceKeepsStandaloneExecutableAvailableForOlderUpdaters()
    {
        var release = System.Text.Json.Nodes.JsonNode.Parse("""
        {
          "tag_name": "v2.1.0",
          "name": "v2.1.0",
          "assets": [
            { "name": "LlamaCppWindowsManager-win-x64.zip", "browser_download_url": "https://example.invalid/app.zip", "size": 1234 },
            { "name": "LlamaCppWindowsManager-win-x64.zip.sha256", "browser_download_url": "https://example.invalid/app.zip.sha256", "size": 64 },
            { "name": "LlamaCppWindowsManager.exe", "browser_download_url": "https://example.invalid/LlamaCppWindowsManager.exe", "size": 1024 },
            { "name": "LlamaCppWindowsManager.exe.sha256", "browser_download_url": "https://example.invalid/LlamaCppWindowsManager.exe.sha256", "size": 64 }
          ]
        }
        """)!.AsObject();

        var update = AppUpdateReleaseParser.ParseLatestRelease(release, "v2.0.0");

        Assert.True(update.IsAvailable);
        Assert.Equal("LlamaCppWindowsManager.exe", update.AssetName);
        Assert.Equal("LlamaCppWindowsManager.exe.sha256", update.ChecksumAssetName);
    }

    [Fact]
    public void AppUpdateWorkflowServiceBuildsCheckResultMessages()
    {
        var available = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "Release v1.1.2",
            "notes",
            "https://example.invalid/release",
            AppUpdateService.PortableExeName,
            "https://example.invalid/app.exe",
            1024 * 1024,
            ExpectedSha256: new string('a', 64));
        var unavailable = AppUpdateReleaseParser.NoUpdateAvailable("v1.1.2");

        var availableResult = AppUpdateWorkflowService.DescribeCheckResult(available, manual: true);
        var backgroundAvailable = AppUpdateWorkflowService.DescribeCheckResult(available, manual: false);
        var unavailableResult = AppUpdateWorkflowService.DescribeCheckResult(unavailable, manual: true);

        Assert.Equal("Update available: v1.1.2.", availableResult.StatusMessage);
        Assert.True(availableResult.ShouldPromptInstall);
        Assert.False(backgroundAvailable.ShouldPromptInstall);
        Assert.Contains("v1.0 -> v1.1.2", availableResult.DialogMessage, StringComparison.Ordinal);
        Assert.Equal("No app updates available.", unavailableResult.StatusMessage);
        Assert.True(unavailableResult.ShouldShowNoUpdateDialog);
        Assert.Contains("Current version: v1.1.2", unavailableResult.DialogMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AppUpdateApplicationServiceOwnsCheckAndInstallUiFlow()
    {
        var service = new AppUpdateApplicationService();
        var available = new AppUpdateInfo(
            true,
            "v1.0",
            "v9.9.9",
            "Release v9.9.9",
            "notes",
            "https://example.invalid/release",
            AppUpdateService.PortableExeName,
            "https://example.invalid/app.exe",
            1024,
            ExpectedSha256: new string('a', 64));
        var unavailable = AppUpdateReleaseParser.NoUpdateAvailable("v1.1.2");
        var calls = new List<string>();
        var inFlight = false;

        var skipped = await service.CheckForUpdatesAsync(manual: true, CheckActions(
            () => true,
            (_, _) => throw new InvalidOperationException("Already running checks must not call the workflow."),
            confirmResult: true),
            TestContext.Current.CancellationToken);
        var checkedAvailable = await service.CheckForUpdatesAsync(manual: true, CheckActions(
            () => inFlight,
            (manual, _) => Task.FromResult(AppUpdateWorkflowService.DescribeCheckResult(available, manual)),
            confirmResult: true),
            TestContext.Current.CancellationToken);
        var checkedUnavailable = await service.CheckForUpdatesAsync(manual: true, CheckActions(
            () => inFlight,
            (manual, _) => Task.FromResult(AppUpdateWorkflowService.DescribeCheckResult(unavailable, manual)),
            confirmResult: true),
            TestContext.Current.CancellationToken);
        var failed = await service.CheckForUpdatesAsync(manual: true, CheckActions(
            () => inFlight,
            (_, _) => throw new InvalidOperationException("offline"),
            confirmResult: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateCheckApplicationOutcome.Skipped, skipped);
        Assert.Equal(AppUpdateCheckApplicationOutcome.Checked, checkedAvailable);
        Assert.Equal(AppUpdateCheckApplicationOutcome.Checked, checkedUnavailable);
        Assert.Equal(AppUpdateCheckApplicationOutcome.Failed, failed);
        Assert.Contains("inflight:True", calls);
        Assert.Contains("latest:v9.9.9", calls);
        Assert.Contains("nav", calls);
        Assert.Contains("show-updates", calls);
        Assert.Contains("status:Update available: v9.9.9.", calls);
        Assert.Contains("confirm:Install update:Information", calls);
        Assert.Contains("install:v9.9.9:False", calls);
        Assert.Contains("notify:Check for updates:Information", calls);
        Assert.Contains("status:Update check failed: offline", calls);
        Assert.Contains("notify:Update check failed:Warning", calls);
        Assert.False(inFlight);

        AppUpdateCheckApplicationActions CheckActions(
            Func<bool> isCheckInFlight,
            Func<bool, CancellationToken, Task<AppUpdateCheckWorkflowResult>> checkLatestAsync,
            bool confirmResult)
            => new(
                isCheckInFlight,
                value =>
                {
                    inFlight = value;
                    calls.Add($"inflight:{value}");
                },
                checkLatestAsync,
                update => calls.Add($"latest:{update.LatestVersion}"),
                () => calls.Add("nav"),
                () => true,
                () => calls.Add("show-updates"),
                status => calls.Add($"status:{status}"),
                prompt =>
                {
                    calls.Add($"confirm:{prompt.Title}:{prompt.Kind}");
                    return confirmResult;
                },
                prompt => calls.Add($"notify:{prompt.Title}:{prompt.Kind}"),
                (update, confirm) =>
                {
                    calls.Add($"install:{update.LatestVersion}:{confirm}");
                    return Task.CompletedTask;
                });
    }

    [Fact]
    public async Task AppUpdateApplicationServiceOwnsInstallValidationAndClose()
    {
        var service = new AppUpdateApplicationService();
        var unavailable = AppUpdateReleaseParser.NoUpdateAvailable("v1.1.2");
        var missingAsset = new AppUpdateInfo(true, "v1.0", "v2.0", "Release", "", "", "", "", 0);
        var installable = missingAsset with
        {
            AssetName = AppUpdateService.PortableExeName,
            AssetUrl = "https://example.invalid/app.zip"
        };
        var calls = new List<string>();

        var notAvailable = await service.InstallAsync(
            new AppUpdateInstallApplicationRequest(unavailable, Confirm: true, "app.exe", 123),
            InstallActions(confirmResult: true),
            TestContext.Current.CancellationToken);
        var missing = await service.InstallAsync(
            new AppUpdateInstallApplicationRequest(missingAsset, Confirm: true, "app.exe", 123),
            InstallActions(confirmResult: true),
            TestContext.Current.CancellationToken);
        var declined = await service.InstallAsync(
            new AppUpdateInstallApplicationRequest(installable, Confirm: true, "app.exe", 123),
            InstallActions(confirmResult: false),
            TestContext.Current.CancellationToken);
        var started = await service.InstallAsync(
            new AppUpdateInstallApplicationRequest(installable, Confirm: true, "app.exe", 123),
            InstallActions(confirmResult: true),
            TestContext.Current.CancellationToken);

        Assert.Equal(AppUpdateInstallApplicationOutcome.NotAvailable, notAvailable);
        Assert.Equal(AppUpdateInstallApplicationOutcome.MissingAsset, missing);
        Assert.Equal(AppUpdateInstallApplicationOutcome.Declined, declined);
        Assert.Equal(AppUpdateInstallApplicationOutcome.Started, started);
        Assert.Contains("notify:Install update:Warning", calls);
        Assert.Contains("confirm:Install update:Information", calls);
        Assert.Contains("busy:Preparing app update...", calls);
        Assert.Contains("stage:v2.0:app.exe:123", calls);
        Assert.Contains("status:Update staged. Closing to install...", calls);
        Assert.Contains("close", calls);

        AppUpdateInstallApplicationActions InstallActions(bool confirmResult)
            => new(
                prompt =>
                {
                    calls.Add($"confirm:{prompt.Title}:{prompt.Kind}");
                    return confirmResult;
                },
                prompt => calls.Add($"notify:{prompt.Title}:{prompt.Kind}"),
                async (message, action) =>
                {
                    calls.Add($"busy:{message}");
                    await action();
                },
                (update, processPath, processId, _) =>
                {
                    calls.Add($"stage:{update.LatestVersion}:{processPath}:{processId}");
                    return Task.FromResult("Update staged. Closing to install...");
                },
                status => calls.Add($"status:{status}"),
                () => calls.Add("close"));
    }

    [Fact]
    public async Task AppUpdateServiceChecksConfiguredGithubReleaseEndpoint()
    {
        using var handler = new CapturingHttpHandler(_ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
        using var http = new HttpClient(handler);
        var service = CreateAppUpdateService(http);

        var update = await service.CheckLatestAsync(TestContext.Current.CancellationToken);

        Assert.Equal("https://api.github.com/repos/alekk89/llama-cpp-windows-manager/releases/latest", handler.RequestUri?.ToString());
        Assert.False(update.IsAvailable);
        Assert.Contains("No GitHub release feed", update.ReleaseNotes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AppUpdateServiceStartsInstallerThroughInjectedProcessLauncher()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "App", "AppUpdateService.cs"));
        var root = CreateTempRoot();
        var scriptPath = Path.Combine(root, "cache", "app-updates", "v1.1.2", "Install-LlamaCppWindowsManagerUpdate.ps1");
        var sourceExe = Path.Combine(root, "cache", "app-updates", "v1.1.2", AppUpdateService.PortableExeName);
        var targetExe = Path.Combine(root, AppUpdateService.PortableExeName);
        var noticePath = Path.Combine(root, "cache", "app-updates", "installed-update.json");
        var started = new List<ProcessStartInfo>();
        var service = new AppUpdateService(new HttpClient(), started.Add);

        service.StartInstaller(new AppUpdateInstallPlan(scriptPath, sourceExe, targetExe, noticePath), 4321);

        var process = Assert.Single(started);
        Assert.Equal(HostExecutableResolver.WindowsPowerShellExe(), process.FileName);
        Assert.False(process.UseShellExecute);
        Assert.True(process.CreateNoWindow);
        Assert.Equal(ProcessWindowStyle.Hidden, process.WindowStyle);
        Assert.Equal(Path.GetDirectoryName(targetExe), process.WorkingDirectory);
        var args = process.ArgumentList.ToArray();
        Assert.Contains("-ParentPid", args);
        Assert.Contains("4321", args);
        Assert.Contains("-SourceExe", args);
        Assert.Contains(sourceExe, args);
        Assert.Contains("-TargetExe", args);
        Assert.Contains(targetExe, args);
        Assert.Contains("-ObsoleteExe", args);
        Assert.Contains("-SourceCli", args);
        Assert.Contains("-TargetCli", args);
        Assert.Contains("-NoticeTarget", args);
        Assert.Contains(noticePath, args);
        Assert.DoesNotContain("new HttpClient", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PortableUpdaterRollsBackAppWhenCliReplacementFails()
    {
        var root = CreateTempRoot();
        var sourceExe = Path.Combine(root, "staged-app.exe");
        var targetExe = Path.Combine(root, AppUpdateService.PortableExeName);
        var sourceCli = Path.Combine(root, "staged-cli.exe");
        var targetCli = Path.Combine(root, AppUpdateService.ControlCliExeName);
        var scriptPath = Path.Combine(root, "update.ps1");
        await File.WriteAllTextAsync(sourceExe, "new-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(targetExe, "old-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(sourceCli, "new-cli", TestContext.Current.CancellationToken);
        Directory.CreateDirectory(targetCli);
        var updaterScript = typeof(AppUpdateService)
            .GetMethod("UpdaterScript", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)?.ToString() ?? throw new InvalidOperationException("Updater script was unavailable.");
        await File.WriteAllTextAsync(scriptPath, updaterScript, TestContext.Current.CancellationToken);

        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = HostExecutableResolver.WindowsPowerShellExe(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            ArgumentList =
            {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath,
                "-ParentPid", "999999",
                "-SourceExe", sourceExe,
                "-TargetExe", targetExe,
                "-SourceCli", sourceCli,
                "-TargetCli", targetCli,
                "-WorkingDirectory", root
            }
        }) ?? throw new InvalidOperationException("Could not start updater rollback test.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        var diagnostics = (await standardOutput) + Environment.NewLine + (await standardError);

        Assert.NotEqual(0, process.ExitCode);
        Assert.True(
            string.Equals("old-app", await File.ReadAllTextAsync(targetExe, TestContext.Current.CancellationToken), StringComparison.Ordinal),
            diagnostics);
        Assert.True(Directory.Exists(targetCli), diagnostics);
        var temporaryFiles = Directory.EnumerateFiles(root, ".*.new").ToArray();
        var backupFiles = Directory.EnumerateFiles(root, ".*.bak").ToArray();
        Assert.True(
            temporaryFiles.Length == 0,
            $"{diagnostics}{Environment.NewLine}Temporary files: {string.Join(", ", temporaryFiles)}");
        Assert.True(
            backupFiles.Length == 0,
            $"{diagnostics}{Environment.NewLine}Backup files: {string.Join(", ", backupFiles)}");
    }

    [Fact]
    public void AppUpdateServiceExtractsChecksumForSelectedAsset()
    {
        const string hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

        var exact = AppUpdateAssetVerifier.ExtractSha256($"{hash}  LlamaCppWindowsManager-win-x64.zip", "LlamaCppWindowsManager-win-x64.zip");
        var unrelated = AppUpdateAssetVerifier.ExtractSha256($"{hash}  other.zip", "LlamaCppWindowsManager-win-x64.zip");

        Assert.Equal(hash, exact);
        Assert.Equal("", unrelated);
    }

    [Fact]
    public async Task AppUpdateServiceRequiresChecksumBeforeStaging()
    {
        var temp = CreateTempRoot();
        var service = CreateAppUpdateService(new HttpClient());
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager.exe",
            "https://example.invalid/LlamaCppWindowsManager.exe",
            1024 * 1024);

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, "LlamaCppWindowsManager.exe"), TestContext.Current.CancellationToken));

            Assert.Contains("SHA-256 companion", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRejectsMalformedChecksumCompanion()
    {
        var temp = CreateTempRoot();
        using var handler = new CapturingHttpHandler(request =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true
                ? System.Text.Encoding.UTF8.GetBytes("not-a-checksum  LlamaCppWindowsManager.exe")
                : new byte[1024 * 1024]);
            return response;
        });
        using var http = new HttpClient(handler);
        var service = CreateAppUpdateService(http);
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager.exe",
            "https://example.invalid/LlamaCppWindowsManager.exe",
            1024 * 1024,
            "LlamaCppWindowsManager.exe.sha256",
            "https://example.invalid/LlamaCppWindowsManager.exe.sha256");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, "LlamaCppWindowsManager.exe"), TestContext.Current.CancellationToken));

            Assert.Contains("does not contain a checksum", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRejectsInvalidInlineChecksum()
    {
        var temp = CreateTempRoot();
        var service = CreateAppUpdateService(new HttpClient());
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager.exe",
            "https://example.invalid/LlamaCppWindowsManager.exe",
            1024 * 1024,
            ExpectedSha256: "not-a-sha256");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, "LlamaCppWindowsManager.exe"), TestContext.Current.CancellationToken));

            Assert.Contains("invalid SHA-256", ex.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceRejectsUpdateArchiveTraversal()
    {
        var temp = CreateTempRoot();
        var escapeName = $"llama-update-escape-{Guid.NewGuid():N}.txt";
        var archiveBytes = CreateZipBytes(
            (AppUpdateService.PortableExeName, Enumerable.Repeat((byte)7, 1024 * 1024).ToArray()),
            ("../" + escapeName, [1, 2, 3]));
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(archiveBytes)).ToLowerInvariant();
        using var handler = new CapturingHttpHandler(request =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Content = new ByteArrayContent(request.RequestUri?.AbsolutePath.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase) == true
                ? System.Text.Encoding.UTF8.GetBytes($"{hash}  LlamaCppWindowsManager-win-x64.zip")
                : archiveBytes);
            return response;
        });
        using var http = new HttpClient(handler);
        var service = CreateAppUpdateService(http);
        var update = new AppUpdateInfo(
            true,
            "v1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            "LlamaCppWindowsManager-win-x64.zip",
            "https://example.invalid/LlamaCppWindowsManager-win-x64.zip",
            archiveBytes.Length,
            "LlamaCppWindowsManager-win-x64.zip.sha256",
            "https://example.invalid/LlamaCppWindowsManager-win-x64.zip.sha256");

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                service.StageInstallAsync(update, temp, Path.Combine(temp, AppUpdateService.PortableExeName), TestContext.Current.CancellationToken));

            Assert.Contains("unsafe path", ex.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(Path.GetTempPath(), escapeName)));
            Assert.False(File.Exists(Path.Combine(temp, escapeName)));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    [Fact]
    public async Task AppUpdateServiceNormalizesObsoleteExecutableNamesToTheCanonicalName()
    {
        var temp = CreateTempRoot();
        var bytes = Enumerable.Repeat((byte)7, 1024 * 1024).ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
        using var handler = new CapturingHttpHandler(_ =>
        {
            var response = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            response.Content = new ByteArrayContent(bytes);
            return response;
        });
        using var http = new HttpClient(handler);
        var service = CreateAppUpdateService(http);
        var obsoleteExe = Path.Combine(temp, "LlamaCppConsole.exe");
        var update = new AppUpdateInfo(
            true,
            "v1.1.0",
            "v1.1.2",
            "v1.1.2",
            "",
            "https://example.invalid/release",
            AppUpdateService.PortableExeName,
            "https://example.invalid/LlamaCppWindowsManager.exe",
            bytes.Length,
            ExpectedSha256: hash);

        try
        {
            var plan = await service.StageInstallAsync(update, temp, obsoleteExe, TestContext.Current.CancellationToken);

            Assert.Equal(Path.Combine(temp, AppUpdateService.PortableExeName), plan.TargetExe, ignoreCase: true);
            Assert.Equal(obsoleteExe, plan.ObsoleteExe, ignoreCase: true);
            Assert.True(File.Exists(plan.SourceExe));
        }
        finally
        {
            if (Directory.Exists(temp)) Directory.Delete(temp, recursive: true);
        }
    }

    private static AppUpdateService CreateAppUpdateService(HttpClient http)
        => new(http, _ => { });

    private static byte[] CreateZipBytes(params (string EntryName, byte[] Bytes)[] entries)
    {
        using var memory = new MemoryStream();
        using (var zip = new System.IO.Compression.ZipArchive(memory, System.IO.Compression.ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in entries)
            {
                using var stream = zip.CreateEntry(entry.EntryName).Open();
                stream.Write(entry.Bytes);
            }
        }
        return memory.ToArray();
    }

    private sealed class CapturingHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            return Task.FromResult(respond(request));
        }
    }

}
