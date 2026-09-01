using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed class RuntimesPageState
{
    public TextBlock? RuntimesFolderText { get; private set; }

    public RuntimeRecord? SelectedRuntime => RuntimeGrid?.SelectedItem is RuntimeCatalogRow row ? row.Runtime : null;

    public string SelectedCudaPackagePreference => RuntimeCudaPreferenceCombo?.SelectedItem?.ToString() ?? "";

    private DataGrid? RuntimeGrid { get; set; }

    private DataGrid? RuntimePackageGrid { get; set; }

    private WpfComboBox? RuntimeCudaPreferenceCombo { get; set; }

    public void Apply(RuntimesPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        RuntimesFolderText = controls.RuntimesFolderText;
        RuntimeGrid = controls.RuntimeGrid;
        RuntimePackageGrid = controls.RuntimePackageGrid;
        RuntimeCudaPreferenceCombo = controls.RuntimeCudaPreferenceCombo;
    }

    public void ReleaseView()
    {
        RuntimesFolderText = null;
        RuntimeGrid = null;
        RuntimePackageGrid = null;
        RuntimeCudaPreferenceCombo = null;
    }

    public void RefreshRuntimePackageGrid()
        => RuntimePackageGrid?.Items.Refresh();

    public void RefreshRuntimeDownloadsGrid()
        => RuntimePackageGrid?.Items.Refresh();

    public void RestoreRuntimeSelection(string? selectedId, IReadOnlyList<RuntimeCatalogRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (RuntimeGrid is null) return;

        RuntimeGrid.SelectedItem = string.IsNullOrWhiteSpace(selectedId)
            ? null
            : rows.FirstOrDefault(row => string.Equals(row.Runtime?.Id, selectedId, StringComparison.OrdinalIgnoreCase));
    }

}
