using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using LocalLlmConsole.Localization;

namespace LocalLlmConsole.Models;

public sealed class EditableSettingRow : INotifyPropertyChanged
{
    private bool _isSecretVisible;
    private string _type = "text";
    private string _value = "";

    public string Group { get; set; } = "";
    public string Label { get; set; } = "";
    public string Key { get; set; } = "";
    public string ToolTip { get; set; } = "";
    public string Type
    {
        get => _type;
        set
        {
            if (_type == value) return;
            _type = value;
            OnPropertyChanged();
            OnSecretActionPropertiesChanged();
        }
    }
    public string Action { get; set; } = "";
    public string ActionToolTip { get; set; } = "";
    public bool CanAction { get; set; }
    public string RevealAction => Type == "secret"
        ? Loc.T(IsSecretVisible ? "Settings.Secret.Hide" : "Settings.Secret.Show")
        : "";
    public string RevealToolTip => Type == "secret"
        ? Loc.T(IsSecretVisible ? "Settings.Secret.HideTooltip" : "Settings.Secret.ShowTooltip")
        : "";
    public bool CanRevealAction => Type == "secret" && !string.IsNullOrWhiteSpace(Value);
    public string CopyAction => Type == "secret" ? Loc.T("Settings.Secret.Copy") : "";
    public string CopyToolTip => Type == "secret" ? Loc.T("Settings.Secret.CopyTooltip") : "";
    public bool CanCopyAction => Type == "secret" && !string.IsNullOrWhiteSpace(Value);
    public bool IsSecretVisible
    {
        get => _isSecretVisible;
        set
        {
            if (_isSecretVisible == value) return;
            _isSecretVisible = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(RevealAction));
            OnPropertyChanged(nameof(RevealToolTip));
        }
    }
    public ObservableCollection<string> Options { get; } = new();
    public string Value
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(DisplayValue));
            OnPropertyChanged(nameof(CanRevealAction));
            OnPropertyChanged(nameof(CanCopyAction));
        }
    }
    public string DisplayValue => Type == "secret" ? IsSecretVisible ? SecretDisplayValue(Value) : MaskSecret(Value) : Value;
    public JsonObject Data { get; set; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private void OnSecretActionPropertiesChanged()
    {
        OnPropertyChanged(nameof(DisplayValue));
        OnPropertyChanged(nameof(RevealAction));
        OnPropertyChanged(nameof(RevealToolTip));
        OnPropertyChanged(nameof(CanRevealAction));
        OnPropertyChanged(nameof(CopyAction));
        OnPropertyChanged(nameof(CopyToolTip));
        OnPropertyChanged(nameof(CanCopyAction));
    }

    private static string SecretDisplayValue(string value)
    {
        var secret = (value ?? "").Trim();
        return string.IsNullOrWhiteSpace(secret) ? Loc.T("Settings.Secret.NotSet") : secret;
    }

    private static string MaskSecret(string value)
    {
        var secret = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(secret)) return Loc.T("Settings.Secret.NotSet");
        var suffix = secret.Length >= 4 ? secret[^4..] : "";
        return string.IsNullOrWhiteSpace(suffix) ? "********" : $"************{suffix}";
    }
}
