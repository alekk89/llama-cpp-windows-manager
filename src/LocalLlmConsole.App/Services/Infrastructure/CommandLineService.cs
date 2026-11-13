
namespace LocalLlmConsole.Services;

public static class CommandLineService
{
    public static string PowerShellWslBashScriptCommand(string distro, string bashScript)
        => PowerShellWslBashScriptCommand(HostExecutableResolver.WslExe(), distro, bashScript);

    public static string PowerShellWslBashScriptCommand(string wslExe, string distro, string bashScript)
    {
        var encodedScript = Convert.ToBase64String(Encoding.UTF8.GetBytes(bashScript));
        return string.Join("; ", new[]
        {
            $"$bash=[System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String('{encodedScript}'))",
            $"$bash | & {PowerShellQuote(wslExe)} -d {PowerShellQuote(distro)} -- bash -s"
        });
    }

    public static ProcessStartInfo VisiblePowerShellStartInfo(string command, bool elevated)
    {
        var script = $"{command}; Write-Host ''; Write-Host 'Command finished. You can close this window.'";
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe())
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Normal,
            Arguments = $"-NoExit -NoProfile -EncodedCommand {encoded}"
        };
        if (elevated) psi.Verb = "runas";
        return psi;
    }

    public static string PowerShellQuote(string value)
        => "'" + RejectNullBytes(value ?? "", nameof(value)).Replace("'", "''") + "'";

    public static string BashQuote(string value)
        => "'" + RejectNullBytes(value ?? "", nameof(value)).Replace("'", "'\"'\"'") + "'";

    private static string RejectNullBytes(string value, string parameterName)
    {
        if (value.IndexOf('\0') >= 0)
            throw new ArgumentException("Shell arguments cannot contain null bytes.", parameterName);
        return value;
    }

    public static string FirstNonBlankLine(string value)
        => (value ?? "")
            .Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line)) ?? "";

    public static string WslKillByEnvironmentMarkerCommand(string marker)
    {
        var modernMarkerValue = $"LLAMA_CPP_WINDOWS_MANAGER_BUILD_MARKER={marker}";
        var legacyConsoleMarkerValue = $"LLAMA_CPP_CONSOLE_BUILD_MARKER={marker}";
        var legacyMarkerValue = $"LOCAL_LLM_CONSOLE_BUILD_MARKER={marker}";
        return WslProcessKillScript.Build(
            [
                $"modern_marker={BashQuote(modernMarkerValue)};",
                $"legacy_console_marker={BashQuote(legacyConsoleMarkerValue)};",
                $"legacy_marker={BashQuote(legacyMarkerValue)};"
            ],
            WslProcessKillScript.ScanSource.Environment,
            "*\"$modern_marker\"*|*\"$legacy_console_marker\"*|*\"$legacy_marker\"*",
            verify: false);
    }
}
