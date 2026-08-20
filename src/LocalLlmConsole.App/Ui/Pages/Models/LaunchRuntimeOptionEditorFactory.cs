using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Services;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

internal sealed class LaunchRuntimeOptionEditorFactory
{
    private const double EditorHeight = 28;
    private readonly Func<string, string?> _chooseFile;
    private readonly Func<string, string?> _chooseDirectory;
    private readonly Action _changed;

    public LaunchRuntimeOptionEditorFactory(
        Func<string, string?> chooseFile,
        Func<string, string?> chooseDirectory,
        Action changed)
    {
        _chooseFile = chooseFile;
        _chooseDirectory = chooseDirectory;
        _changed = changed;
    }

    public RuntimeOptionEditor Create(RuntimeLaunchOptionDefinition option)
    {
        FrameworkElement valueControl;
        FrameworkElement control;
        if (option.ValueKind == RuntimeLaunchOptionValueKind.Switch)
        {
            var button = SwitchButton(option);
            valueControl = button;
            control = button;
            button.Click += (_, _) =>
            {
                SetSwitchState(button, NextSwitchState(button, option), option);
                _changed();
            };
        }
        else if (option.ValueKind == RuntimeLaunchOptionValueKind.Choice)
        {
            var combo = CrispCompactControl(new WpfComboBox
            {
                ItemsSource = new[] { RuntimeDefaultChoiceLabel(option) }.Concat(option.Choices).ToArray(),
                SelectedIndex = 0,
                Height = EditorHeight,
                MinHeight = EditorHeight,
                Margin = new Thickness(0),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = ResourceBrush("TextMain")
            });
            valueControl = combo;
            control = combo;
        }
        else
        {
            var textBox = CrispCompactControl(new WpfTextBox
            {
                Height = EditorHeight,
                MinHeight = EditorHeight,
                Margin = new Thickness(0),
                Padding = new Thickness(8, 2, 8, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Foreground = ResourceBrush("TextMain")
            });
            var textEditor = TextEditor(textBox, option);
            valueControl = textBox;
            control = option.ValueKind switch
            {
                RuntimeLaunchOptionValueKind.File => PathEditor(textBox, textEditor, _chooseFile),
                RuntimeLaunchOptionValueKind.Directory => PathEditor(textBox, textEditor, _chooseDirectory),
                _ => textEditor
            };
        }

        control.Height = EditorHeight;
        control.MinHeight = EditorHeight;
        control.MinWidth = Math.Max(control.MinWidth, 72);
        control.Margin = new Thickness(0, 0, 4, 1);
        control.VerticalAlignment = VerticalAlignment.Center;
        control.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        control.ToolTip = LaunchRuntimeOptionLayout.OptionToolTip(option);
        var editor = new RuntimeOptionEditor(option, control, valueControl);
        if (valueControl is WpfComboBox comboBox) comboBox.SelectionChanged += (_, _) => _changed();
        if (valueControl is WpfTextBox text) text.TextChanged += (_, _) => _changed();
        return editor;
    }

    private static Grid TextEditor(WpfTextBox textBox, RuntimeLaunchOptionDefinition option)
    {
        var hint = new TextBlock
        {
            Text = RuntimeDefaultLabel(option),
            Foreground = ResourceBrush("TextMain"),
            FontFamily = new WpfFontFamily("Segoe UI"),
            FontSize = 12,
            FontWeight = FontWeights.Normal,
            Margin = new Thickness(9, 0, 7, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            IsHitTestVisible = false
        };
        TextOptions.SetTextHintingMode(hint, TextHintingMode.Fixed);
        textBox.TextChanged += (_, _) =>
            hint.Visibility = string.IsNullOrEmpty(textBox.Text) ? Visibility.Visible : Visibility.Collapsed;
        var host = new Grid { Height = EditorHeight };
        host.Children.Add(textBox);
        host.Children.Add(hint);
        return host;
    }

    private static Grid PathEditor(WpfTextBox textBox, FrameworkElement textEditor, Func<string, string?> choose)
    {
        var grid = new Grid { Height = EditorHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        textEditor.Margin = new Thickness(0, 0, 5, 0);
        grid.Children.Add(textEditor);
        var button = CrispCompactControl(new WpfButton
        {
            Content = Loc.T("Common.ChooseButton"),
            Height = EditorHeight,
            MinHeight = EditorHeight,
            MinWidth = 62,
            Margin = new Thickness(0)
        });
        button.Click += (_, _) =>
        {
            var selected = choose(textBox.Text.Trim());
            if (!string.IsNullOrWhiteSpace(selected)) textBox.Text = selected;
        };
        Grid.SetColumn(button, 1);
        grid.Children.Add(button);
        return grid;
    }

    private static WpfButton SwitchButton(RuntimeLaunchOptionDefinition option)
    {
        var button = CrispCompactControl(new WpfButton
        {
            Height = EditorHeight,
            MinHeight = EditorHeight,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Tag = RuntimeSwitchState.Default
        });
        SetSwitchState(button, RuntimeSwitchState.Default, option);
        return button;
    }

    private static RuntimeSwitchState NextSwitchState(WpfButton button, RuntimeLaunchOptionDefinition option)
    {
        var current = button.Tag is RuntimeSwitchState state ? state : RuntimeSwitchState.Default;
        return current switch
        {
            RuntimeSwitchState.Default when !string.IsNullOrWhiteSpace(option.EnabledName) => RuntimeSwitchState.Enabled,
            RuntimeSwitchState.Default when !string.IsNullOrWhiteSpace(option.DisabledName) => RuntimeSwitchState.Disabled,
            RuntimeSwitchState.Enabled when !string.IsNullOrWhiteSpace(option.DisabledName) => RuntimeSwitchState.Disabled,
            RuntimeSwitchState.Enabled => RuntimeSwitchState.Default,
            _ => RuntimeSwitchState.Default
        };
    }

    internal static void SetSwitchState(WpfButton button, RuntimeSwitchState state, RuntimeLaunchOptionDefinition option)
    {
        button.Tag = state;
        button.Content = state.ToString();
        VisualRole.SetButtonRole(button, state == RuntimeSwitchState.Enabled ? VisualRole.Primary : "");
    }

    private static string RuntimeDefaultLabel(RuntimeLaunchOptionDefinition option)
        => string.IsNullOrWhiteSpace(option.DefaultValue) ? "" : $"Default: {option.DefaultValue}";

    private static string RuntimeDefaultChoiceLabel(RuntimeLaunchOptionDefinition option)
        => string.IsNullOrWhiteSpace(option.DefaultValue) ? "" : $"Inherit (runtime default: {option.DefaultValue})";

    private static T CrispCompactControl<T>(T control) where T : System.Windows.Controls.Control
    {
        control.FontFamily = new WpfFontFamily("Segoe UI");
        control.FontSize = 12;
        control.FontWeight = FontWeights.Normal;
        control.SnapsToDevicePixels = true;
        control.UseLayoutRounding = true;
        TextOptions.SetTextFormattingMode(control, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(control, TextRenderingMode.ClearType);
        TextOptions.SetTextHintingMode(control, TextHintingMode.Fixed);
        return control;
    }

    private static WpfBrush ResourceBrush(string key) => (WpfBrush)WpfApplication.Current.Resources[key];
}

internal sealed record RuntimeOptionEditor(
    RuntimeLaunchOptionDefinition Option,
    FrameworkElement Control,
    FrameworkElement ValueControl)
{
    public void AppendTokens(ICollection<string> tokens)
    {
        if (Option.ValueKind == RuntimeLaunchOptionValueKind.Switch)
        {
            var argument = Value();
            if (!string.IsNullOrWhiteSpace(argument)) tokens.Add(argument);
            return;
        }

        var value = Value();
        if (string.IsNullOrWhiteSpace(value)) return;
        tokens.Add(Option.Name);
        tokens.Add(value);
    }

    public string Value() => ValueControl switch
    {
        WpfButton { Tag: RuntimeSwitchState.Enabled } => Option.EnabledName,
        WpfButton { Tag: RuntimeSwitchState.Disabled } => Option.DisabledName,
        WpfComboBox combo => combo.SelectedIndex <= 0 ? "" : combo.SelectedItem?.ToString() ?? "",
        WpfTextBox textBox => textBox.Text.Trim(),
        _ => ""
    };

    public void Set(string value)
    {
        if (ValueControl is WpfButton button)
        {
            var state = string.Equals(value, Option.DisabledName, StringComparison.OrdinalIgnoreCase)
                ? RuntimeSwitchState.Disabled
                : RuntimeSwitchState.Enabled;
            LaunchRuntimeOptionEditorFactory.SetSwitchState(button, state, Option);
        }
        if (ValueControl is WpfTextBox textBox) textBox.Text = value;
        if (ValueControl is WpfComboBox combo)
            combo.SelectedItem = combo.Items.Cast<object>().Skip(1)
                .FirstOrDefault(item => string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase))
                ?? combo.Items[0];
    }

    public void Clear()
    {
        if (ValueControl is WpfButton button) LaunchRuntimeOptionEditorFactory.SetSwitchState(button, RuntimeSwitchState.Default, Option);
        if (ValueControl is WpfTextBox textBox) textBox.Clear();
        if (ValueControl is WpfComboBox combo) combo.SelectedIndex = 0;
    }
}

internal enum RuntimeSwitchState
{
    Default,
    Enabled,
    Disabled
}
