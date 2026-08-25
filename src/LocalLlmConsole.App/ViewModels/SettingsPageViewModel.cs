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
    private EditableSettingRow? _requireApiKeyAuthRow;
    private PropertyChangedEventHandler? _requireApiKeyAuthChanged;
    private EditableSettingRow? _modelApiKeyRow;
    private bool _synchronizing;
    private bool _applyingAuthenticationDefaults;

    public ObservableCollection<EditableSettingRow> Rows { get; } = new();

    public EditableSettingRow? CacheRow
        => Rows.FirstOrDefault(row => string.Equals(row.Key, "cache", StringComparison.Ordinal));

    public void ReplaceRows(IReadOnlyList<SettingRowDefinition> definitions)
    {
        DetachModelAccessHandler();
        Rows.Clear();
        EditableSettingRow? modelAccessRow = null;
        EditableSettingRow? requireApiKeyAuthRow = null;
        EditableSettingRow? apiKeyRow = null;
        foreach (var definition in definitions)
        {
            var row = AddRow(definition);
            if (row.Key == "modelAccessMode") modelAccessRow = row;
            if (row.Key == "requireApiKeyAuth") requireApiKeyAuthRow = row;
            if (row.Key == "modelApiKey") apiKeyRow = row;
        }

        if (modelAccessRow is null || requireApiKeyAuthRow is null || apiKeyRow is null)
            throw new InvalidOperationException("Settings page definitions are missing required network rows.");
        _modelAccessRow = modelAccessRow;
        _requireApiKeyAuthRow = requireApiKeyAuthRow;
        _modelApiKeyRow = apiKeyRow;
        _modelAccessChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(EditableSettingRow.Value))
                ApplyAuthenticationDefaults(authenticationChanged: false);
        };
        _requireApiKeyAuthChanged = (_, e) =>
        {
            if (e.PropertyName == nameof(EditableSettingRow.Value))
                ApplyAuthenticationDefaults(authenticationChanged: true);
        };
        _modelAccessRow.PropertyChanged += _modelAccessChanged;
        _requireApiKeyAuthRow.PropertyChanged += _requireApiKeyAuthChanged;
        ApplyAuthenticationDefaults(authenticationChanged: false);
    }

    public void ApplyPersistedSettings(AppSettings settings)
    {
        if (_modelAccessRow is null || _requireApiKeyAuthRow is null || _modelApiKeyRow is null)
            return;

        _synchronizing = true;
        try
        {
            _modelAccessRow.Value = AppPreferenceService.ModelAccessModeLabel(settings.ModelAccessMode);
            _requireApiKeyAuthRow.Value = AppPreferenceService.EnableDisableLabel(settings.RequireApiKeyAuth);
            _modelApiKeyRow.Value = settings.ModelApiKey;
        }
        finally
        {
            _synchronizing = false;
        }
    }

    private void DetachModelAccessHandler()
    {
        if (_modelAccessRow is not null && _modelAccessChanged is not null)
            _modelAccessRow.PropertyChanged -= _modelAccessChanged;
        if (_requireApiKeyAuthRow is not null && _requireApiKeyAuthChanged is not null)
            _requireApiKeyAuthRow.PropertyChanged -= _requireApiKeyAuthChanged;
        _modelAccessRow = null;
        _modelAccessChanged = null;
        _requireApiKeyAuthRow = null;
        _requireApiKeyAuthChanged = null;
        _modelApiKeyRow = null;
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

    private void ApplyAuthenticationDefaults(bool authenticationChanged)
    {
        if (_synchronizing
            || _applyingAuthenticationDefaults
            || _modelAccessRow is null
            || _requireApiKeyAuthRow is null
            || _modelApiKeyRow is null)
            return;

        _applyingAuthenticationDefaults = true;
        try
        {
            var accessMode = AppPreferenceService.ModelAccessMode(_modelAccessRow.Value);
            var requireApiKeyAuth = AppPreferenceService.EnableDisableValue(_requireApiKeyAuthRow.Value, true);
            if (!ModelAccessPolicy.AllowsUnauthenticatedAccess(accessMode))
            {
                if (!requireApiKeyAuth && authenticationChanged)
                    _modelAccessRow.Value = AppPreferenceService.ModelAccessModeLabel("local");
                else if (!requireApiKeyAuth)
                    _requireApiKeyAuthRow.Value = AppPreferenceService.EnableDisableLabel(true);
            }

            if (!AppPreferenceService.EnableDisableValue(_requireApiKeyAuthRow.Value, true)
                && ModelAccessPolicy.AllowsUnauthenticatedAccess(
                    AppPreferenceService.ModelAccessMode(_modelAccessRow.Value)))
                _modelApiKeyRow.Value = "";
        }
        finally
        {
            _applyingAuthenticationDefaults = false;
        }
    }

    private static string SettingActionToolTip(SettingRowDefinition definition)
        => definition.Key switch
        {
            "cache" => "Clear disposable app cache files.",
            "modelApiKey" => "Generate a new local API key.",
            _ => string.IsNullOrWhiteSpace(definition.Action) ? "" : $"Run {definition.Action} for this setting."
        };
}
