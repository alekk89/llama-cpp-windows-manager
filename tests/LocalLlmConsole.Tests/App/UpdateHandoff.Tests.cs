using System.Diagnostics;
using System.Reflection;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class UpdateHandoffTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActualLauncherRequiresVerifiedStageBeforeAuthorizingReplacement(bool missingSource)
    {
        var root = CreateTempRoot();
        var script = Path.Combine(root, "update.ps1");
        await File.WriteAllTextAsync(script, (string)typeof(AppUpdateService).GetMethod("UpdaterScript", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null)!, TestContext.Current.CancellationToken);
        var source = Path.Combine(root, "source.exe");
        var target = Path.Combine(root, "target.exe");
        var sourceCli = Path.Combine(root, "source-cli.exe");
        var targetCli = Path.Combine(root, "llwmctl.exe");
        if (!missingSource) await File.WriteAllTextAsync(source, "new-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(target, "old-app", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(sourceCli, "new-cli", TestContext.Current.CancellationToken);
        await File.WriteAllTextAsync(targetCli, "old-cli", TestContext.Current.CancellationToken);
        Process? helper = null;
        using var http = new HttpClient();
        using var service = new AppUpdateService(http, info =>
        {
            info.ArgumentList.Add("-SkipRestart");
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            helper = Process.Start(info) ?? throw new InvalidOperationException("No updater process");
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            var plan = new AppUpdateInstallPlan(script, source, target, Path.Combine(root, "notice.json"), SourceCli: sourceCli, TargetCli: targetCli);
            if (missingSource)
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartInstallerAsync(plan, 999999, timeout.Token));
            else
                await service.StartInstallerAsync(plan, 999999, timeout.Token);
            Assert.NotNull(helper);
            var output = helper.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = helper.StandardError.ReadToEndAsync(timeout.Token);
            await helper.WaitForExitAsync(timeout.Token);
            var diagnostics = (await output) + (await error);
            Assert.Equal(missingSource ? "old-app" : "new-app", await File.ReadAllTextAsync(target, timeout.Token));
            Assert.Equal(missingSource ? "old-cli" : "new-cli", await File.ReadAllTextAsync(targetCli, timeout.Token));
            Assert.True(missingSource ? helper.ExitCode != 0 : helper.ExitCode == 0, diagnostics);
            Assert.Empty(Directory.EnumerateFiles(root, ".*.new"));
            Assert.Empty(Directory.EnumerateFiles(root, ".*.bak"));
        }
        finally
        {
            if (helper is { HasExited: false }) { helper.Kill(entireProcessTree: true); await helper.WaitForExitAsync(TestContext.Current.CancellationToken); }
            helper?.Dispose();
        }
    }

    [Fact]
    public async Task MissingAcknowledgementCanBeCancelledWithoutAuthorizingTheHelper()
    {
        var root = CreateTempRoot();
        string? handoff = null;
        using var http = new HttpClient();
        using var cancellation = new CancellationTokenSource();
        using var service = new AppUpdateService(http, info =>
        {
            var args = info.ArgumentList.ToArray();
            handoff = args[Array.IndexOf(args, "-HandoffName") + 1];
            cancellation.Cancel();
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.StartInstallerAsync(
            new AppUpdateInstallPlan("script", "source", Path.Combine(root, "app.exe"), "notice"), 999999, cancellation.Token));
        Assert.NotNull(handoff);
        Assert.Throws<WaitHandleCannotBeOpenedException>(() => EventWaitHandle.OpenExisting(handoff + ".proceed"));
    }
}
