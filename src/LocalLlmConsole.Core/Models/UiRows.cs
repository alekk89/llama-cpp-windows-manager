using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace LocalLlmConsole.Models;

public sealed class UiRow : INotifyPropertyChanged
{
    private string _c1 = "";
    private string _c2 = "";
    private string _c3 = "";
    private string _c4 = "";
    private string _c5 = "";
    private string _c6 = "";
    private string _c7 = "";
    private string _c8 = "";
    private string _c9 = "";
    private string _c10 = "";
    private string _t1 = "";
    private string _t2 = "";
    private string _t3 = "";
    private string _t4 = "";
    private string _t5 = "";
    private bool _b1 = true;
    private bool _b2 = true;
    private bool _b3 = true;
    private bool _b4 = true;
    private bool _b5 = true;
    private JsonObject _data = new();

    public string C1 { get => _c1; set => Set(ref _c1, value); }
    public string C2 { get => _c2; set => Set(ref _c2, value); }
    public string C3 { get => _c3; set => Set(ref _c3, value); }
    public string C4 { get => _c4; set => Set(ref _c4, value); }
    public string C5 { get => _c5; set => Set(ref _c5, value); }
    public string C6 { get => _c6; set => Set(ref _c6, value); }
    public string C7 { get => _c7; set => Set(ref _c7, value); }
    public string C8 { get => _c8; set => Set(ref _c8, value); }
    public string C9 { get => _c9; set => Set(ref _c9, value); }
    public string C10 { get => _c10; set => Set(ref _c10, value); }
    public string T1 { get => _t1; set => Set(ref _t1, value); }
    public string T2 { get => _t2; set => Set(ref _t2, value); }
    public string T3 { get => _t3; set => Set(ref _t3, value); }
    public string T4 { get => _t4; set => Set(ref _t4, value); }
    public string T5 { get => _t5; set => Set(ref _t5, value); }
    public bool B1 { get => _b1; set => Set(ref _b1, value); }
    public bool B2 { get => _b2; set => Set(ref _b2, value); }
    public bool B3 { get => _b3; set => Set(ref _b3, value); }
    public bool B4 { get => _b4; set => Set(ref _b4, value); }
    public bool B5 { get => _b5; set => Set(ref _b5, value); }
    public JsonObject Data { get => _data; set => Set(ref _data, value ?? new JsonObject()); }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Apply(UiRow source)
    {
        ArgumentNullException.ThrowIfNull(source);

        C1 = source.C1;
        C2 = source.C2;
        C3 = source.C3;
        C4 = source.C4;
        C5 = source.C5;
        C6 = source.C6;
        C7 = source.C7;
        C8 = source.C8;
        C9 = source.C9;
        C10 = source.C10;
        T1 = source.T1;
        T2 = source.T2;
        T3 = source.T3;
        T4 = source.T4;
        T5 = source.T5;
        B1 = source.B1;
        B2 = source.B2;
        B3 = source.B3;
        B4 = source.B4;
        B5 = source.B5;
        Data = source.Data;
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

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
    public string RevealAction => Type == "secret" ? (IsSecretVisible ? "Hide" : "Show") : "";
    public string RevealToolTip => Type == "secret"
        ? IsSecretVisible ? "Hide the full API key." : "Show the full API key in the settings grid."
        : "";
    public bool CanRevealAction => Type == "secret" && !string.IsNullOrWhiteSpace(Value);
    public string CopyAction => Type == "secret" ? "Copy" : "";
    public string CopyToolTip => Type == "secret" ? "Copy the full API key to the clipboard." : "";
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
        return string.IsNullOrWhiteSpace(secret) ? "not set" : secret;
    }

    private static string MaskSecret(string value)
    {
        var secret = (value ?? "").Trim();
        if (string.IsNullOrWhiteSpace(secret)) return "not set";
        var suffix = secret.Length >= 4 ? secret[^4..] : "";
        return string.IsNullOrWhiteSpace(suffix) ? "********" : $"************{suffix}";
    }
}
