using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using WpfPanel = System.Windows.Controls.Panel;

namespace LocalLlmConsole;

public sealed class BenchmarkGpuConfigurationPicker : StackPanel
{
    private const string Automatic = "Automatic";
    private readonly List<BenchmarkGpuConfiguration> _values = [];
    private readonly WrapPanel _selected = new() { Margin = new Thickness(0, 3, 0, 0) };
    private readonly List<Button> _renderedChips = [];
    private WpfPanel _selectionHost;

    public BenchmarkGpuConfigurationPicker()
    {
        _selectionHost = _selected;
        Orientation = System.Windows.Controls.Orientation.Vertical;
        Mode = new ComboBox
        {
            ItemsSource = new[]
            {
                new GpuModeChoice("single", "Single"),
                new GpuModeChoice("layer", "Layer"),
                new GpuModeChoice("row", "Row"),
                new GpuModeChoice("tensor", "Tensor")
            },
            DisplayMemberPath = nameof(GpuModeChoice.Label),
            SelectedValuePath = nameof(GpuModeChoice.Value),
            MinWidth = 100,
            Height = 28,
            MinHeight = 28,
            Margin = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(Mode, Loc.T("Launch.Field.GpuMode"));

        Distribution = new TextBox
        {
            Text = Automatic,
            MinWidth = 100,
            Height = 28,
            MinHeight = 28,
            Margin = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        Distribution.ToolTip = Loc.T("Tooltip.Field.GpuSplit");
        AutomationProperties.SetName(Distribution, Loc.T("Launch.Field.GpuSplit"));

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
        AutomationProperties.SetName(AddButton, Loc.T("Benchmarks.ValuePicker.Add", Loc.T("Launch.Field.GpuSplit")));

        Mode.SelectionChanged += (_, _) => UpdateEditorState();
        Distribution.TextChanged += (_, _) => UpdateEditorState();
        AddButton.Click += (_, _) => AddCurrent();
        Distribution.KeyDown += (_, args) =>
        {
            if (args.Key != System.Windows.Input.Key.Enter || !AddButton.IsEnabled) return;
            AddCurrent();
            args.Handled = true;
        };

        var editor = new Grid();
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(6) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(Distribution, 2);
        Grid.SetColumn(AddButton, 4);
        editor.Children.Add(Mode);
        editor.Children.Add(Distribution);
        editor.Children.Add(AddButton);
        Children.Add(editor);
        Children.Add(_selected);
    }

    public ComboBox Mode { get; }
    public TextBox Distribution { get; }
    public Button AddButton { get; }
    public IReadOnlyList<BenchmarkGpuConfiguration> Values => _values;
    public event EventHandler? Changed;

    public void SetValues(IEnumerable<BenchmarkGpuConfiguration> configurations)
    {
        _values.Clear();
        foreach (var configuration in configurations.Select(Normalize).DistinctBy(Key, StringComparer.OrdinalIgnoreCase))
            _values.Add(configuration);
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
        var selectedMode = GetSelectedMode();
        if (selectedMode is null) return;
        var configuration = Normalize(new BenchmarkGpuConfiguration(selectedMode, Distribution.Text));
        if (_values.Any(value => Key(value).Equals(Key(configuration), StringComparison.OrdinalIgnoreCase))) return;
        _values.Add(configuration);
        Distribution.Text = Automatic;
        RenderSelected();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Remove(BenchmarkGpuConfiguration configuration)
    {
        _values.RemoveAll(value => Key(value).Equals(Key(configuration), StringComparison.OrdinalIgnoreCase));
        RenderSelected();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateEditorState()
    {
        var selectedMode = GetSelectedMode();
        var selected = selectedMode is not null;
        var single = selectedMode?.Equals("single", StringComparison.OrdinalIgnoreCase) == true;
        var automatic = string.IsNullOrWhiteSpace(Distribution.Text)
                        || Distribution.Text.Equals(Automatic, StringComparison.OrdinalIgnoreCase);
        AddButton.IsEnabled = selected && (single || automatic || IsValidDistribution(Distribution.Text));
        Distribution.IsEnabled = selected && !single;
        if (single)
            Distribution.Text = Automatic;
    }

    private string? GetSelectedMode()
        => Mode.SelectedItem is GpuModeChoice choice
            ? choice.Value
            : Mode.SelectedValue as string;

    private void RenderSelected()
    {
        ClearRenderedChips();
        foreach (var configuration in _values)
        {
            var chip = new Button
            {
                Content = $"Multi-GPU: {Display(configuration)}  ×",
                Tag = configuration,
                MinHeight = 24,
                MinWidth = 0,
                Padding = new Thickness(7, 1, 7, 2),
                Margin = new Thickness(0, 0, 5, 3),
                ToolTip = Loc.T("Benchmarks.ValuePicker.Remove", Display(configuration))
            };
            VisualRole.SetButtonRole(chip, VisualRole.Quiet);
            AutomationProperties.SetName(chip, Loc.T("Benchmarks.ValuePicker.Remove", Display(configuration)));
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

    private static BenchmarkGpuConfiguration Normalize(BenchmarkGpuConfiguration configuration)
    {
        var mode = (configuration.Mode ?? "").Trim().ToLowerInvariant() switch
        {
            "none" => "single",
            var value => value
        };
        var split = mode == "single"
                    || (configuration.Split ?? "").Equals(Automatic, StringComparison.OrdinalIgnoreCase)
                    || (configuration.Split ?? "").Equals("automatic", StringComparison.OrdinalIgnoreCase)
            ? ""
            : string.Join(',', (configuration.Split ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(value => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    ? number.ToString("0.###", CultureInfo.InvariantCulture)
                    : value));
        return new BenchmarkGpuConfiguration(mode, split);
    }

    private static string Key(BenchmarkGpuConfiguration configuration)
        => $"{configuration.Mode}|{configuration.Split}";

    private static string Display(BenchmarkGpuConfiguration configuration)
    {
        var mode = TitleCase(configuration.Mode);
        return $"{mode} · {(string.IsNullOrWhiteSpace(configuration.Split) ? Automatic : configuration.Split)}";
    }

    private static string TitleCase(string value)
        => value.Length == 0 ? "?" : char.ToUpperInvariant(value[0]) + value[1..].ToLowerInvariant();

    private static bool IsValidDistribution(string value)
    {
        var parts = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return parts.Length >= 2
               && parts.All(part => double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture,
                       out var number)
                   && double.IsFinite(number)
                   && number >= 0)
               && parts.Any(part => double.Parse(part, CultureInfo.InvariantCulture) > 0);
    }

    private sealed record GpuModeChoice(string Value, string Label)
    {
        public override string ToString() => Label;
    }
}
