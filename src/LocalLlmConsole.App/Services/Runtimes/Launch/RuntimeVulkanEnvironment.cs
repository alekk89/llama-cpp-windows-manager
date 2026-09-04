namespace LocalLlmConsole.Services;

internal static class RuntimeVulkanEnvironment
{
    internal const string Variable = "GGML_VK_SUBALLOCATION_BLOCK_SIZE";

    internal static string? Value(RuntimeBackend backend, int blockSizeMiB)
    {
        if (blockSizeMiB < 0)
            throw new InvalidOperationException("Vulkan allocation block size must be zero (runtime default) or a positive number of MiB.");
        return backend == RuntimeBackend.Vulkan && blockSizeMiB > 0
            ? ((long)blockSizeMiB * 1024 * 1024).ToString(CultureInfo.InvariantCulture)
            : null;
    }

    internal static void ApplyNative(ProcessStartInfo start, RuntimeBackend backend, int blockSizeMiB)
    {
        if (Value(backend, blockSizeMiB) is { } value) start.Environment[Variable] = value;
    }

    internal static string WslPrefix(RuntimeBackend backend, int blockSizeMiB)
        => Value(backend, blockSizeMiB) is { } value ? $"export {Variable}={value}; " : "";
}
