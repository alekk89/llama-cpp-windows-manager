using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Services;
using ComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

/// <summary>A runtime-discovered editor that adds one explicit override to the visible argument field.</summary>
public sealed class BenchmarkRuntimeOptionPicker : StackPanel
{
    private sealed record Choice(RuntimeLaunchOptionDefinition Definition)
    {
        public string Name => Definition.Name;
        public override string ToString() => Name;
    }
    private readonly ComboBox _options = new() { Height = 30, DisplayMemberPath = nameof(RuntimeLaunchOptionDefinition.Name) };
    private readonly ContentControl _editorHost = new();
    private readonly TextBlock _description = BenchmarkWorkspaceView.Label("");
    private readonly Action<IReadOnlyList<string>> _add;
    private RuntimeOptionEditor? _editor;
    private IReadOnlyList<RuntimeLaunchOptionDefinition> _definitions = [];
    public event Action? DiscoverRequested;

    public BenchmarkRuntimeOptionPicker(Action<IReadOnlyList<string>> add)
    {
        _add = add;
        Children.Add(BenchmarkWorkspaceView.Action("Discover runtime options", (_, _) => DiscoverRequested?.Invoke()));
        Children.Add(BenchmarkWorkspaceView.Field("Runtime option", _options));
        Children.Add(_description);
        Children.Add(_editorHost);
        Children.Add(BenchmarkWorkspaceView.Action("Add override", (_, _) => Add()));
        _options.SelectionChanged += (_, _) =>
        {
            if (_options.SelectedItem is not Choice choice) return;
            var option = choice.Definition;
            _description.Text = option.Description;
            _editor = new LaunchRuntimeOptionEditorFactory(ChooseFile, ChooseDirectory, () => { }).Create(option);
            _editorHost.Content = _editor.Control;
        };
        SetStatus("Discover settings from the selected runtime, then choose a flag. Added values remain visible in the argument field and are saved with the plan.");
    }

    public void SetOptions(IReadOnlyList<RuntimeLaunchOptionDefinition> definitions)
    {
        _definitions = definitions;
        _options.ItemsSource = definitions.Select(option => new Choice(option)).ToArray();
        _options.SelectedIndex = 0;
        if (definitions.Count == 0) SetStatus("No additional options were recognized. You can still enter arguments directly.");
    }

    public void Filter(string query)
        => _options.ItemsSource = _definitions.Where(option => (option.Name + " " + option.Description).Contains(query, StringComparison.OrdinalIgnoreCase)).Select(option => new Choice(option)).ToArray();
    public void SetStatus(string text) => _description.Text = text;
    private void Add()
    {
        if (_editor is null) return;
        var tokens = new List<string>();
        _editor.AppendTokens(tokens);
        if (tokens.Count == 0) { SetStatus("Choose an explicit value first."); return; }
        try { _add(tokens); SetStatus("Override added to the argument field."); }
        catch (InvalidOperationException error) { SetStatus(error.Message); }
    }

    private static string? ChooseFile(string current)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { FileName = current };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
    private static string? ChooseDirectory(string current)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog();
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }
}
