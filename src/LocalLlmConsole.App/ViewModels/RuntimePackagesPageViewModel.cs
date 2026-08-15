using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class RuntimePackagesPageViewModel
{
    private readonly List<RuntimePackagePresetRow> _allRows = [];

    public ObservableCollection<RuntimePackagePresetRow> Rows { get; } = new();

    public IReadOnlyList<string> VendorFilters => RuntimeInventoryFilterService.VendorOptions;

    public IReadOnlyList<string> PlatformFilters => RuntimeInventoryFilterService.PlatformOptions;

    public string SelectedVendorFilter { get; private set; } = RuntimeInventoryFilterService.All;

    public string SelectedPlatformFilter { get; private set; } = RuntimeInventoryFilterService.All;

    public void ReplaceRows(IEnumerable<RuntimePackagePresetRow> rows)
    {
        _allRows.Clear();
        _allRows.AddRange(rows);
        ApplyFilters(SelectedVendorFilter, SelectedPlatformFilter);
    }

    public void ApplyFilters(string? vendor, string? platform)
    {
        SelectedVendorFilter = Normalize(vendor, VendorFilters);
        SelectedPlatformFilter = Normalize(platform, PlatformFilters);
        Rows.Clear();
        foreach (var row in _allRows.Where(row => RuntimeInventoryFilterService.Matches(
                     row.Vendor,
                     row.Platform,
                     SelectedVendorFilter,
                     SelectedPlatformFilter)))
            Rows.Add(row);
    }

    private static string Normalize(string? value, IReadOnlyList<string> options)
        => options.FirstOrDefault(option => string.Equals(option, value, StringComparison.OrdinalIgnoreCase))
            ?? RuntimeInventoryFilterService.All;
}
