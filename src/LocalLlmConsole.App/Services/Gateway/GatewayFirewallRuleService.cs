using System.ComponentModel;

namespace LocalLlmConsole.Services;

public enum GatewayFirewallRuleStatus
{
    Missing,
    Installed,
    DifferentPort,
    Unavailable
}

public sealed record GatewayFirewallRuleState(
    GatewayFirewallRuleStatus Status,
    int Port,
    string Detail = "")
{
    public string StatusCode => Status switch
    {
        GatewayFirewallRuleStatus.Installed => "installed",
        GatewayFirewallRuleStatus.DifferentPort => "different_port",
        GatewayFirewallRuleStatus.Unavailable => "unavailable",
        _ => "missing"
    };
}

public sealed record GatewayFirewallRuleOperationResult(
    bool Success,
    bool Cancelled,
    GatewayFirewallRuleState State,
    string Error = "");

public sealed class GatewayFirewallRuleService
{
    internal const string DisplayName = "llama.cpp Windows Manager Gateway";
    private const string ResultPrefix = "LLWM_FIREWALL=";
    private readonly IProcessRunner _processRunner;
    private readonly Func<ProcessStartInfo, CancellationToken, Task<int>> _runElevated;

    public GatewayFirewallRuleService(IProcessRunner processRunner)
        : this(processRunner, RunElevatedAsync)
    {
    }

    internal GatewayFirewallRuleService(
        IProcessRunner processRunner,
        Func<ProcessStartInfo, CancellationToken, Task<int>> runElevated)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _runElevated = runElevated ?? throw new ArgumentNullException(nameof(runElevated));
    }

    public async Task<GatewayFirewallRuleState> InspectAsync(
        int port,
        CancellationToken cancellationToken = default)
    {
        ValidatePort(port);
        try
        {
            var result = await _processRunner.RunAsync(
                PowerShell(QueryScript(port), elevated: false),
                TimeSpan.FromSeconds(20),
                cancellationToken);
            if (result.ExitCode != 0)
                return new GatewayFirewallRuleState(
                    GatewayFirewallRuleStatus.Unavailable,
                    port,
                    FirstNonBlank(result.Error, result.Output, $"PowerShell exited with code {result.ExitCode}."));

            var status = result.Output
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .LastOrDefault(line => line.StartsWith(ResultPrefix, StringComparison.Ordinal));
            return status switch
            {
                ResultPrefix + "installed" => new GatewayFirewallRuleState(GatewayFirewallRuleStatus.Installed, port),
                ResultPrefix + "different_port" => new GatewayFirewallRuleState(GatewayFirewallRuleStatus.DifferentPort, port),
                ResultPrefix + "missing" => new GatewayFirewallRuleState(GatewayFirewallRuleStatus.Missing, port),
                _ => new GatewayFirewallRuleState(
                    GatewayFirewallRuleStatus.Unavailable,
                    port,
                    "Windows Firewall did not return a recognizable rule state.")
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new GatewayFirewallRuleState(GatewayFirewallRuleStatus.Unavailable, port, ex.Message);
        }
    }

    public Task<GatewayFirewallRuleOperationResult> InstallAsync(
        int port,
        CancellationToken cancellationToken = default)
        => ChangeAsync(port, InstallScript(port), cancellationToken);

    public Task<GatewayFirewallRuleOperationResult> RemoveAsync(
        int port,
        CancellationToken cancellationToken = default)
        => ChangeAsync(port, RemoveScript(), cancellationToken);

    private async Task<GatewayFirewallRuleOperationResult> ChangeAsync(
        int port,
        string script,
        CancellationToken cancellationToken)
    {
        ValidatePort(port);
        try
        {
            var exitCode = await _runElevated(PowerShell(script, elevated: true), cancellationToken);
            var state = await InspectAsync(port, cancellationToken);
            var expected = script.Contains("New-NetFirewallRule", StringComparison.Ordinal)
                ? GatewayFirewallRuleStatus.Installed
                : GatewayFirewallRuleStatus.Missing;
            var success = exitCode == 0 && state.Status == expected;
            return new GatewayFirewallRuleOperationResult(
                success,
                Cancelled: false,
                state,
                success ? "" : $"Windows Firewall operation exited with code {exitCode}.");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return new GatewayFirewallRuleOperationResult(
                Success: false,
                Cancelled: true,
                await InspectAsync(port, cancellationToken));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new GatewayFirewallRuleOperationResult(
                Success: false,
                Cancelled: false,
                await InspectAsync(port, cancellationToken),
                ex.Message);
        }
    }

    private static ProcessStartInfo PowerShell(string script, bool elevated)
        => new()
        {
            FileName = "powershell.exe",
            Arguments = $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -EncodedCommand {Encoded(script)}",
            UseShellExecute = elevated,
            Verb = elevated ? "runas" : "",
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };

    private static string QueryScript(int port)
        => $$"""
            $ErrorActionPreference = 'Stop'
            try {
                $rules = @(Get-NetFirewallRule -DisplayName '{{DisplayName}}' -ErrorAction SilentlyContinue)
                if ($rules.Count -eq 0) {
                    Write-Output '{{ResultPrefix}}missing'
                    exit 0
                }
                foreach ($rule in $rules) {
                    $ports = @($rule | Get-NetFirewallPortFilter)
                    $addresses = @($rule | Get-NetFirewallAddressFilter)
                    $profile = [string]$rule.Profile
                    $portMatch = @($ports.LocalPort) -contains '{{port}}'
                    $tcp = @($ports.Protocol) -contains 'TCP'
                    $localSubnet = @($addresses.RemoteAddress) -contains 'LocalSubnet'
                    $safeProfile = $profile -notmatch 'Public' -and $profile -match 'Private|Domain'
                    if ($rule.Enabled -eq 'True' -and $rule.Direction -eq 'Inbound' -and $rule.Action -eq 'Allow' -and $portMatch -and $tcp -and $localSubnet -and $safeProfile) {
                        Write-Output '{{ResultPrefix}}installed'
                        exit 0
                    }
                }
                Write-Output '{{ResultPrefix}}different_port'
            }
            catch {
                Write-Error $_
                exit 1
            }
            """;

    private static string InstallScript(int port)
        => $$"""
            $ErrorActionPreference = 'Stop'
            $existing = @(Get-NetFirewallRule -DisplayName '{{DisplayName}}' -ErrorAction SilentlyContinue)
            if ($existing.Count -gt 0) {
                $existing | Remove-NetFirewallRule -ErrorAction Stop
            }
            New-NetFirewallRule -DisplayName '{{DisplayName}}' -Direction Inbound -Action Allow -Enabled True -Profile Domain,Private -Protocol TCP -LocalPort {{port}} -RemoteAddress LocalSubnet -ErrorAction Stop | Out-Null
            """;

    private static string RemoveScript()
        => $$"""
            $ErrorActionPreference = 'Stop'
            Get-NetFirewallRule -DisplayName '{{DisplayName}}' -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction Stop
            """;

    private static string Encoded(string script)
        => Convert.ToBase64String(Encoding.Unicode.GetBytes(script));

    private static async Task<int> RunElevatedAsync(
        ProcessStartInfo startInfo,
        CancellationToken cancellationToken)
    {
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start the Windows Firewall permission request.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    private static string FirstNonBlank(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Unknown error.";

    private static void ValidatePort(int port)
    {
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "Gateway port must be between 1 and 65535.");
    }
}
