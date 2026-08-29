namespace LocalLlmConsole.Services;

public sealed class RuntimePortAllocator
{
    public async Task<int> AllocateAsync(
        int preferredPort,
        IEnumerable<int> reservedPorts,
        Func<int, Task<bool>> isPortUnavailable,
        int maxAttempts = 100)
    {
        var reserved = reservedPorts.ToHashSet();
        var start = IsValidPort(preferredPort) ? preferredPort : 8081;
        for (var offset = 0; offset < maxAttempts; offset++)
        {
            var port = start + offset;
            if (!IsValidPort(port)) break;
            if (reserved.Contains(port)) continue;
            if (await isPortUnavailable(port)) continue;
            return port;
        }

        throw new InvalidOperationException($"No free model server port was found near {start}.");
    }

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;
}
