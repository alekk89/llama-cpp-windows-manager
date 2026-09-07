using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Button = System.Windows.Controls.Button;
using TextBox = System.Windows.Controls.TextBox;
using ComboBox = System.Windows.Controls.ComboBox;
using Panel = System.Windows.Controls.Panel;
using Orientation = System.Windows.Controls.Orientation;
using TabControl = System.Windows.Controls.TabControl;

namespace LocalLlmConsole;

public sealed partial class BenchmarkWorkspaceView
{
    private readonly BenchmarksPageControls _c;
    private readonly BenchmarksPageController _controller;
    private readonly StackPanel _setup = new();
    private readonly StackPanel _scopeSelection = new();
    private readonly StackPanel _comparison = new();
    private readonly StackPanel _advanced = new();
    private readonly StackPanel _results = new();
    private readonly TextBlock _inherited = Label("");
    private readonly TextBlock _counts = Label("");
    private readonly DataGrid _preview = PageSectionFactory.GridFor(
        ("Profile", nameof(BenchmarkPlanPreviewRow.Profile), 1.3),
        ("Runtime", nameof(BenchmarkPlanPreviewRow.Runtime), 1.3),
        ("Context", nameof(BenchmarkPlanPreviewRow.Context), .8),
        ("Threads", nameof(BenchmarkPlanPreviewRow.Threads), .8),
        ("Cache K / V", nameof(BenchmarkPlanPreviewRow.Cache), 1),
        ("Measurements", nameof(BenchmarkPlanPreviewRow.Measurements), .8));
    public int SetupDepth { get => _depth.SelectedIndex; set => _depth.SelectedIndex = value; }
    private readonly ComboBox _depth = new() { ItemsSource = new[] { "Quick test", "Compare settings", "Advanced" }, SelectedIndex = 0, Width = 174, Height = 30 };
    private readonly Button _newTab;
    private readonly Button _resultsTab;
    private FrameworkElement? _footer;
    public DockPanel Root { get; } = new();
    public TextBox Seed { get; } = Input("42");
    public TextBox Temperature { get; } = Input("0");
    public TextBox CacheK { get; } = Input("");
    public TextBox CacheV { get; } = Input("");
    public TextBox LazyModes { get; } = Input("");
    public BenchmarkProfileEditor ProfileEditor { get; }
    public BenchmarkRuntimeOptionPicker RuntimeOptions { get; }
    public BenchmarkResultsView Results { get; } = new();

    public BenchmarkWorkspaceView(BenchmarksPageControls controls, BenchmarksPageController controller, StackPanel content)
    {
        _c = controls;
        _controller = controller;
        ProfileEditor = new BenchmarkProfileEditor(controller.NotifyPlanChanged);
        RuntimeOptions = new BenchmarkRuntimeOptionPicker(AddRuntimeArguments);
        content.Children.Clear();
        var tabs = new WrapPanel();
        _newTab = Action(Loc.T("Benchmarks.NewBenchmark"), (_, _) => SelectTab(false));
        _resultsTab = Action(Loc.T("Benchmarks.Results"), (_, _) => SelectTab(true));
        tabs.Children.Add(_newTab);
        tabs.Children.Add(_resultsTab);
        content.Children.Add(tabs);
        content.Children.Add(_setup);
        content.Children.Add(_results);
        Root.SetResourceReference(DockPanel.BackgroundProperty, "AppBack");
        _setup.Margin = _results.Margin = new Thickness(0, 12, 0, 0);
        var modeRow = new WrapPanel();
        _depth.Margin = new Thickness(0, 0, 8, 6);
        modeRow.Children.Add(_depth);
        modeRow.Children.Add(Action("Import plan", controller.ImportPlan));
        modeRow.Children.Add(Action("Export plan", controller.ExportPlan));
        _setup.Children.Add(modeRow);
        AutomationProperties.SetName(_depth, "Setup depth");

        var selectors = new StackPanel();
        selectors.Children.Add(Field("Model", controls.Model));
        selectors.Children.Add(Field("Profile", controls.Profile));
        selectors.Children.Add(Field("Runtime", controls.Runtime));
        selectors.Children.Add(_inherited);
        var scopeActions = new WrapPanel();
        scopeActions.Children.Add(Action("Add selected profile / runtime", controller.AddProfile));
        scopeActions.Children.Add(Action("Clear", controller.ClearProfiles));
        _scopeSelection.Children.Add(scopeActions);
        controls.ScopeProfiles.MinHeight = 70;
        controls.ScopeProfiles.MaxHeight = 180;
        _scopeSelection.Children.Add(Move(controls.ScopeProfiles));
        selectors.Children.Add(_scopeSelection);
        _setup.Children.Add(Section(Loc.T("Benchmarks.ProfileToTest"), selectors));
        _comparison.Children.Add(Label(Loc.T("Benchmarks.MatrixHelp")));
        var chips = new WrapPanel();
        foreach (var (name, picker) in new (string, BenchmarkValuePicker)[] {
            ("K/V cache", controls.CacheTypesK), ("Flash Attention", controls.FlashAttention),
            ("Threads", controls.Threads), ("Context size", controls.ContextSizes),
            ("Batch size", controls.BatchSizes), ("Micro-batch", controls.MicroBatchSizes),
            ("GPU layers", controls.GpuLayers), ("KV offload", controls.KvOffload) })
        {
            picker.UseSharedSelectionHost(chips, name);
            _comparison.Children.Add(Field(name, picker));
        }
        controls.GpuConfigurations.UseSharedSelectionHost(chips);
        controls.SpeculativeConfigurations.UseSharedSelectionHost(chips);
        _comparison.Children.Add(Field("Multi-GPU", controls.GpuConfigurations));
        _comparison.Children.Add(Field("Speculative", controls.SpeculativeConfigurations));
        _comparison.Children.Add(chips);
        _preview.MinHeight = 80;
        _preview.MaxHeight = 220;
        _comparison.Children.Add(new Expander { Header = Loc.T("Benchmarks.PreviewLaunches"), Content = _preview });
        _setup.Children.Add(Section(Loc.T("Benchmarks.CompareSettings"), _comparison));

        var workload = new StackPanel();
        controls.Preset.ItemsSource = new[] { "Quick check", "Standard", "Short", "Medium", "Long", "Custom" };
        controls.Preset.SelectionChanged += (_, _) => ApplySimplePreset();
        workload.Children.Add(Field("Workload", controls.Preset));
        workload.Children.Add(Field("Prompt / generation tokens", controls.PromptGenerationPairs));
        workload.Children.Add(Field("Repetitions", controls.Repetitions));
        workload.Children.Add(Label(Loc.T("Benchmarks.QuickHelp")));
        _setup.Children.Add(Section(Loc.T("Benchmarks.Workload"), workload));
        BuildAdvanced();
        _setup.Children.Add(_advanced);

        var run = new StackPanel();
        run.Children.Add(Field("Run name", controls.Name));
        run.Children.Add(_counts);
        run.Children.Add(Move(controls.Summary));
        controls.Summary.Text = "";
        controls.Summary.MaxHeight = 36;
        controls.Summary.Margin = new Thickness(0);
        var runActions = new WrapPanel();
        runActions.Children.Add(Move(controls.RunButton));
        runActions.Children.Add(Action("Validate", controller.Validate));
        runActions.Children.Add(Move(controls.StopButton));
        runActions.Children.Add(Action("Pause", controller.Pause));
        foreach (FrameworkElement button in runActions.Children)
            button.Margin = new Thickness(0, 0, 8, 6);
        run.Children.Add(runActions);
        run.Children.Add(Move(controls.ActiveStatus));
        run.Children.Add(Move(controls.Progress));
        controls.ActiveStatus.Visibility = controls.Progress.Visibility = Visibility.Collapsed;
        controls.ActiveStatus.MaxHeight = 48;
        var footerBorder = new Border { Child = run, Padding = new Thickness(10), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(7) };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        footerBorder.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        _footer = footerBorder;
        _footer.Margin = new Thickness(20, 0, 20, 12);
        DockPanel.SetDock(_footer, Dock.Bottom);
        Root.Children.Add(_footer);
        Root.Children.Add(controls.Root);
        BuildResults();
        _depth.SelectionChanged += (_, _) => UpdateDepth();
        controls.ExecutionMode.SelectionChanged += (_, _) => UpdateEngine();
        foreach (var box in new[] { Seed, Temperature, CacheK, CacheV, LazyModes }) box.TextChanged += controller.PlanTextChanged;
        controls.Preset.SelectedItem = "Quick check";
        UpdateDepth();
        UpdateEngine();
        SelectTab(false);
    }

    private void ApplySimplePreset()
    {
        if (_c.Preset.SelectedItem is not ("Quick check" or "Standard")) return;
        var quick = _c.Preset.SelectedItem.Equals("Quick check");
        _c.PromptSizes.Text = _c.GenerationSizes.Text = "";
        _c.PromptGenerationPairs.Text = quick ? "512/128" : "2048/256";
        _c.Repetitions.Text = quick ? "3" : "5";
        _c.ContextSizes.Text = "";
        _c.Concurrencies.Text = "1";
        _c.Depths.Text = "0";
        _c.Warmup.SelectedIndex = 0;
    }

    public void ShowImportedPlan(BenchmarkPlan plan)
    {
        Seed.Text = plan.Serving.Seed.ToString(CultureInfo.InvariantCulture);
        Temperature.Text = plan.Serving.Temperature.ToString(CultureInfo.InvariantCulture);
        CacheK.Text = string.Join(',', plan.Options.CacheTypesK);
        CacheV.Text = string.Join(',', plan.Options.CacheTypesV);
        LazyModes.Text = string.Join(',', plan.Options.LazyModes);
        ProfileEditor.Apply(plan.Serving.ProfileOverrides);
        _depth.SelectedIndex = 2;
        SelectTab(false);
    }

    public void UpdateSelection(ModelLaunchSettings? settings, ModelRecord? model)
    {
        _inherited.Text = settings is null ? Loc.T("Benchmarks.ChooseProfile") :
            $"{(model is null ? "" : model.Name + " · ")}context {settings.ContextSize:N0} · batch {settings.BatchSize:N0} · " +
            $"GPU layers {settings.GpuLayers} · cache {settings.CacheTypeK}/{settings.CacheTypeV} · threads {settings.Threads}";
        ProfileEditor.SetInherited(settings);
    }

    public void ShowPreview(BenchmarkPlanPreview preview)
    {
        _counts.Text = preview.IsValid
            ? $"{preview.WorkItems.Count:N0} launch(es) · {preview.ExpectedResultRows:N0} measurements · {preview.TimedRepetitions:N0} timed repetitions"
            : string.Join(Environment.NewLine, preview.Errors.Take(3));
        _counts.MaxHeight = 40;
        _counts.ToolTip = _counts.Text;
        _preview.ItemsSource = preview.WorkItems.Select(item => new BenchmarkPlanPreviewRow(
            string.Join(", ", item.ProfileNames), item.RuntimeName,
            item.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? item.LaunchSettings?.ContextSize.ToString("N0") ?? "—" : "By workload",
            item.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? item.LaunchSettings?.Threads.ToString() ?? "—" : string.Join(',', item.Options.Threads),
            item.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? $"{item.LaunchSettings?.CacheTypeK}/{item.LaunchSettings?.CacheTypeV}" : $"{string.Join(',', item.Options.CacheTypesK)} / {string.Join(',', item.Options.CacheTypesV)}",
            item.ExpectedResultRows)).ToArray();
    }

    private void SelectTab(bool results)
    {
        _setup.Visibility = results ? Visibility.Collapsed : Visibility.Visible;
        _results.Visibility = results ? Visibility.Visible : Visibility.Collapsed;
        if (_footer is not null) _footer.Visibility = results ? Visibility.Collapsed : Visibility.Visible;
        VisualRole.SetButtonRole(_newTab, results ? "" : VisualRole.Primary);
        VisualRole.SetButtonRole(_resultsTab, results ? VisualRole.Primary : "");
        _c.Root.ScrollToTop();
    }

    private void UpdateDepth()
    {
        SetSectionVisibility(_comparison, _depth.SelectedIndex > 0);
        _scopeSelection.Visibility = _depth.SelectedIndex > 0 ? Visibility.Visible : Visibility.Collapsed;
        _advanced.Visibility = _depth.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowResults(string title, IReadOnlyList<StoredBenchmarkResult> results)
    {
        Results.SetResults(title, results);
        SelectTab(true);
    }

    internal static TextBlock Label(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 5, 0, 7) };
    internal static TextBox Input(string text) => new() { Text = text, Height = 28, MinWidth = 60, Padding = new Thickness(7, 2, 7, 2) };
    internal static Button Action(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(10, 4, 10, 4) };
        button.Click += handler;
        return button;
    }
    internal static FrameworkElement Section(string title, FrameworkElement child)
    {
        child.Margin = new Thickness(10, 6, 10, 8);
        return PageSectionFactory.FramedSection(title, child);
    }

    private static void SetSectionVisibility(FrameworkElement child, bool visible)
    {
        if (child.Parent is FrameworkElement { Parent: UIElement section })
            section.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
    }

    internal static FrameworkElement Field(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(174) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var caption = Label(label);
        caption.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(caption);
        Move(control);
        control.Width = double.NaN;
        control.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        Grid.SetColumn(control, 1);
        Grid.SetRow(control, 0);
        Grid.SetColumnSpan(control, 1);
        Grid.SetRowSpan(control, 1);
        grid.Children.Add(control);
        AutomationProperties.SetName(control, label);
        return grid;
    }

    private static T Move<T>(T control) where T : FrameworkElement
    {
        switch (control.Parent)
        {
            case Panel panel: panel.Children.Remove(control); break;
            case ContentControl content: content.Content = null; break;
            case Decorator decorator: decorator.Child = null; break;
        }
        return control;
    }
}

public sealed record BenchmarkPlanPreviewRow(string Profile, string Runtime, string Context, string Threads, string Cache, int Measurements);
