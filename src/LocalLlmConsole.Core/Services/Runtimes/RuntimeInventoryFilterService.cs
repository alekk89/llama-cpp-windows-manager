namespace LocalLlmConsole.Services;

public static class RuntimeInventoryFilterService
{
    public const string All = "All";
    public const string Amd = "AMD";
    public const string Intel = "Intel";
    public const string Nvidia = "NVIDIA";
    public const string Windows = "Windows";
    public const string Linux = "Linux";

    public static IReadOnlyList<string> VendorOptions { get; } = [All, Amd, Intel, Nvidia];

    public static IReadOnlyList<string> PlatformOptions { get; } = [All, Windows, Linux];

    public static string Vendor(RuntimeBackend backend) => backend switch
    {
        RuntimeBackend.Cuda => Nvidia,
        RuntimeBackend.Sycl => Intel,
        RuntimeBackend.Vulkan => Amd,
        RuntimeBackend.Rocm => Amd,
        _ => "CPU"
    };

    public static string Platform(RuntimeMode mode)
        => mode == RuntimeMode.Native ? Windows : Linux;

    public static bool Matches(string vendor, string platform, string vendorFilter, string platformFilter)
        => (IsAll(vendorFilter) || IsAll(vendor) || string.Equals(vendor, vendorFilter, StringComparison.OrdinalIgnoreCase))
            && (IsAll(platformFilter) || IsAll(platform) || string.Equals(platform, platformFilter, StringComparison.OrdinalIgnoreCase));

    private static bool IsAll(string? value)
        => string.IsNullOrWhiteSpace(value) || string.Equals(value, All, StringComparison.OrdinalIgnoreCase);
}
