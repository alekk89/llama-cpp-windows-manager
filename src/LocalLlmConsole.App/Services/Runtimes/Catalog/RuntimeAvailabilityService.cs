namespace LocalLlmConsole.Services;

public sealed record RuntimeAvailability(bool IsAvailable, string Reason)
{
    public static RuntimeAvailability Available { get; } = new(true, "");
}

public static class RuntimeAvailabilityService
{
    public static RuntimeAvailability Inspect(RuntimeRecord runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        return Inspect(runtime.ExecutablePath);
    }

    public static RuntimeAvailability Inspect(string? executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath))
            return new RuntimeAvailability(false, "No llama-server executable is registered for this runtime.");

        try
        {
            return File.Exists(executablePath)
                ? RuntimeAvailability.Available
                : new RuntimeAvailability(false, $"The registered llama-server executable is missing: {executablePath}");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new RuntimeAvailability(false, $"The registered llama-server path is invalid: {executablePath}");
        }
    }

    public static bool IsAvailable(RuntimeRecord runtime) => Inspect(runtime).IsAvailable;

    public static IReadOnlyList<RuntimeRecord> AvailableRuntimes(IEnumerable<RuntimeRecord> runtimes)
    {
        ArgumentNullException.ThrowIfNull(runtimes);
        return runtimes.Where(IsAvailable).ToArray();
    }

    public static void EnsureAvailable(RuntimeRecord runtime)
    {
        var availability = Inspect(runtime);
        if (!availability.IsAvailable)
            throw new InvalidOperationException($"{availability.Reason} Repair or reinstall the runtime before using it.");
    }
}
