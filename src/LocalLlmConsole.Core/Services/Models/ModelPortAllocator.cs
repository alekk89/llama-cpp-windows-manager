namespace LocalLlmConsole.Services;

public static class ModelPortAllocator
{
    public static int NextAvailable(int preferredPort, IEnumerable<int> usedPorts, int maxAttempts = 1000)
    {
        var used = usedPorts
            .Where(port => port is >= 1 and <= 65535)
            .ToHashSet();
        var start = preferredPort is >= 1 and <= 65535 ? preferredPort : 8081;
        for (var offset = 0; offset < maxAttempts; offset++)
        {
            var port = start + offset;
            if (port > 65535) break;
            if (!used.Contains(port)) return port;
        }

        throw new InvalidOperationException($"No free model port was found near {start}.");
    }
}
