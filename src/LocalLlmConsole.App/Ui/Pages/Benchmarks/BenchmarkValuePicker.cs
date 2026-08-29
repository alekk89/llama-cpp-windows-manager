using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalLlmConsole.Localization;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using WpfPanel = System.Windows.Controls.Panel;

namespace LocalLlmConsole;

public sealed class BenchmarkValuePicker : StackPanel
{
    private readonly char _separator;
    private readonly string[] _availableValues;
    private readonly List<string> _values = [];
    private readonly WrapPanel _selected = new() { Margin = new Thickness(0, 3, 0, 0) };
    private readonly List<Button> _renderedChips = [];
    private WpfPanel _selectionHost;
    private string _selectionLabel = "";
    private string _pendingMouseValue = "";
    private bool _updating;

    public BenchmarkValuePicker(string name, IEnumerable<string> availableValues, char separator = ',')
    {
        _separator = separator;
        _availableValues = availableValues.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        _selectionHost = _selected;
        Orientation = System.Windows.Controls.Orientation.Vertical;
        Input = new ComboBox
        {
            IsEditable = true,
            StaysOpenOnEdit = true,
            ItemsSource = _availableValues,
            Height = 28,
            MinHeight = 28,
            MinWidth = 120,
            Margin = new Thickness(0, 0, 4, 0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(Input, Loc.T("Benchmarks.ValuePicker.Choose", name));
        Input.SelectionChanged += (_, _) =>
        {
            if (_updating || Input.SelectedItem is not string value) return;
            AddValues(value, Input.IsDropDownOpen);
        };
        Input.AddHandler(UIElement.PreviewMouseLeftButtonDownEvent,
            new MouseButtonEventHandler(InputPreviewMouseLeftButtonDown), true);
        Input.AddHandler(UIElement.PreviewMouseLeftButtonUpEvent,
            new MouseButtonEventHandler(InputPreviewMouseLeftButtonUp), true);
        Input.KeyDown += (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            AddValues(Input.SelectedItem as string ?? Input.Text, Input.IsDropDownOpen);
            args.Handled = true;
        };

        AddButton = new Button
        {
            Content = "+",
            MinWidth = 36,
            MinHeight = 28,
            Height = 28,
            Padding = new Thickness(8, 2, 8, 2),
            Margin = new Thickness(0),
            ToolTip = Loc.T("Benchmarks.ValuePicker.AddTooltip")
        };
        AutomationProperties.SetName(AddButton, Loc.T("Benchmarks.ValuePicker.Add", name));
        AddButton.Click += (_, _) =>
        {
            AddValues(Input.Text);
        };

        var editor = new Grid();
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        editor.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(AddButton, 1);
        editor.Children.Add(Input);
        editor.Children.Add(AddButton);
        Children.Add(editor);
        Children.Add(_selected);
    }

    public ComboBox Input { get; }
    public Button AddButton { get; }
    public ItemCollection Items => Input.Items;
    public bool IsEditable => Input.IsEditable;
    public IReadOnlyList<string> Values => _values;
    public event EventHandler? Changed;

    public string Text
    {
        get => string.Join(_separator, _values);
        set
        {
            _values.Clear();
            foreach (var item in Split(value))
                if (!_values.Contains(item, StringComparer.OrdinalIgnoreCase))
                    _values.Add(item);
            RefreshAvailableValues();
            RenderSelected();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public void UseSharedSelectionHost(WpfPanel host, string label)
    {
        ArgumentNullException.ThrowIfNull(host);
        ClearRenderedChips();
        if (_selected.Parent is WpfPanel parent)
            parent.Children.Remove(_selected);
        _selectionHost = host;
        _selectionLabel = label;
        RenderSelected();
    }

    private void AddValues(string? text, bool keepDropDownOpen = false)
    {
        var changed = false;
        foreach (var value in Split(text))
        {
            if (_values.Contains(value, StringComparer.OrdinalIgnoreCase)) continue;
            _values.Add(value);
            changed = true;
        }
        if (!changed) return;
        RefreshAvailableValues();
        RenderSelected();
        Changed?.Invoke(this, EventArgs.Empty);
        if (keepDropDownOpen)
            ReopenDropDown();
    }

    private IEnumerable<string> Split(string? text)
        => (text ?? "")
            .Split(_separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(value => !string.IsNullOrWhiteSpace(value));

    private void Remove(string value)
    {
        _values.RemoveAll(item => item.Equals(value, StringComparison.OrdinalIgnoreCase));
        RefreshAvailableValues();
        RenderSelected();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void RenderSelected()
    {
        ClearRenderedChips();
        foreach (var value in _values)
        {
            var chip = new Button
            {
                Content = string.IsNullOrWhiteSpace(_selectionLabel)
                    ? $"{value}  ×"
                    : $"{_selectionLabel}: {value}  ×",
                Tag = value,
                MinHeight = 24,
                MinWidth = 0,
                Padding = new Thickness(7, 1, 7, 2),
                Margin = new Thickness(0, 0, 5, 3),
                ToolTip = Loc.T("Benchmarks.ValuePicker.Remove", value)
            };
            VisualRole.SetButtonRole(chip, VisualRole.Quiet);
            AutomationProperties.SetName(chip, Loc.T("Benchmarks.ValuePicker.Remove", value));
            chip.Click += (_, _) => Remove(value);
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

    private void RefreshAvailableValues()
    {
        _updating = true;
        Input.ItemsSource = _availableValues
            .Where(value => !_values.Contains(value, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        Input.SelectedIndex = -1;
        Input.Text = "";
        _updating = false;
    }

    private void InputPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs args)
    {
        var value = ComboBoxItemValue(args.OriginalSource as DependencyObject);
        if (string.IsNullOrWhiteSpace(value)) return;
        _pendingMouseValue = value;
        args.Handled = true;
    }

    private void InputPreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs args)
    {
        var value = ComboBoxItemValue(args.OriginalSource as DependencyObject);
        var pending = _pendingMouseValue;
        _pendingMouseValue = "";
        if (string.IsNullOrWhiteSpace(value)
            || !value.Equals(pending, StringComparison.OrdinalIgnoreCase))
            return;
        AddValues(value, keepDropDownOpen: true);
        args.Handled = true;
    }

    private static string ComboBoxItemValue(DependencyObject? source)
    {
        var item = FindAncestor<ComboBoxItem>(source);
        return item?.DataContext as string ?? item?.Content as string ?? "";
    }

    private void ReopenDropDown()
        => Input.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => Input.IsDropDownOpen = true));

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
