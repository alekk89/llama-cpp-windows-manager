using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed record ProfileImportDialogResult(IReadOnlyList<ProfileImportChoice> Choices, ProfileImportPreview Preview);

public static class ProfileImportDialog
{
    public static ModelRecord? ChooseModel(Window owner, IReadOnlyList<ModelRecord> models)
    {
        var window = Window(owner, Loc.T("Profiles.Transfer.CopyTitle"), 520, 230);
        var panel = Panel();
        var combo = new WpfComboBox { ItemsSource = models, ItemTemplate = LabelTemplate(nameof(ModelRecord.Name)), SelectedIndex = models.Count > 0 ? 0 : -1, Margin = new Thickness(0, 12, 0, 12) };
        panel.Children.Add(Text(Loc.T("Profiles.Transfer.TargetPrompt")));
        panel.Children.Add(combo);
        panel.Children.Add(Button(Loc.T("Common.Continue"), () => window.DialogResult = combo.SelectedItem is ModelRecord));
        window.Content = panel;
        return window.ShowDialog() == true ? combo.SelectedItem as ModelRecord : null;
    }

    public static ProfileImportDialogResult? Show(Window owner, ModelRecord model, ProfileBundle bundle,
        ProfileImportPreview initial, IReadOnlyList<RuntimeRecord> runtimes,
        Func<IReadOnlyList<ProfileImportChoice>, Task<ProfileImportPreview>> previewAsync)
    {
        var window = Window(owner, Loc.T("Profiles.Transfer.TargetTitle", model.Name), 850, 760);
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(Text(Loc.T("Profiles.Transfer.PreviewPrompt")));
        root.SetResourceReference(System.Windows.Controls.Panel.BackgroundProperty, "AppBack");
        var editors = new List<Editor>();
        foreach (var row in initial.Rows)
        {
            var editor = new Editor(row.Index, bundle.Profiles[row.Index], row, runtimes);
            editors.Add(editor);
        }
        var editorView = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 80 };
        var profilePicker = new WpfComboBox
        {
            ItemsSource = editors,
            ItemTemplate = LabelTemplate(nameof(Editor.Title)),
            SelectedIndex = editors.Count > 0 ? 0 : -1,
            Margin = new Thickness(0, 10, 0, 8)
        };
        profilePicker.SelectionChanged += (_, _) => editorView.Content = (profilePicker.SelectedItem as Editor)?.Panel;
        editorView.Content = editors.FirstOrDefault()?.Panel;
        Grid.SetRow(profilePicker, 1); root.Children.Add(profilePicker);
        Grid.SetRow(editorView, 2); root.Children.Add(editorView);
        var summary = Text(Describe(initial));
        var summaryScroller = new ScrollViewer { Content = summary, MaxHeight = 100, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(summaryScroller, 3); root.Children.Add(summaryScroller);
        ProfileImportDialogResult? result = null;
        ProfileImportDialogResult? reviewed = null;
        var apply = Button(Loc.T("Profiles.Transfer.Apply"), () => { result = reviewed; window.DialogResult = true; });
        apply.IsEnabled = false;
        var review = new WpfButton { Content = Loc.T("Profiles.Transfer.Review"), Margin = new Thickness(0, 8, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        review.Click += async (_, _) =>
        {
            review.IsEnabled = false; apply.IsEnabled = false;
            foreach (var editor in editors) editor.Panel.IsEnabled = false;
            try
            {
                var choices = editors.Select(editor => editor.Choice()).ToArray();
                var preview = await previewAsync(choices);
                summary.Text = Describe(preview);
                reviewed = new ProfileImportDialogResult(choices, preview);
                apply.IsEnabled = preview.CanApply;
            }
            catch (Exception ex) { summary.Text = ex.Message; reviewed = null; }
            finally { review.IsEnabled = true; foreach (var editor in editors) editor.Panel.IsEnabled = true; }
        };
        foreach (var editor in editors) editor.Changed += () => { apply.IsEnabled = false; reviewed = null; };
        var buttons = new WrapPanel();
        buttons.Children.Add(review); buttons.Children.Add(apply); buttons.Children.Add(Button(Loc.T("Common.Cancel"), () => window.DialogResult = false));
        Grid.SetRow(buttons, 4); root.Children.Add(buttons);
        window.Content = root;
        return window.ShowDialog() == true ? result : null;
    }

    private static string Describe(ProfileImportPreview preview)
        => preview.Rows.Count == 0 ? Loc.T("Profiles.Transfer.AllSkipped")
            : string.Join("\n", preview.Rows.Select(row => Loc.T("Profiles.Transfer.Row", row.Name, row.ReplaceId is null ? Loc.T("Profiles.Transfer.New") : Loc.T("Profiles.Transfer.ReplaceExisting"), row.Settings.Port) + "\n"
                + string.Join("\n", row.Errors.Select(error => "ERROR: " + error).Concat(row.Warnings))));

    private sealed class Editor
    {
        private readonly int _index;
        private readonly WpfTextBox _name;
        private readonly WpfComboBox _conflict;
        private readonly WpfComboBox _runtime;
        private readonly WpfTextBox _settings;
        public string Title { get; }
        public StackPanel Panel { get; } = ProfileImportDialog.Panel();
        public event Action? Changed;

        public Editor(int index, PortableLaunchProfile source, ProfileImportRow row, IReadOnlyList<RuntimeRecord> runtimes)
        {
            _index = index;
            Title = source.Name;
            _name = Box(source.Name);
            _conflict = new WpfComboBox
            {
                ItemsSource = Enum.GetValues<ProfileConflictAction>().Select(action => new ConflictItem(action, ConflictLabel(action))).ToArray(),
                ItemTemplate = LabelTemplate(nameof(ConflictItem.Label)),
                SelectedValuePath = nameof(ConflictItem.Action),
                SelectedValue = ProfileConflictAction.KeepBoth
            };
            _runtime = new WpfComboBox { ItemsSource = runtimes.Where(RuntimeAvailabilityService.IsAvailable).ToArray(), ItemTemplate = LabelTemplate(nameof(RuntimeRecord.Name)), SelectedItem = runtimes.FirstOrDefault(runtime => runtime.Id == row.Settings.RuntimeId) };
            var node = ProfileBundleCodec.SettingsNode(row.Settings); node.Remove("runtimeId");
            _settings = Box(node.ToJsonString(ProfileBundleCodec.JsonOptions));
            _settings.AcceptsReturn = true; _settings.AcceptsTab = true; _settings.Height = 280;
            _settings.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _settings.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            Panel.Children.Add(Text(Loc.T("Profiles.Transfer.Name"))); Panel.Children.Add(_name);
            Panel.Children.Add(Text(Loc.T("Profiles.Transfer.Conflict"))); Panel.Children.Add(_conflict);
            Panel.Children.Add(Text(Loc.T("Profiles.Transfer.Runtime"))); Panel.Children.Add(_runtime);
            var browse = new WrapPanel();
            foreach (var (label, key) in new[] { (Loc.T("Profiles.Transfer.BrowseVision"), "visionProjectorPath"), (Loc.T("Profiles.Transfer.BrowseDraft"), "specDraftModelPath"), (Loc.T("Profiles.Transfer.BrowseMtp"), "mtpHeadPath") })
                browse.Children.Add(Button(label, () => Browse(key)));
            Panel.Children.Add(browse);
            Panel.Children.Add(Text(Loc.T("Profiles.Transfer.SettingsHint")));
            Panel.Children.Add(_settings);
            if (source.Annotations is not null) Panel.Children.Add(Text(Loc.T("Profiles.Transfer.Notes", source.Annotations.Notes) + "\n" + string.Join("\n", source.Annotations.SettingNotes?.Select(pair => $"{pair.Key}: {pair.Value}") ?? [])));
            _name.TextChanged += (_, _) => Changed?.Invoke();
            _settings.TextChanged += (_, _) => Changed?.Invoke();
            _conflict.SelectionChanged += (_, _) => Changed?.Invoke();
            _runtime.SelectionChanged += (_, _) => Changed?.Invoke();
        }

        public ProfileImportChoice Choice()
        {
            var action = (ProfileConflictAction)_conflict.SelectedValue;
            return new ProfileImportChoice(_index, action, _name.Text, (_runtime.SelectedItem as RuntimeRecord)?.Id,
                action == ProfileConflictAction.Skip ? null : JsonNode.Parse(_settings.Text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject());
        }

        private void Browse(string key)
        {
            var picker = new Microsoft.Win32.OpenFileDialog { Filter = "GGUF companions (*.gguf)|*.gguf" };
            if (picker.ShowDialog() != true) return;
            try
            {
                var node = JsonNode.Parse(_settings.Text)!.AsObject();
                node[key] = picker.FileName;
                _settings.Text = node.ToJsonString(ProfileBundleCodec.JsonOptions);
            }
            catch (JsonException) { System.Windows.MessageBox.Show(Loc.T("Profiles.Transfer.InvalidSettings")); }
        }
    }

    private sealed record ConflictItem(ProfileConflictAction Action, string Label);

    private static string ConflictLabel(ProfileConflictAction action) => action switch
    {
        ProfileConflictAction.KeepBoth => Loc.T("Profiles.Transfer.KeepBoth"),
        ProfileConflictAction.Replace => Loc.T("Profiles.Transfer.Replace"),
        _ => Loc.T("Profiles.Transfer.Skip")
    };

    private static Window Window(Window owner, string title, double width, double height)
    {
        var window = new Window { Owner = owner, Title = title, Width = width, Height = height, MinWidth = Math.Min(width, 520), MinHeight = Math.Min(height, 440), WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.CanResize };
        window.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, "AppBack");
        window.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "TextMain");
        return window;
    }

    private static DataTemplate LabelTemplate(string property)
    {
        var label = new FrameworkElementFactory(typeof(TextBlock));
        label.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(property));
        label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        return new DataTemplate { VisualTree = label };
    }
    private static StackPanel Panel() => new() { Margin = new Thickness(16) };
    private static TextBlock Text(string value) => new() { Text = value, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 4) };
    private static WpfTextBox Box(string value) => new() { Text = value, Margin = new Thickness(0, 4, 0, 8) };
    private static WpfButton Button(string label, Action action)
    {
        var button = new WpfButton { Content = label, Margin = new Thickness(0, 8, 8, 0), Padding = new Thickness(12, 6, 12, 6) };
        button.Click += (_, _) => action(); return button;
    }
}
