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
        Task<string>? output = null;
        Task<string>? error = null;
        using var http = new HttpClient();
        using var service = new AppUpdateService(http, info =>
        {
            info.ArgumentList.Add("-SkipRestart");
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;
            // PowerShell runs script assertions, not instrumentable application C#.
            // Keep its CLR startup independent of the test host's coverage profiler.
            info.Environment["COR_ENABLE_PROFILING"] = "0";
            info.Environment["CORECLR_ENABLE_PROFILING"] = "0";
            helper = Process.Start(info) ?? throw new InvalidOperationException("No updater process");
            output = helper.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            error = helper.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        // Allow the production 30-second acknowledgement deadline plus replacement
        // and process cleanup; coverage on a shared runner can delay PowerShell startup.
        timeout.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            var plan = new AppUpdateInstallPlan(script, source, target, Path.Combine(root, "notice.json"), SourceCli: sourceCli, TargetCli: targetCli);
            if (missingSource)
                await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartInstallerAsync(plan, 999999, timeout.Token));
            else
                await service.StartInstallerAsync(plan, 999999, timeout.Token);
            Assert.NotNull(helper);
            await helper.WaitForExitAsync(timeout.Token);
            var diagnostics = (await output!) + (await error!);
            Assert.Equal(missingSource ? "old-app" : "new-app", await File.ReadAllTextAsync(target, timeout.Token));
            Assert.Equal(missingSource ? "old-cli" : "new-cli", await File.ReadAllTextAsync(targetCli, timeout.Token));
            Assert.True(missingSource ? helper.ExitCode != 0 : helper.ExitCode == 0, diagnostics);
            Assert.Empty(Directory.EnumerateFiles(root, ".*.new"));
            Assert.Empty(Directory.EnumerateFiles(root, ".*.bak"));
        }
        catch (Exception ex)
        {
            if (helper is { HasExited: false }) { helper.Kill(entireProcessTree: true); await helper.WaitForExitAsync(TestContext.Current.CancellationToken); }
            var diagnostics = (output is null ? "" : await output) + (error is null ? "" : await error);
            throw new InvalidOperationException($"Updater handoff failed. Helper output: {diagnostics}", ex);
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
