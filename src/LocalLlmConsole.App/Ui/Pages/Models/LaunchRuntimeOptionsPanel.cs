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

public sealed class LaunchRuntimeOptionsPanel
{
    private const double EditorHeight = 28;
    private readonly WpfTextBox _rawParameters;
    private readonly Func<string, string?> _chooseFile;
    private readonly Func<string, string?> _chooseDirectory;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _status;
    private readonly WpfTextBox _preview;
    private readonly TextBlock _commandStatus;
    private readonly WpfButton _applyCommandButton;
    private readonly Dictionary<string, RuntimeOptionEditor> _editors = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RuntimeOptionRow> _optionRows = [];
    private readonly List<RuntimeOptionGroupView> _optionGroups = [];
    private string _searchQuery = "";
    private string _lastGeneratedCommand = "";
    private bool _showAdvanced;

    public LaunchRuntimeOptionsPanel(
        WpfTextBox rawParameters,
        Func<string, string?> chooseFile,
        Func<string, string?> chooseDirectory)
    {
        _rawParameters = rawParameters ?? throw new ArgumentNullException(nameof(rawParameters));
        _chooseFile = chooseFile ?? throw new ArgumentNullException(nameof(chooseFile));
        _chooseDirectory = chooseDirectory ?? throw new ArgumentNullException(nameof(chooseDirectory));
        _status = new TextBlock
        {
            Text = "",
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 5)
        };
        _preview = new WpfTextBox
        {
            IsReadOnly = false,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            MinHeight = 48,
            Margin = new Thickness(0, 6, 0, 5),
            ToolTip = Loc.T("Launch.Command.Tooltip")
        };

        _applyCommandButton = new WpfButton
        {
            Content = Loc.T("Launch.Command.ApplyAddedFlags"),
            Height = EditorHeight,
            MinHeight = EditorHeight,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            ToolTip = Loc.T("Launch.Command.ApplyTooltip")
        };
        _applyCommandButton.Click += (_, _) => ApplyCommandAdditions();
        _commandStatus = new TextBlock
        {
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 0),
            Visibility = Visibility.Collapsed
        };
        var commandContent = new StackPanel();
        commandContent.Children.Add(new TextBlock
        {
            Text = Loc.T("Launch.Command.Hint"),
            Foreground = ResourceBrush("TextMuted"),
            FontSize = 12,
            TextWrapping = TextWrapping.Wrap
        });
        commandContent.Children.Add(_preview);
        commandContent.Children.Add(_applyCommandButton);
        commandContent.Children.Add(_commandStatus);
        var additionalSettings = new StackPanel();
        additionalSettings.Children.Add(_status);
        additionalSettings.Children.Add(_rows);
        AdditionalSettingsRoot = additionalSettings;
        CommandRoot = CreateGroup("Runtime Command", commandContent);
        var content = new StackPanel();
        content.Children.Add(AdditionalSettingsRoot);
        content.Children.Add(CommandRoot);
        Root = content;
    }

    public FrameworkElement Root { get; }

    public FrameworkElement AdditionalSettingsRoot { get; }

    public FrameworkElement CommandRoot { get; }

    public WpfTextBox CommandTextBox => _preview;

    public WpfButton ApplyCommandButton => _applyCommandButton;

    public int OptionCount => _optionRows.Count;

    public IReadOnlyList<string> GroupTitles => _optionGroups.Select(group => group.Title).ToArray();

    public string StatusText => _status.Text;

    public event Action? Changed;

    public void SetLoading(string runtimeName)
    {
        ResetDiscoveredOptions();
        SetDiscoveryStatus($"Reading supported settings from {runtimeName}...");
        ApplyFilter(_searchQuery);
    }

    public void SetNoRuntime()
    {
        ResetDiscoveredOptions();
        SetDiscoveryStatus("Select a runtime to discover its additional launch settings.");
        ApplyFilter(_searchQuery);
    }

    public void SetError(string message)
    {
        ResetDiscoveredOptions();
        SetDiscoveryStatus($"Runtime settings could not be discovered: {message}");
        ApplyFilter(_searchQuery);
    }

    public void SetOptions(IReadOnlyList<RuntimeLaunchOptionDefinition> options, string runtimeName = "")
    {
        ResetDiscoveredOptions();
        var normalizedOptions = RuntimeLaunchOptionSwitchService.Normalize(options);
        foreach (var group in RuntimeLaunchOptionGroupingService.Group(normalizedOptions))
        {
            var groupRows = CreateGroupRowsGrid();
            var groupView = new RuntimeOptionGroupView(group.Title, CreateGroup(group.Title, groupRows), groupRows, []);
            foreach (var option in group.Options)
            {
                var editor = CreateEditor(option);
                _editors[option.Name] = editor;
                var row = CreateRow(option, editor.Control);
                var optionRow = new RuntimeOptionRow(row, $"{group.Title} {SearchText(option)}");
                groupView.Rows.Add(optionRow);
                _optionRows.Add(optionRow);
                groupRows.Children.Add(row);
            }

            ReflowGroup(groupView);
            _optionGroups.Add(groupView);
            _rows.Children.Add(groupView.Root);
        }

        _status.Text = "";
        _status.Visibility = Visibility.Collapsed;
        ImportRawParameters(notify: false);
        ApplyFilter(_searchQuery);
    }

    public void ApplyVisibility(bool showAdvanced, string? query)
    {
        _showAdvanced = showAdvanced;
        ApplyFilter(query);
    }

    public void ApplyFilter(string? query)
    {
        _searchQuery = query?.Trim() ?? "";
        var terms = _searchQuery.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var visible = 0;
        foreach (var group in _optionGroups)
        {
            var groupVisible = 0;
            foreach (var optionRow in group.Rows)
            {
                var matches = terms.Length == 0
                    || terms.All(term => optionRow.SearchText.Contains(term, StringComparison.OrdinalIgnoreCase));
                optionRow.Row.Visibility = matches ? Visibility.Visible : Visibility.Collapsed;
                if (matches)
                {
                    groupVisible++;
                    visible++;
                }
            }

            ReflowGroup(group);
            group.Root.Visibility = groupVisible > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        var hasVisibleContent = visible > 0 || (_status.Visibility == Visibility.Visible && terms.Length == 0);
        AdditionalSettingsRoot.Visibility = (_showAdvanced || terms.Length > 0) && hasVisibleContent
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public string BuildCustomParameters()
    {
        var tokens = CustomLaunchParameterParser.Parse(_rawParameters.Text).ToList();
        foreach (var editor in _editors.Values)
            editor.AppendTokens(tokens);
        RuntimeLaunchOptionPolicy.ValidateCustomArguments(tokens);
        return LaunchArgumentText.Format(tokens);
    }

    public void UpdatePreview(string command)
    {
        _lastGeneratedCommand = command ?? "";
        _preview.Text = _lastGeneratedCommand;
        _applyCommandButton.IsEnabled = !string.IsNullOrWhiteSpace(_lastGeneratedCommand)
                                        && !_lastGeneratedCommand.StartsWith("Preview unavailable:", StringComparison.OrdinalIgnoreCase);
        _commandStatus.Visibility = Visibility.Collapsed;
    }

    public void ApplyCommandAdditions()
    {
        try
        {
            var baseline = _lastGeneratedCommand.TrimEnd();
            var edited = _preview.Text.TrimEnd();
            if (!edited.StartsWith(baseline, StringComparison.Ordinal)
                || (edited.Length > baseline.Length && !char.IsWhiteSpace(edited[baseline.Length])))
            {
                throw new InvalidOperationException("Keep the generated command unchanged and append new flags at the end.");
            }

            var addition = edited[baseline.Length..].Trim();
            if (addition.Length == 0)
                throw new InvalidOperationException("Append one or more flags to the command first.");

            var addedTokens = CustomLaunchParameterParser.Parse(addition);
            RuntimeLaunchOptionPolicy.ValidateCustomArguments(addedTokens);
            var combined = CustomLaunchParameterParser.Parse(_rawParameters.Text).Concat(addedTokens).ToArray();
            _rawParameters.Text = LaunchArgumentText.Format(combined);
            ImportRawParameters();
            ShowCommandStatus("Added flags were applied to matching settings.", error: false);
        }
        catch (Exception ex)
        {
            ShowCommandStatus(ex.Message, error: true);
        }
    }

    public void ImportRawParameters(bool notify = true)
    {
        IReadOnlyList<string> tokens;
        try
        {
            tokens = CustomLaunchParameterParser.Parse(_rawParameters.Text);
        }
        catch (Exception ex)
        {
            _status.Text = $"Raw parameters could not be imported: {ex.Message}";
            return;
        }

        foreach (var editor in _editors.Values) editor.Clear();
        var residual = new List<string>();
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            var equalsIndex = token.IndexOf('=');
            var name = equalsIndex > 0 ? token[..equalsIndex] : token;
            var editor = FindEditor(name);
            if (editor is null)
            {
                residual.Add(token);
                continue;
            }

            var inlineValue = equalsIndex > 0 ? token[(equalsIndex + 1)..] : null;
            if (editor.Option.ValueKind == RuntimeLaunchOptionValueKind.Switch)
            {
                editor.Set(name);
                continue;
            }

            if (inlineValue is not null)
                editor.Set(inlineValue);
            else if (index + 1 < tokens.Count && !LooksLikeOptionName(tokens[index + 1]))
                editor.Set(tokens[++index]);
            else
                residual.Add(token);
        }

        _rawParameters.Text = LaunchArgumentText.Format(residual);
        if (notify) Changed?.Invoke();
    }

    private RuntimeOptionEditor? FindEditor(string alias)
        => _editors.Values.FirstOrDefault(editor => editor.Option.Aliases.Contains(alias, StringComparer.OrdinalIgnoreCase));

    private static bool LooksLikeOptionName(string value)
        => value.StartsWith("-", StringComparison.Ordinal)
           && !double.TryParse(
               value,
               System.Globalization.NumberStyles.Float,
               System.Globalization.CultureInfo.InvariantCulture,
               out _);

    private void SetDiscoveryStatus(string message)
    {
        _status.Text = message;
        _status.Visibility = Visibility.Visible;
    }

    private void ShowCommandStatus(string message, bool error)
    {
        _commandStatus.Text = message;
        _commandStatus.Foreground = ResourceBrush(error ? "Danger" : "TextMuted");
        _commandStatus.Visibility = Visibility.Visible;
    }

    private void MaterializeStructuredValues()
    {
        if (_editors.Count > 0)
            _rawParameters.Text = BuildCustomParameters();
    }

    private void ResetDiscoveredOptions()
    {
        MaterializeStructuredValues();
        _editors.Clear();
        _optionRows.Clear();
        _optionGroups.Clear();
        _rows.Children.Clear();
    }

    private static Grid CreateGroupRowsGrid()
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        return grid;
    }

    private static void ReflowGroup(RuntimeOptionGroupView group)
    {
        group.RowsGrid.RowDefinitions.Clear();
        var visibleIndex = 0;
        foreach (var optionRow in group.Rows.Where(candidate => candidate.Row.Visibility == Visibility.Visible))
        {
            var row = visibleIndex / 2;
            if (visibleIndex % 2 == 0)
                group.RowsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(optionRow.Row, row);
            Grid.SetColumn(optionRow.Row, visibleIndex % 2 == 0 ? 0 : 2);
            visibleIndex++;
        }
    }

    private static Border CreateGroup(string title, FrameworkElement rows)
    {
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 0, 0, 4) };
        header.Children.Add(new Border
        {
            Width = 3,
            Height = 17,
            Background = ResourceBrush("AccentStrong"),
            CornerRadius = new CornerRadius(2),
            Margin = new Thickness(0, 1, 6, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var content = new StackPanel();
        content.Children.Add(header);
        content.Children.Add(new Border
        {
            Height = 1,
            Background = ResourceBrush("PanelBorder"),
            Margin = new Thickness(0, 0, 0, 5)
        });
        content.Children.Add(rows);
        return new Border
        {
            Background = ResourceBrush("SurfaceRaised"),
            BorderBrush = ResourceBrush("PanelBorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 0, 6),
            Child = content
        };
    }

    private RuntimeOptionEditor CreateEditor(RuntimeLaunchOptionDefinition option)
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
                Changed?.Invoke();
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
        control.ToolTip = OptionToolTip(option);
        var editor = new RuntimeOptionEditor(option, control, valueControl);
        if (valueControl is WpfComboBox comboBox) comboBox.SelectionChanged += OnChanged;
        if (valueControl is WpfTextBox text) text.TextChanged += OnChanged;
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

    private void OnChanged(object sender, RoutedEventArgs args) => Changed?.Invoke();

    private static Grid CreateRow(RuntimeLaunchOptionDefinition option, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 2), MinHeight = EditorHeight };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(104) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock
        {
            Text = LaunchSettingMetadataService.RuntimeOptionLabel(RuntimeLaunchOptionSwitchService.DisplayFlag(option)),
            Foreground = ResourceBrush("TextSoft"),
            FontSize = 11.5,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            ToolTip = OptionToolTip(option)
        });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
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

    private static void SetSwitchState(WpfButton button, RuntimeSwitchState state, RuntimeLaunchOptionDefinition option)
    {
        button.Tag = state;
        button.Content = state.ToString();
        VisualRole.SetButtonRole(button, state == RuntimeSwitchState.Enabled ? VisualRole.Primary : "");
    }

    private static string RuntimeDefaultLabel(RuntimeLaunchOptionDefinition option)
        => string.IsNullOrWhiteSpace(option.DefaultValue) ? "" : $"Default: {option.DefaultValue}";

    private static string RuntimeDefaultChoiceLabel(RuntimeLaunchOptionDefinition option)
        => string.IsNullOrWhiteSpace(option.DefaultValue) ? "" : $"Inherit (runtime default: {option.DefaultValue})";

    private static string OptionToolTip(RuntimeLaunchOptionDefinition option)
    {
        var description = string.IsNullOrWhiteSpace(option.Description) ? option.Name : option.Description;
        var advertisedDefault = RuntimeDefaultLabel(option);
        var inheritance = string.IsNullOrWhiteSpace(advertisedDefault)
            ? "Leave unchanged to inherit the runtime default."
            : $"{advertisedDefault}. Leave unchanged to inherit it.";
        var aliases = string.Join(", ", option.Aliases.Where(alias => alias.StartsWith("--", StringComparison.Ordinal)).Distinct(StringComparer.OrdinalIgnoreCase));
        return $"{LaunchSettingMetadataService.RuntimeOptionLabel(RuntimeLaunchOptionSwitchService.DisplayFlag(option))} ({aliases}){Environment.NewLine}{description}{Environment.NewLine}{inheritance}";
    }

    private static string SearchText(RuntimeLaunchOptionDefinition option)
        => $"{LaunchSettingMetadataService.RuntimeOptionLabel(RuntimeLaunchOptionSwitchService.DisplayFlag(option))} {option.Name} {string.Join(" ", option.Aliases)} {option.ValueHint} {option.Description} {option.DefaultValue}";

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

    private sealed record RuntimeOptionEditor(
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
                SetSwitchState(button, state, Option);
            }
            if (ValueControl is WpfTextBox textBox) textBox.Text = value;
            if (ValueControl is WpfComboBox combo)
                combo.SelectedItem = combo.Items.Cast<object>().Skip(1)
                    .FirstOrDefault(item => string.Equals(item.ToString(), value, StringComparison.OrdinalIgnoreCase))
                    ?? combo.Items[0];
        }

        public void Clear()
        {
            if (ValueControl is WpfButton button) SetSwitchState(button, RuntimeSwitchState.Default, Option);
            if (ValueControl is WpfTextBox textBox) textBox.Clear();
            if (ValueControl is WpfComboBox combo) combo.SelectedIndex = 0;
        }
    }

    private sealed record RuntimeOptionRow(FrameworkElement Row, string SearchText);

    private sealed record RuntimeOptionGroupView(
        string Title,
        FrameworkElement Root,
        Grid RowsGrid,
        List<RuntimeOptionRow> Rows);

    private enum RuntimeSwitchState
    {
        Default,
        Enabled,
        Disabled
    }
}
