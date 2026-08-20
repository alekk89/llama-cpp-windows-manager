using System.Collections.ObjectModel;
using System.ComponentModel;

namespace LocalLlmConsole.ViewModels;

public sealed record SettingRowDefinition(
    string Group,
    string Label,
    string Key,
    string Value,
    string Type = "text",
    IEnumerable<string>? Options = null,
    string Action = "",
    string ToolTip = "");

public sealed class SettingsPageViewModel
{
    private EditableSettingRow? _modelAccessRow;
    private PropertyChangedEventHandler? _modelAccessChanged;

    public ObservableCollection<EditableSettingRow> Rows { get; } = new();

    public EditableSettingRow? CacheRow
        => Rows.FirstOrDefault(row => string.Equals(row.Key, "cache", StringComparison.Ordinal));

    public void ReplaceRows(IReadOnlyList<SettingRowDefinition> definitions)
    {
        DetachModelAccessHandler();
        Rows.Clear();
        EditableSettingRow? modelAccessRow = null;
        EditableSettingRow? apiKeyRow = null;
        foreach (var definition in definitions)
        {
            var row = AddRow(definition);
            if (row.Key == "modelAccessMode") modelAccessRow = row;
            if (row.Key == "modelApiKey") apiKeyRow = row;
        }

        if (modelAccessRow is null || apiKeyRow is null)
            throw new InvalidOperationException("Settings page definitions are missing required network rows.");
        _modelAccessRow = modelAccessRow;
        _modelAccessChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(EditableSettingRow.Value))
                ApplyModelAccessDefaults(apiKeyRow);
        };
        _modelAccessRow.PropertyChanged += _modelAccessChanged;
        ApplyModelAccessDefaults(apiKeyRow);
    }

    private void DetachModelAccessHandler()
    {
        if (_modelAccessRow is not null && _modelAccessChanged is not null)
            _modelAccessRow.PropertyChanged -= _modelAccessChanged;
        _modelAccessRow = null;
        _modelAccessChanged = null;
    }

    private EditableSettingRow AddRow(SettingRowDefinition definition)
    {
        var row = new EditableSettingRow
        {
            Group = definition.Group,
            Label = definition.Label,
            Key = definition.Key,
            Type = definition.Type,
            Value = definition.Value,
            ToolTip = string.IsNullOrWhiteSpace(definition.ToolTip)
                ? $"{definition.Label} setting."
                : definition.ToolTip,
            Action = definition.Action,
            ActionToolTip = SettingActionToolTip(definition),
            CanAction = !string.IsNullOrWhiteSpace(definition.Action)
        };
        if (definition.Options is not null)
        {
            foreach (var option in definition.Options)
                row.Options.Add(option);
        }
        Rows.Add(row);
        return row;
    }

    private static void ApplyModelAccessDefaults(EditableSettingRow apiKeyRow)
    {
        if (string.IsNullOrWhiteSpace(apiKeyRow.Value))
            apiKeyRow.Value = ApiSecurity.GenerateHexToken(32);
    }

    private static string SettingActionToolTip(SettingRowDefinition definition)
        => definition.Key switch
        {
            "cache" => "Clear disposable app cache files.",
            "modelApiKey" => "Generate a new local API key.",
            _ => string.IsNullOrWhiteSpace(definition.Action) ? "" : $"Run {definition.Action} for this setting."
        };
}
