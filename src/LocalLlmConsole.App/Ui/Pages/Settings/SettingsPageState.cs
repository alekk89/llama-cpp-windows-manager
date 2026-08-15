using System.ComponentModel;
using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed class SettingsPageState
{
    private readonly List<EditableSettingRow> _rows = [];
    private Action? _preferencesChanged;

    public DataGrid? SettingsGrid { get; private set; }

    private WpfComboBox? ThemeCombo { get; set; }

    public string SelectedThemeValue
        => ThemeCombo?.SelectedItem?.ToString() ?? ThemeCombo?.Text ?? "";

    public void Apply(
        SettingsPageControls controls,
        IEnumerable<EditableSettingRow> rows,
        Action preferencesChanged)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(preferencesChanged);

        DetachChangeHandlers();

        ThemeCombo = controls.ThemeCombo;
        SettingsGrid = controls.SettingsGrid;
        _rows.AddRange(rows);
        _preferencesChanged = preferencesChanged;

        ThemeCombo.SelectionChanged += ThemeSelectionChanged;
        foreach (var row in _rows)
            row.PropertyChanged += SettingRowPropertyChanged;
    }

    private void ThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
        => _preferencesChanged?.Invoke();

    private void SettingRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableSettingRow.Value))
            _preferencesChanged?.Invoke();
    }

    private void DetachChangeHandlers()
    {
        if (ThemeCombo is not null)
            ThemeCombo.SelectionChanged -= ThemeSelectionChanged;
        foreach (var row in _rows)
            row.PropertyChanged -= SettingRowPropertyChanged;
        _rows.Clear();
        _preferencesChanged = null;
    }
}
