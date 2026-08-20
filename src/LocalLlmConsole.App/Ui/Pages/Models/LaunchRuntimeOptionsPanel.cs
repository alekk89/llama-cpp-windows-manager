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
    private readonly LaunchRuntimeOptionEditorFactory _editorFactory;
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
        ArgumentNullException.ThrowIfNull(chooseFile);
        ArgumentNullException.ThrowIfNull(chooseDirectory);
        _editorFactory = new LaunchRuntimeOptionEditorFactory(chooseFile, chooseDirectory, () => Changed?.Invoke());
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
        CommandRoot = LaunchRuntimeOptionLayout.CreateGroup("Runtime Command", commandContent);
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
            var groupRows = LaunchRuntimeOptionLayout.CreateGroupRowsGrid();
            var groupView = new RuntimeOptionGroupView(group.Title, LaunchRuntimeOptionLayout.CreateGroup(group.Title, groupRows), groupRows, []);
            foreach (var option in group.Options)
            {
                var editor = _editorFactory.Create(option);
                _editors[option.Name] = editor;
                var row = LaunchRuntimeOptionLayout.CreateRow(option, editor.Control);
                var optionRow = new RuntimeOptionRow(row, $"{group.Title} {LaunchRuntimeOptionLayout.SearchText(option)}");
                groupView.Rows.Add(optionRow);
                _optionRows.Add(optionRow);
                groupRows.Children.Add(row);
            }

            LaunchRuntimeOptionLayout.ReflowGroup(groupView);
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

            LaunchRuntimeOptionLayout.ReflowGroup(group);
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

    private static WpfBrush ResourceBrush(string key) => (WpfBrush)WpfApplication.Current.Resources[key];
}
