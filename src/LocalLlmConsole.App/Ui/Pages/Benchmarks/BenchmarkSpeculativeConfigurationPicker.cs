using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using WpfPanel = System.Windows.Controls.Panel;

namespace LocalLlmConsole;

public sealed class BenchmarkSpeculativeConfigurationPicker : StackPanel
{
    private readonly List<BenchmarkSpeculativeConfiguration> _values = [];
    private readonly WrapPanel _selected = new() { Margin = new Thickness(0, 3, 0, 0) };
    private readonly List<Button> _renderedChips = [];
    private WpfPanel _selectionHost;

    public BenchmarkSpeculativeConfigurationPicker()
    {
        _selectionHost = _selected;
        Orientation = System.Windows.Controls.Orientation.Vertical;
        Type = ChoiceBox(TypeChoices);
        AutomationProperties.SetName(Type, "Speculative type");

        Head = ChoiceBox(HeadChoices);
        AutomationProperties.SetName(Head, "Speculative head");

        AddButton = new Button
        {
            Content = "+",
            IsEnabled = false,
            MinWidth = 36,
            MinHeight = 28,
            Height = 28,
            Margin = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("Benchmarks.ValuePicker.AddTooltip")
        };
        AutomationProperties.SetName(AddButton, "Add speculative configuration");

        Type.SelectionChanged += (_, _) => UpdateEditorState();
        Head.SelectionChanged += (_, _) => UpdateEditorState();
        AddButton.Click += (_, _) => AddCurrent();

        var editor = new Grid();
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(Head, 2);
        Grid.SetColumn(AddButton, 4);
        editor.Children.Add(Type);
        editor.Children.Add(Head);
        editor.Children.Add(AddButton);
        Children.Add(editor);
        Children.Add(_selected);
    }

    public ComboBox Type { get; }
    public ComboBox Head { get; }
    public Button AddButton { get; }
    public IReadOnlyList<BenchmarkSpeculativeConfiguration> Values => _values;
    public event EventHandler? Changed;

    public void SetValues(IEnumerable<BenchmarkSpeculativeConfiguration> configurations)
    {
        _values.Clear();
        _values.AddRange(configurations.Select(Normalize).DistinctBy(Key, StringComparer.OrdinalIgnoreCase));
        RenderSelected();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void UseSharedSelectionHost(WpfPanel host)
    {
        ArgumentNullException.ThrowIfNull(host);
        ClearRenderedChips();
        if (_selected.Parent is WpfPanel parent)
            parent.Children.Remove(_selected);
        _selectionHost = host;
        RenderSelected();
    }

    private void AddCurrent()
    {
        if (Type.SelectedItem is not Choice type || Head.SelectedItem is not Choice head) return;
        var configuration = Normalize(new BenchmarkSpeculativeConfiguration(type.Value, head.Value));
        if (_values.Any(value => Key(value).Equals(Key(configuration), StringComparison.OrdinalIgnoreCase))) return;
        _values.Add(configuration);
        RenderSelected();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(BenchmarkSpeculativeConfiguration configuration)
    {
        _values.RemoveAll(value => Key(value).Equals(Key(configuration), StringComparison.OrdinalIgnoreCase));
        RenderSelected();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEditorState()
        => AddButton.IsEnabled = Type.SelectedItem is Choice && Head.SelectedItem is Choice;

    private void RenderSelected()
    {
        ClearRenderedChips();
        foreach (var configuration in _values)
        {
            var display = Display(configuration);
            var chip = new Button
            {
                Content = $"Speculative: {display}  ×",
                Tag = configuration,
                MinHeight = 24,
                MinWidth = 0,
                Padding = new Thickness(7, 1, 7, 2),
                Margin = new Thickness(0, 0, 5, 3),
                ToolTip = $"Remove {display}"
            };
            VisualRole.SetButtonRole(chip, VisualRole.Quiet);
            AutomationProperties.SetName(chip, $"Remove {display}");
            chip.Click += (_, _) => Remove(configuration);
            _selectionHost.Children.Add(chip);
            _renderedChips.Add(chip);
        }
        if (ReferenceEquals(_selectionHost, _selected))
            _selected.Visibility = _values.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearRenderedChips()
    {
        foreach (var chip in _renderedChips)
            _selectionHost.Children.Remove(chip);
        _renderedChips.Clear();
    }

    private static BenchmarkSpeculativeConfiguration Normalize(BenchmarkSpeculativeConfiguration configuration)
        => new(
            (configuration.Type ?? "none").Trim().ToLowerInvariant(),
            string.IsNullOrWhiteSpace(configuration.Head) ? "profile" : configuration.Head.Trim().ToLowerInvariant());

    private static string Key(BenchmarkSpeculativeConfiguration configuration)
        => $"{configuration.Type}|{configuration.Head}";

    private static string Display(BenchmarkSpeculativeConfiguration configuration)
        => $"{Label(TypeChoices, configuration.Type)} · {Label(HeadChoices, configuration.Head)}";

    private static string Label(IReadOnlyList<Choice> choices, string value)
        => choices.FirstOrDefault(choice => choice.Value.Equals(value, StringComparison.OrdinalIgnoreCase))?.Label ?? value;

    private static ComboBox ChoiceBox(IReadOnlyList<Choice> choices)
        => new()
        {
            ItemsSource = choices,
            DisplayMemberPath = nameof(Choice.Label),
            SelectedValuePath = nameof(Choice.Value),
            MinWidth = 100,
            Height = 28,
            MinHeight = 28,
            Margin = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };

    private static readonly Choice[] TypeChoices =
    [
        new("none", "None"), new("atomic-mtp", "Atomic MTP"), new("draft-mtp", "Draft MTP"),
        new("draft-simple", "Draft simple"), new("draft-eagle3", "Draft Eagle 3"),
        new("draft-dflash", "Draft DFlash"), new("draft-dspark", "Draft DSpark"),
        new("ngram-simple", "N-gram simple"), new("ngram-map-k", "N-gram map K"),
        new("ngram-map-k4v", "N-gram map K4V"), new("ngram-mod", "N-gram mod"),
        new("ngram-cache", "N-gram cache")
    ];

    private static readonly Choice[] HeadChoices = [new("profile", "Profile head"), new("auto", "Automatic")];

    private sealed record Choice(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
