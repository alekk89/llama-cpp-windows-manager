using System.ComponentModel;
using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed class SettingsPageState
{
    private readonly List<EditableSettingRow> _rows = [];
    private Action? _preferencesChanged;
    private Action? _apiKeyAuthenticationDisabled;
    private Action<int>? _uiScaleChanged;
    private Action<int>? _fontScaleChanged;

    public DataGrid? SettingsGrid { get; private set; }

    private WpfComboBox? ThemeCombo { get; set; }

    public string SelectedThemeValue
        => ThemeCombo?.SelectedItem?.ToString() ?? ThemeCombo?.Text ?? "";

    public void Apply(
        SettingsPageControls controls,
        IEnumerable<EditableSettingRow> rows,
        Action preferencesChanged,
        Action? apiKeyAuthenticationDisabled = null,
        Action<int>? uiScaleChanged = null,
        Action<int>? fontScaleChanged = null)
    {
        ArgumentNullException.ThrowIfNull(controls);
        ArgumentNullException.ThrowIfNull(rows);
        ArgumentNullException.ThrowIfNull(preferencesChanged);

        DetachChangeHandlers();

        ThemeCombo = controls.ThemeCombo;
        SettingsGrid = controls.SettingsGrid;
        _rows.AddRange(rows);
        _preferencesChanged = preferencesChanged;
        _apiKeyAuthenticationDisabled = apiKeyAuthenticationDisabled;
        _uiScaleChanged = uiScaleChanged;
        _fontScaleChanged = fontScaleChanged;

        ThemeCombo.SelectionChanged += ThemeSelectionChanged;
        foreach (var row in _rows)
            row.PropertyChanged += SettingRowPropertyChanged;
    }

    public void Synchronize(Action update)
    {
        ArgumentNullException.ThrowIfNull(update);

        foreach (var row in _rows)
            row.PropertyChanged -= SettingRowPropertyChanged;
        try
        {
            update();
        }
        finally
        {
            foreach (var row in _rows)
                row.PropertyChanged += SettingRowPropertyChanged;
        }
    }

    public void ReleaseView()
    {
        DetachChangeHandlers();
        ThemeCombo = null;
        SettingsGrid = null;
    }

    private void ThemeSelectionChanged(object sender, SelectionChangedEventArgs e)
        => _preferencesChanged?.Invoke();

    private void SettingRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is EditableSettingRow { Type: "readonly" }) return;
        if (e.PropertyName == nameof(EditableSettingRow.Value))
        {
            if (sender is EditableSettingRow { Key: "uiScalePercent" } uiScaleRow)
            {
                _uiScaleChanged?.Invoke(AppPreferenceService.ParseUiScalePercent(
                    uiScaleRow.Value,
                    AppSettings.DefaultUiScalePercent));
                return;
            }
            if (sender is EditableSettingRow { Key: "fontScalePercent" } fontScaleRow)
            {
                _fontScaleChanged?.Invoke(AppPreferenceService.ParseFontScalePercent(
                    fontScaleRow.Value,
                    AppSettings.DefaultFontScalePercent));
                return;
            }
            if (sender is EditableSettingRow { Key: "requireApiKeyAuth" } authenticationRow
                && !AppPreferenceService.EnableDisableValue(authenticationRow.Value, true))
                _apiKeyAuthenticationDisabled?.Invoke();
            _preferencesChanged?.Invoke();
        }
    }

    private void DetachChangeHandlers()
    {
        if (ThemeCombo is not null)
            ThemeCombo.SelectionChanged -= ThemeSelectionChanged;
        foreach (var row in _rows)
            row.PropertyChanged -= SettingRowPropertyChanged;
        _rows.Clear();
        _preferencesChanged = null;
        _apiKeyAuthenticationDisabled = null;
        _uiScaleChanged = null;
        _fontScaleChanged = null;
    }
}
