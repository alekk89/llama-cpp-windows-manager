using System.Diagnostics;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class PackageAuditPolicyTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task AuditIgnoresOnlyTheNextDotNetMajorOfRuntimeAlignedPackages()
    {
        var root = CreateTempRoot();
        var fixture = Path.Combine(root, "outdated.json");
        File.WriteAllText(fixture, """
            {
              "projects": [{
                "path": "App.csproj",
                "frameworks": [{
                  "framework": "net10.0-windows7.0",
                  "topLevelPackages": [
                    { "id": "Microsoft.Data.Sqlite", "resolvedVersion": "10.0.13", "latestVersion": "11.0.0" },
                    { "id": "System.Security.Cryptography.ProtectedData", "resolvedVersion": "10.0.13", "latestVersion": "11.0.0" },
                    { "id": "Microsoft.Data.Sqlite.Patch", "resolvedVersion": "10.0.13", "latestVersion": "10.0.14" },
                    { "id": "System.Text.Json", "resolvedVersion": "9.0.4", "latestVersion": "11.0.0" },
                    { "id": "xunit.v3.mtp-v2", "resolvedVersion": "4.0.2", "latestVersion": "5.0.0" },
                    { "id": "xunit.v3.assert", "resolvedVersion": "4.0.2", "latestVersion": "4.0.3" },
                    { "id": "System.Memory", "resolvedVersion": "10.0.13" }
                  ]
                }]
              }]
            }
            """);

        var result = await EvaluateAsync(fixture);

        Assert.True(result.ExitCode == 0, result.Output);
        var reported = result.Output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(line => line.StartsWith("OUTDATED ", StringComparison.Ordinal))
            .Select(line => line["OUTDATED ".Length..])
            .ToArray();
        Assert.Equal(
            ["Microsoft.Data.Sqlite.Patch", "System.Text.Json", "xunit.v3.mtp-v2", "xunit.v3.assert"],
            reported);
        Assert.Contains("Ignoring Microsoft.Data.Sqlite 11.0.0", result.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditAndDependabotShareTheRuntimeAlignedMajorPolicy()
    {
        var audit = File.ReadAllText(FindRepositoryFile("scripts", "test-vulnerabilities.ps1"));
        var dependabot = File.ReadAllText(FindRepositoryFile(".github", "dependabot.yml"));

        Assert.Contains("package-audit-policy.ps1", audit, StringComparison.Ordinal);
        Assert.Contains("Get-OutdatedPackages", audit, StringComparison.Ordinal);
        foreach (var pattern in new[] { "System.*", "Microsoft.Data.Sqlite*", "Microsoft.Extensions.*", "Microsoft.Win32.*" })
            Assert.Contains($"dependency-name: \"{pattern}\"", dependabot, StringComparison.Ordinal);
        Assert.Contains("version-update:semver-major", dependabot, StringComparison.Ordinal);
    }

    private static async Task<(int ExitCode, string Output)> EvaluateAsync(string fixture)
    {
        var policy = FindRepositoryFile("scripts", "package-audit-policy.ps1").Replace("'", "''", StringComparison.Ordinal);
        var json = fixture.Replace("'", "''", StringComparison.Ordinal);
        var command = $". '{policy}'; foreach ($p in (Get-OutdatedPackages (Get-Content -Raw -LiteralPath '{json}' | ConvertFrom-Json))) {{ Write-Output \"OUTDATED $($p.id)\" }}";
        var start = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
