using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.UiTests;

public sealed class WpfEditableComboBoxTests : WpfUiTestBase
{
    [Fact]
    public async Task EditableComboBoxShowsThemedTextBoxAndKeepsReadOnlyCombosUnchanged()
    {
        await RunStaAsync(() =>
        {
            var editable = new ComboBox { IsEditable = true, ItemsSource = new[] { "mmap", "mlock" } };
            var readOnly = new ComboBox { ItemsSource = new[] { "mmap", "mlock" }, SelectedIndex = 0 };
            var host = new StackPanel { Width = 240 };
            host.Children.Add(editable);
            host.Children.Add(readOnly);
            var window = new Window { Content = host, Width = 300, Height = 160, ShowInTaskbar = false };
            window.Show();
            try
            {
                AssertEditableTemplate(editable, readOnly);
            }
            finally
            {
                window.Close();
            }
        });
    }

    private static void AssertEditableTemplate(ComboBox editable, ComboBox readOnly)
    {
        Layout(editable);

        var editor = EditableTextBox(editable);
        Assert.Equal(Visibility.Visible, editor.Visibility);
        Assert.Equal(Visibility.Collapsed, SelectionPresenter(editable).Visibility);
        Assert.True(editor.ActualHeight > 0);
        Assert.True(editor.ActualWidth > 0);
        Assert.False(editable.IsTabStop);

        editor.Text = "mmap,mlock";
        Assert.Equal("mmap,mlock", editable.Text);
        editable.SelectedItem = "mlock";
        Assert.Equal("mlock", editor.Text);
        editable.Text = "dio";
        Assert.Equal("dio", editor.Text);

        Assert.Equal(Visibility.Collapsed, EditableTextBox(readOnly).Visibility);
        var selection = SelectionPresenter(readOnly);
        Assert.Equal(Visibility.Visible, selection.Visibility);
        Assert.Same(Application.Current.Resources["TextMain"], TextElement.GetForeground(selection));

        var originalAccent = Application.Current.Resources["Accent"];
        var originalAccentColor = ((SolidColorBrush)originalAccent).Color;
        var restoreTheme = SnapshotThemeResources();
        try
        {
            foreach (var theme in new[] { "light", "dark" })
            {
                LocalLlmConsole.ApplicationThemeService.Apply(theme);
                var caret = Assert.IsType<SolidColorBrush>(editor.CaretBrush);
                var background = Assert.IsType<SolidColorBrush>(editable.Background);
                Assert.Equal(((SolidColorBrush)Application.Current.Resources["Accent"]).Color, caret.Color);
                Assert.Equal(((SolidColorBrush)Application.Current.Resources["AccentSoft"]).Color,
                    Assert.IsType<SolidColorBrush>(editor.SelectionBrush).Color);
                Assert.NotEqual(background.Color, caret.Color);
                Assert.Equal(((SolidColorBrush)Application.Current.Resources["TextMain"]).Color,
                    Assert.IsType<SolidColorBrush>(editor.Foreground).Color);
            }
        }
        finally
        {
            restoreTheme();
        }
        Assert.Same(originalAccent, Application.Current.Resources["Accent"]);
        Assert.Equal(originalAccentColor, ((SolidColorBrush)Application.Current.Resources["Accent"]).Color);

        editable.IsEnabled = false;
        Assert.Same(Application.Current.Resources["DisabledControlForeground"], editor.Foreground);
        Assert.False(editor.IsEnabled);
    }

    [Fact]
    public async Task BenchmarkValuePickersAddTypedValuesAndExplainRejectedValues()
    {
        await RunStaAsync(() =>
        {
            var (_, controls, _) = CreatePage();
            Layout(controls.ContextSizes);
            Layout(controls.GpuLayers);
            Layout(controls.Threads);

            var contextEditor = EditableTextBox(controls.ContextSizes.Input);
            Assert.Equal(Visibility.Visible, contextEditor.Visibility);
            Assert.Equal(AutomationProperties.GetName(controls.ContextSizes.Input), AutomationProperties.GetName(contextEditor));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(contextEditor)));
            contextEditor.Text = "12288, 24576";
            Click(controls.ContextSizes.AddButton);
            Assert.Equal(["12288", "24576"], controls.ContextSizes.Values);
            Assert.True(controls.CompareContextSizes.IsChecked);
            Assert.Empty(contextEditor.Text);
            Assert.Equal(Visibility.Collapsed, controls.ContextSizes.ValidationText.Visibility);

            contextEditor.Text = "8k";
            Click(controls.ContextSizes.AddButton);
            Assert.Equal(["12288", "24576"], controls.ContextSizes.Values);
            Assert.Equal(Visibility.Visible, controls.ContextSizes.ValidationText.Visibility);
            Assert.Contains("8k", controls.ContextSizes.ValidationText.Text, StringComparison.Ordinal);
            Assert.Equal("8k", contextEditor.Text);
            contextEditor.Text = "8192";
            Assert.Equal(Visibility.Collapsed, controls.ContextSizes.ValidationText.Visibility);

            var gpuEditor = EditableTextBox(controls.GpuLayers.Input);
            gpuEditor.Text = "-2";
            Click(controls.GpuLayers.AddButton);
            Assert.Empty(controls.GpuLayers.Values);
            Assert.Equal(Visibility.Visible, controls.GpuLayers.ValidationText.Visibility);
            gpuEditor.Text = "40";
            Click(controls.GpuLayers.AddButton);
            Assert.Equal(["40"], controls.GpuLayers.Values);

            var threadEditor = EditableTextBox(controls.Threads.Input);
            threadEditor.Text = "6,0";
            Click(controls.Threads.AddButton);
            Assert.Empty(controls.Threads.Values);
            Assert.Contains("0", controls.Threads.ValidationText.Text, StringComparison.Ordinal);
            threadEditor.Text = "6";
            Click(controls.Threads.AddButton);
            Assert.Equal(["6"], controls.Threads.Values);

            var batchEditor = EditableTextBox(controls.BatchSizes.Input);
            batchEditor.Text = "3072";
            Click(controls.BatchSizes.AddButton);
            var microEditor = EditableTextBox(controls.MicroBatchSizes.Input);
            microEditor.Text = "768";
            Click(controls.MicroBatchSizes.AddButton);
            Assert.Equal(["3072"], controls.BatchSizes.Values);
            Assert.Equal(["768"], controls.MicroBatchSizes.Values);
        });
    }

    [Fact]
    public async Task MatrixChoicesAcceptTypedListsAndShowImportedValues()
    {
        await RunStaAsync(() =>
        {
            var (page, controls, planChanges) = CreatePage();
            Layout(controls.LoadModes);
            Layout(controls.CpuStrict);
            var loadEditor = EditableTextBox(controls.LoadModes);

            controls.LoadModes.SelectedItem = "mlock";
            Assert.Equal("mlock", loadEditor.Text);

            var before = planChanges();
            loadEditor.Text = "mmap,mlock";
            Assert.True(planChanges() > before);
            Assert.Equal(["mmap", "mlock"], BenchmarksPagePlanService.Build(page, "").Options.LoadModes);

            var imported = BenchmarksPagePlanService.Build(page, "") with
            {
                Options = new() { LoadModes = ["dio", "mmap+mlock"], CpuStrict = ["0", "1"], NoHost = ["1"] }
            };
            before = planChanges();
            BenchmarksPagePlanService.Apply(page, imported, []);
            Assert.True(planChanges() > before);
            Assert.Equal("dio,mmap+mlock", loadEditor.Text);
            Assert.Equal("0,1", EditableTextBox(controls.CpuStrict).Text);
            var rebuilt = BenchmarksPagePlanService.Build(page, "");
            Assert.Equal(imported.Options.LoadModes, rebuilt.Options.LoadModes);
            Assert.Equal(imported.Options.CpuStrict, rebuilt.Options.CpuStrict);
            Assert.Equal(imported.Options.NoHost, rebuilt.Options.NoHost);
        });
    }

    private static (BenchmarksPageState Page, BenchmarksPageControls Controls, Func<int> PlanChanges) CreatePage()
    {
        var changes = 0;
        Func<Task> no = () => Task.CompletedTask;
        var actions = new BenchmarksPageActions(no, no, no, no, no, no, no, no, no, no, no, no, no, no, no, no,
            () => 0, _ => Task.CompletedTask, () => { }, _ => { }, () => { }, () => changes++, action => action());
        var controls = BenchmarksPageFactory.Create(new(actions));
        var page = new BenchmarksPageState(() => (true, true));
        page.Apply(controls);
        var now = DateTimeOffset.UtcNow;
        var model = new ModelRecord("m", "Model", "m.gguf", OwnershipKind.External, "{}", now);
        var runtime = new RuntimeRecord("r", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, "llama-server.exe", "{}", now);
        var profile = new NamedModelLaunchProfile("p", "m", "Profile",
            ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(""), "r"), now, true);
        page.SetCatalog([model], [profile], [runtime]);
        return (page, controls, () => changes);
    }

    private static void Layout(FrameworkElement element)
    {
        element.ApplyTemplate();
        element.Measure(new Size(400, 200));
        element.Arrange(new Rect(0, 0, 400, 200));
        element.UpdateLayout();
    }

    private static TextBox EditableTextBox(ComboBox combo)
    {
        combo.ApplyTemplate();
        return Assert.IsType<TextBox>(combo.Template.FindName("PART_EditableTextBox", combo));
    }

    private static ContentPresenter SelectionPresenter(ComboBox combo)
    {
        combo.ApplyTemplate();
        return Assert.IsType<ContentPresenter>(combo.Template.FindName("SelectionPresenter", combo));
    }

    // Restores every application brush the theme service may recolour or replace, so this
    // test leaves the shared test Application exactly as it found it.
    private static Action SnapshotThemeResources()
    {
        var resources = Application.Current.Resources;
        var topLevelKeys = resources.Keys.Cast<object>().ToHashSet();
        var brushes = resources.MergedDictionaries.SelectMany(dictionary => dictionary.Keys.Cast<object>())
            .Concat(topLevelKeys)
            .Distinct()
            .Select(key => (Key: key, Brush: resources[key] as SolidColorBrush))
            .Where(item => item.Brush is not null)
            .Select(item => (item.Key, Brush: item.Brush!, item.Brush!.Color))
            .ToArray();
        return () =>
        {
            foreach (var key in resources.Keys.Cast<object>().Where(key => !topLevelKeys.Contains(key)).ToArray())
                resources.Remove(key);
            foreach (var (key, brush, color) in brushes)
            {
                if (!brush.IsFrozen) brush.Color = color;
                if (topLevelKeys.Contains(key) && !ReferenceEquals(resources[key], brush))
                    resources[key] = brush;
            }
        };
    }

    private static void Click(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
}
