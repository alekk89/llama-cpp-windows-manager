using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class GatewayFirewallRuleTests
{
    [Theory]
    [InlineData("installed", GatewayFirewallRuleStatus.Installed)]
    [InlineData("different_port", GatewayFirewallRuleStatus.DifferentPort)]
    [InlineData("missing", GatewayFirewallRuleStatus.Missing)]
    public async Task InspectionMapsManagerRuleState(string output, GatewayFirewallRuleStatus expected)
    {
        var runner = new CapturingRunner(new ProcessRunResult(0, $"LLWM_FIREWALL={output}\r\n", ""));
        var service = new GatewayFirewallRuleService(runner, (_, _) => Task.FromResult(0));

        var state = await service.InspectAsync(8080, TestContext.Current.CancellationToken);

        Assert.Equal(expected, state.Status);
        var script = Decode(runner.LastStartInfo!);
        Assert.Contains(GatewayFirewallRuleService.DisplayName, script, StringComparison.Ordinal);
        Assert.Contains("LocalSubnet", script, StringComparison.Ordinal);
        Assert.Contains("Private|Domain", script, StringComparison.Ordinal);
        Assert.Contains("8080", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallUsesAnExplicitNarrowRuleAndVerifiesIt()
    {
        ProcessStartInfo? elevated = null;
        var runner = new CapturingRunner(new ProcessRunResult(0, "LLWM_FIREWALL=installed", ""));
        var service = new GatewayFirewallRuleService(runner, (startInfo, _) =>
        {
            elevated = startInfo;
            return Task.FromResult(0);
        });

        var result = await service.InstallAsync(18080, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        Assert.Equal(GatewayFirewallRuleStatus.Installed, result.State.Status);
        Assert.NotNull(elevated);
        Assert.True(elevated.UseShellExecute);
        Assert.Equal("runas", elevated.Verb);
        var script = Decode(elevated);
        Assert.Contains("New-NetFirewallRule", script, StringComparison.Ordinal);
        Assert.Contains("-Direction Inbound", script, StringComparison.Ordinal);
        Assert.Contains("-Profile Domain,Private", script, StringComparison.Ordinal);
        Assert.Contains("-Protocol TCP", script, StringComparison.Ordinal);
        Assert.Contains("-LocalPort 18080", script, StringComparison.Ordinal);
        Assert.Contains("-RemoteAddress LocalSubnet", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RemoveTargetsOnlyTheNamedManagerRule()
    {
        ProcessStartInfo? elevated = null;
        var runner = new CapturingRunner(new ProcessRunResult(0, "LLWM_FIREWALL=missing", ""));
        var service = new GatewayFirewallRuleService(runner, (startInfo, _) =>
        {
            elevated = startInfo;
            return Task.FromResult(0);
        });

        var result = await service.RemoveAsync(8080, TestContext.Current.CancellationToken);

        Assert.True(result.Success);
        var script = Decode(elevated!);
        Assert.Contains($"-DisplayName '{GatewayFirewallRuleService.DisplayName}'", script, StringComparison.Ordinal);
        Assert.Contains("Remove-NetFirewallRule", script, StringComparison.Ordinal);
        Assert.DoesNotContain("New-NetFirewallRule", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancelledElevationIsReportedWithoutChangingState()
    {
        var runner = new CapturingRunner(new ProcessRunResult(0, "LLWM_FIREWALL=missing", ""));
        var service = new GatewayFirewallRuleService(
            runner,
            (_, _) => throw new Win32Exception(1223));

        var result = await service.InstallAsync(8080, TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.True(result.Cancelled);
        Assert.Equal(GatewayFirewallRuleStatus.Missing, result.State.Status);
    }

    private static string Decode(ProcessStartInfo startInfo)
    {
        var encoded = startInfo.Arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)[^1];
        return Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
    }

    private sealed class CapturingRunner(ProcessRunResult result) : IProcessRunner
    {
        public ProcessStartInfo? LastStartInfo { get; private set; }

        public Task<ProcessRunResult> RunAsync(
            ProcessStartInfo startInfo,
            TimeSpan timeout,
            CancellationToken cancellationToken = default,
            string? standardInput = null)
        {
            LastStartInfo = startInfo;
            return Task.FromResult(result);
        }
    }
}
