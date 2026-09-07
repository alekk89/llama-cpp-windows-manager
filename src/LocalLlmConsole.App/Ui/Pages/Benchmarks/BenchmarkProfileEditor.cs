using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using CheckBox = System.Windows.Controls.CheckBox;
using TextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class BenchmarkProfileEditor : StackPanel
{
    private sealed record Editor(PropertyInfo Property, CheckBox Enabled, TextBox Value, FrameworkElement Row);
    private readonly List<Editor> _editors = [];
    private readonly Action _changed;
    private bool _applying;
    private ModelLaunchSettings? _inherited;

    public BenchmarkProfileEditor(Action changed)
    {
        _changed = changed;
        Children.Add(BenchmarkWorkspaceView.Label(Loc.T("Benchmarks.ProfileOverridesHelp")));
        foreach (var property in BenchmarkProfileOverrides.Properties)
        {
            var check = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
            var input = BenchmarkWorkspaceView.Input("");
            input.IsEnabled = false;
            var value = new DockPanel();
            value.Children.Add(check);
            value.Children.Add(input);
            var label = Regex.Replace(property.Name, "([a-z])([A-Z])", "$1 $2");
            check.ToolTip = $"Override {label} for this benchmark only";
            input.ToolTip = property.Name + " · " + property.PropertyType.Name;
            System.Windows.Automation.AutomationProperties.SetName(input, label);
            System.Windows.Automation.AutomationProperties.SetName(check, "Override " + label);
            var row = BenchmarkWorkspaceView.Field(label, value);
            Children.Add(row);
            var editor = new Editor(property, check, input, row);
            _editors.Add(editor);
            check.Click += (_, _) => { input.IsEnabled = check.IsChecked == true; Notify(); };
            input.TextChanged += (_, _) => { if (check.IsChecked == true) Notify(); };
        }
    }

    public IReadOnlyDictionary<string, string> Build()
        => _editors.Where(editor => editor.Enabled.IsChecked == true)
            .ToDictionary(editor => editor.Property.Name, editor => editor.Value.Text, StringComparer.OrdinalIgnoreCase);

    public void Apply(IReadOnlyDictionary<string, string> values)
    {
        _applying = true;
        foreach (var editor in _editors)
        {
            var entry = values.FirstOrDefault(pair => pair.Key.Equals(editor.Property.Name, StringComparison.OrdinalIgnoreCase));
            editor.Enabled.IsChecked = entry.Key is not null;
            editor.Value.IsEnabled = entry.Key is not null;
            editor.Value.Text = entry.Key is not null ? entry.Value : Inherited(editor.Property);
        }
        _applying = false;
    }

    public void SetInherited(ModelLaunchSettings? settings)
    {
        _inherited = settings;
        _applying = true;
        foreach (var editor in _editors.Where(editor => editor.Enabled.IsChecked != true))
            editor.Value.Text = Inherited(editor.Property);
        _applying = false;
    }

    public void Filter(string query)
    {
        foreach (var editor in _editors)
            editor.Row.Visibility = editor.Property.Name.Contains(query.Replace(" ", ""), StringComparison.OrdinalIgnoreCase)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    public void AddCustomArguments(IReadOnlyList<string> added)
    {
        var editor = _editors.Single(editor => editor.Property.Name == nameof(ModelLaunchSettings.CustomParameters));
        var tokens = CustomLaunchParameterParser.Parse(editor.Value.Text).ToList();
        if (tokens.Contains(added[0], StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("This flag is already present. Edit its value in Custom parameters.");
        tokens.AddRange(added);
        RuntimeLaunchOptionPolicy.ValidateCustomArguments(tokens);
        editor.Enabled.IsChecked = true;
        editor.Value.IsEnabled = true;
        editor.Value.Text = LaunchArgumentText.Format(tokens);
    }

    private string Inherited(PropertyInfo property) => _inherited is null ? "" : Convert.ToString(property.GetValue(_inherited), CultureInfo.InvariantCulture) ?? "";
    private void Notify() { if (!_applying) _changed(); }
}
