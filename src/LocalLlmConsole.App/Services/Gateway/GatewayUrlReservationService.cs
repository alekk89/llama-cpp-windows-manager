using System.Diagnostics;

namespace LocalLlmConsole.Services;

/// <summary>
/// Handles Windows http.sys URL ACL reservations so the auto-load gateway
/// can bind without Administrator privileges.
/// </summary>
public static class GatewayUrlReservationService
{
    private const string NetshPath = "netsh";

    /// <summary>
    /// Returns the URL prefix that the gateway listener uses for a given port
    /// when LAN access is enabled. This matches the format http.sys expects.
    /// </summary>
    public static string ListenerPrefixForPort(int port, bool allowLan)
        => allowLan
            ? $"http://+:{port}/"
            : $"http://127.0.0.1:{port}/";

    public static async Task<string> PreferredListenerPrefixAsync(
        int port,
        bool allowLan,
        CancellationToken cancellationToken = default)
    {
        if (allowLan) return ListenerPrefixForPort(port, allowLan: true);

        var wildcard = ListenerPrefixForPort(port, allowLan: true);
        return await ReservationExistsForPrefixAsync(wildcard, cancellationToken)
            ? wildcard
            : ListenerPrefixForPort(port, allowLan: false);
    }

    /// <summary>
    /// Checks whether a URL ACL reservation already exists for the prefix.
    /// </summary>
    public static async Task<bool> ReservationExistsAsync(int port, bool allowLan, CancellationToken cancellationToken = default)
    {
        var prefix = ListenerPrefixForPort(port, allowLan);
        return await ReservationExistsForPrefixAsync(prefix, cancellationToken);
    }

    private static async Task<bool> ReservationExistsForPrefixAsync(string prefix, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = NetshPath,
                Arguments = $"http show urlacl url={prefix}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process is null) return false;

            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0 && output.Contains(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Attempts to register the URL ACL reservation. If the current process
    /// is not elevated, prompts via UAC. Returns true if the reservation was
    /// added successfully.
    /// </summary>
    public static async Task<bool> TryRegisterAsync(int port, bool allowLan, CancellationToken cancellationToken = default)
    {
        var prefix = ListenerPrefixForPort(port, allowLan);

        // If the reservation already exists, nothing to do.
        if (await ReservationExistsForPrefixAsync(prefix, cancellationToken))
            return true;

        return await RunNetshAddAsync(prefix, cancellationToken);
    }

    private static async Task<bool> RunNetshAddAsync(string prefix, CancellationToken cancellationToken)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = NetshPath,
                Arguments = $"http add urlacl url={prefix} user=\"{Environment.UserDomainName}\\{Environment.UserName}\"",
                UseShellExecute = true,
                Verb = "runas", // triggers UAC elevation
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(startInfo);
            if (process is null) return false;

            await process.WaitForExitAsync(cancellationToken);

            // Exit code 0 = success, 183 = already exists (ERROR_ALREADY_EXISTS)
            return process.ExitCode == 0 || process.ExitCode == 183;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            // ERROR_CANCELLED — user declined the UAC prompt
            return false;
        }
        catch
        {
            return false;
        }
    }
}
