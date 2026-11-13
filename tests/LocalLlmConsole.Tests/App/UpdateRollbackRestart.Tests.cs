using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class UpdateRollbackRestartTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task FailedCommitAfterManagerExitRestoresRestartsAndRecordsNotice()
    {
        var root = CreateTempRoot();
        var workspace = Path.Combine(root, "workspace");
        var sourceExe = Path.Combine(root, "staged-app.exe");
        var targetExe = Path.Combine(root, AppUpdateService.PortableExeName);
        var sourceCli = Path.Combine(root, "staged-cli.exe");
        var targetCli = Path.Combine(root, AppUpdateService.ControlCliExeName);
        var noticeSource = Path.Combine(root, "installed-update.json");
        // A real windowless executable that exits immediately stands in for the previous Manager.
        File.Copy(Path.Combine(Environment.SystemDirectory, "rundll32.exe"), targetExe);
        var previousHash = SHA256.HashData(await File.ReadAllBytesAsync(targetExe, TestContext.Current.CancellationToken));
        await File.WriteAllTextAsync(sourceExe, "new-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(sourceCli, "new-cli", TestContext.Current.CancellationToken);
        // A directory at the CLI target makes the second commit fail after the app was replaced.
        Directory.CreateDirectory(targetCli);
        await File.WriteAllTextAsync(noticeSource,
            """{"Version":"9.9.9","ReleaseName":"Test release","ReleaseNotes":"notes","InstalledAt":"2026-01-01T00:00:00Z"}""",
            TestContext.Current.CancellationToken);

        var (exitCode, diagnostics) = await RunUpdaterAsync(root,
            "-ParentPid", "999999",
            "-SourceExe", sourceExe, "-TargetExe", targetExe,
            "-SourceCli", sourceCli, "-TargetCli", targetCli,
            "-NoticeSource", noticeSource,
            "-NoticeTarget", Path.Combine(workspace, "cache", "app-updates", "installed-update.json"),
            "-WorkingDirectory", root,
            "-ExpectedSourceSha256", Sha256("new-app"),
            "-ExpectedCliSha256", Sha256("new-cli"));

        Assert.NotEqual(0, exitCode);
        Assert.Equal(previousHash, SHA256.HashData(await File.ReadAllBytesAsync(targetExe, TestContext.Current.CancellationToken)));
        Assert.True(Directory.Exists(targetCli), diagnostics);
        Assert.Contains("Restarted the previous application after the failed update.", diagnostics, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFiles(root, ".*.new"));
        Assert.Empty(Directory.EnumerateFiles(root, ".*.bak"));

        var notice = await AppUpdateService.TryConsumeInstalledNoticeAsync(workspace, TestContext.Current.CancellationToken);
        Assert.NotNull(notice);
        Assert.Equal("9.9.9", notice.Version);
        Assert.False(string.IsNullOrWhiteSpace(notice.FailureMessage), diagnostics);
        var display = AppUpdateWorkflowService.DescribeInstalledNotice(notice);
        Assert.True(display.Failed);
        Assert.Equal("Update not installed", display.Title);
        Assert.Contains("9.9.9", display.Message, StringComparison.Ordinal);
        Assert.Contains(notice.FailureMessage!, display.Message, StringComparison.Ordinal);
        Assert.Null(await AppUpdateService.TryConsumeInstalledNoticeAsync(workspace, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StagedExecutableNotMatchingVerifiedHashIsNeverCommitted()
    {
        var root = CreateTempRoot();
        var sourceExe = Path.Combine(root, "staged-app.exe");
        var targetExe = Path.Combine(root, AppUpdateService.PortableExeName);
        var noticeTarget = Path.Combine(root, "notice.json");
        await File.WriteAllTextAsync(sourceExe, "tampered-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(targetExe, "old-app", TestContext.Current.CancellationToken);

        var (exitCode, diagnostics) = await RunUpdaterAsync(root,
            "-ParentPid", "999999",
            "-SourceExe", sourceExe, "-TargetExe", targetExe,
            "-NoticeTarget", noticeTarget,
            "-WorkingDirectory", root,
            "-ExpectedSourceSha256", Sha256("new-app"));

        Assert.NotEqual(0, exitCode);
        // Windows PowerShell wraps redirected error records at the console width.
        Assert.Contains("SHA-256 does not match", Unwrapped(diagnostics), StringComparison.Ordinal);
        Assert.Equal("old-app", await File.ReadAllTextAsync(targetExe, TestContext.Current.CancellationToken));
        // Verification failed before the Manager was released, so it keeps running and reports the error itself.
        Assert.DoesNotContain("Restarted the previous application", diagnostics, StringComparison.Ordinal);
        Assert.False(File.Exists(noticeTarget));
        Assert.Empty(Directory.EnumerateFiles(root, ".*.new"));
    }

    [Fact]
    public void LegacyInstalledNoticeWithoutFailureFieldStillDeserializes()
    {
        var notice = System.Text.Json.JsonSerializer.Deserialize<InstalledUpdateNotice>(
            """{"Version":"1.2.3","ReleaseName":"r","ReleaseNotes":"n","InstalledAt":"2026-01-01T00:00:00+00:00"}""");

        Assert.NotNull(notice);
        Assert.Null(notice.FailureMessage);
        var display = AppUpdateWorkflowService.DescribeInstalledNotice(notice);
        Assert.False(display.Failed);
        Assert.Equal("Update installed", display.Title);
        Assert.StartsWith("Updated to 1.2.3.", display.Message, StringComparison.Ordinal);
    }

    private static string Unwrapped(string text) => text.ReplaceLineEndings("");

    private static string Sha256(string text)
        => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));

    private static async Task<(int ExitCode, string Diagnostics)> RunUpdaterAsync(string root, params string[] arguments)
    {
        var scriptPath = Path.Combine(root, "update.ps1");
        var updaterScript = (string)typeof(AppUpdateService)
            .GetMethod("UpdaterScript", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, null)!;
        await File.WriteAllTextAsync(scriptPath, updaterScript, TestContext.Current.CancellationToken);
        var info = new ProcessStartInfo
        {
            FileName = HostExecutableResolver.WindowsPowerShellExe(),
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath }.Concat(arguments))
            info.ArgumentList.Add(argument);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the update helper.");
        var standardOutput = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var standardError = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken);
        return (process.ExitCode, (await standardOutput) + Environment.NewLine + (await standardError));
    }
}
