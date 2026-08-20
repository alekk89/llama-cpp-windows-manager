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
          "tag_name": "v2.2.0",
          "name": "v2.2.0",
          "assets": [
            { "name": "LlamaCppWindowsManager-win-x64.zip", "browser_download_url": "https://example.invalid/app.zip", "size": 1234 },
            { "name": "LlamaCppWindowsManager-win-x64.zip.sha256", "browser_download_url": "https://example.invalid/app.zip.sha256", "size": 64 },
            { "name": "LlamaCppWindowsManager.exe", "browser_download_url": "https://example.invalid/LlamaCppWindowsManager.exe", "size": 1024 },
            { "name": "LlamaCppWindowsManager.exe.sha256", "browser_download_url": "https://example.invalid/LlamaCppWindowsManager.exe.sha256", "size": 64 }
          ]
        }
        """)!.AsObject();

        var update = AppUpdateReleaseParser.ParseLatestRelease(release, "v2.1.0");

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

}
