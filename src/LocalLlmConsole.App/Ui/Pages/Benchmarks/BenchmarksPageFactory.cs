using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace LocalLlmConsole;

public sealed record BenchmarksPageControls(
    ScrollViewer Root,
    ComboBox Model,
    ComboBox Profile,
    ComboBox Runtime,
    DataGrid ScopeProfiles,
    ComboBox Warmup,
    CheckBox RepeatEquivalentProfiles,
    TextBox Name,
    ComboBox Preset,
    ComboBox ExecutionMode,
    TextBox PromptSizes,
    TextBox GenerationSizes,
    CheckBox CompareContextSizes,
    BenchmarkValuePicker ContextSizes,
    TextBox PromptGenerationPairs,
    TextBox Depths,
    TextBox Repetitions,
    TextBox DelaySeconds,
    TextBox Concurrencies,
    TextBox ReadyTimeoutSeconds,
    TextBox RequestTimeoutSeconds,
    ComboBox RequireSpeculativeMetrics,
    TextBox CooldownSeconds,
    ComboBox FailurePolicy,
    BenchmarkValuePicker Threads,
    CheckBox CompareThreads,
    CheckBox CompareBatchSizes,
    BenchmarkValuePicker BatchSizes,
    CheckBox CompareMicroBatchSizes,
    BenchmarkValuePicker MicroBatchSizes,
    CheckBox CompareGpuLayers,
    BenchmarkValuePicker GpuLayers,
    TextBox CpuMoeLayers,
    CheckBox CompareFlashAttention,
    BenchmarkValuePicker FlashAttention,
    CheckBox CompareCacheTypesK,
    BenchmarkValuePicker CacheTypesK,
    CheckBox CompareKvOffload,
    BenchmarkValuePicker KvOffload,
    CheckBox CompareGpuConfigurations,
    BenchmarkGpuConfigurationPicker GpuConfigurations,
    CheckBox CompareSpeculativeConfigurations,
    BenchmarkSpeculativeConfigurationPicker SpeculativeConfigurations,
    TextBox MainGpus,
    TextBox Devices,
    ComboBox LoadModes,
    TextBox FitTargetsMiB,
    TextBox FitContexts,
    ComboBox NumaModes,
    ComboBox Priorities,
    TextBox CpuMasks,
    ComboBox CpuStrict,
    TextBox PollValues,
    ComboBox Embeddings,
    ComboBox NoOpOffload,
    ComboBox NoHost,
    TextBox TensorOverrides,
    TextBox AdditionalArguments,
    TextBlock Summary,
    TextBlock ActiveStatus,
    ProgressBar Progress,
    DataGrid History,
    TextBlock HistoryPage,
    Button HistoryPrevious,
    Button HistoryNext,
    Button RunButton,
    Button StopButton);

public sealed record BenchmarkModeItem(BenchmarkExecutionMode Mode, string Name)
{
    public override string ToString() => Name;
}

internal sealed record BenchmarkBooleanItem(bool Value, string Name)
{
    public override string ToString() => Name;
}

public static partial class BenchmarksPageFactory
{
    private static readonly IReadOnlyDictionary<string, BenchmarkWorkloadPreset> WorkloadPresets =
        BenchmarkWorkloadPresetCatalog.All.ToDictionary(preset => preset.Name, StringComparer.OrdinalIgnoreCase);

    public static BenchmarksPageControls Create(BenchmarksPageController controller)
    {
        var content = new StackPanel
        {
            Margin = new Thickness(20, 16, 20, 24),
            HorizontalAlignment = WpfHorizontalAlignment.Stretch
        };
        var (scope, model, profile, runtime, executionMode, scopeProfiles, resizeSelector) = CreateScopeControls(controller);
        content.Children.Add(scope);

        var mediumPreset = WorkloadPresets["Medium"];
        var name = Text("Benchmark run");
        var preset = Combo("Preset");
        preset.ItemsSource = new[] { "Short", "Medium", "Long", "Custom" };
        preset.SelectedItem = "Medium";
        var pp = Text("");
        var tg = Text("");
        var (variablesPanel, variableSummary, contextSizes, batches, microBatches, threads, gpuLayers,
            flashAttention, cacheKv, kvOffload, gpuConfigurations, speculativeConfigurations,
            compareContexts, compareBatches, compareMicroBatches, compareThreads, compareGpuLayers,
            compareFlashAttention, compareCacheKv, compareKvOffload,
            compareGpuConfigurations, compareSpeculativeConfigurations) = CreateGuidedBenchmarkMatrix();
        contextSizes.Text = mediumPreset.ContextSize.ToString(CultureInfo.InvariantCulture);
        content.Children.Add(PageSectionFactory.FramedSection("1. Launch settings to test", variablesPanel));
        var pg = Text(mediumPreset.PromptGenerationPairText);
        var depths = Text("0");
        var repetitions = Text(mediumPreset.Repetitions.ToString(CultureInfo.InvariantCulture), 90);
        var warmup = BooleanChoice("Warm-up", "Enabled", "Disabled");
        var delay = Text("0", 90);
        var concurrencies = Text("1", 90);
        var readyTimeout = Text(mediumPreset.ReadyTimeoutSeconds.ToString(CultureInfo.InvariantCulture), 90);
        var requestTimeout = Text(mediumPreset.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture), 90);
        var requireSpeculative = BooleanChoice("Speculative proof", "Required", "Not required");
        var workloadFields = GuidedVariablesGrid();
        AddGuidedField(workloadFields, 0, "Run name", name);
        AddGuidedField(workloadFields, 1, "Preset", preset);
        AddGuidedField(workloadFields, 2, "Benchmark type", executionMode);
        AddGuidedField(workloadFields, 3, "Prompt targets", pp);
        AddGuidedField(workloadFields, 4, "Generation targets", tg);
        AddGuidedField(workloadFields, 5, "Prompt / generation pairs", pg);
        AddGuidedField(workloadFields, 6, "Concurrent requests", concurrencies, "Profile serving only; for example 1,2,4.");
        AddGuidedField(workloadFields, 7, "Request batches", repetitions);
        AddGuidedField(workloadFields, 8, "Delay between repetitions", delay);
        AddGuidedField(workloadFields, 9, "Ready timeout", readyTimeout, "Seconds.");
        AddGuidedField(workloadFields, 10, "Request timeout", requestTimeout, "Seconds.");
        AddGuidedField(workloadFields, 11, "Warm-up", warmup);
        AddGuidedField(workloadFields, 12, "Speculative proof", requireSpeculative);
        var workloadPanel = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var workloadDescription = Muted("Choose the request sizes and execution pattern applied to every launch configuration. Presets provide a starting point; individual fields remain editable.");
        workloadDescription.TextWrapping = TextWrapping.Wrap;
        workloadPanel.Children.Add(workloadDescription);
        workloadPanel.Children.Add(workloadFields);
        content.Children.Add(PageSectionFactory.FramedSection("2. Choose the request workload", workloadPanel));

        var cpuMoeLayers = Text("");
        var mainGpus = Text("");
        var devices = Text("");
        var loadModes = MatrixChoice("Load modes", "none", "mmap", "mlock", "mmap+mlock", "dio");
        var fitTargets = Text("");
        var fitContexts = Text("");
        var numaModes = Choice("NUMA mode", "", "distribute", "isolate", "numactl");
        var priorities = Choice("Priority", "", "-1", "0", "1", "2", "3");
        var cpuMasks = Text("");
        var cpuStrict = MatrixChoice("CPU strict", "0", "1");
        var pollValues = Text("");
        var embeddings = MatrixChoice("Embeddings", "0", "1");
        var noOpOffload = MatrixChoice("No-op offload", "0", "1");
        var noHost = MatrixChoice("No host buffer", "0", "1");
        var tensorOverrides = Text("");
        var cooldown = Text("0", 90);
        var failurePolicy = Combo("Failure policy");
        failurePolicy.ItemsSource = Enum.GetValues<BenchmarkFailurePolicy>();
        failurePolicy.SelectedItem = BenchmarkFailurePolicy.Stop;
        var repeatEquivalent = Check("Enabled");
        var advanced = GuidedVariablesGrid();
        AddGuidedField(advanced, 0, "Context depths", depths);
        AddGuidedField(advanced, 1, "CPU MoE layers", cpuMoeLayers);
        AddGuidedField(advanced, 2, "Main GPUs", mainGpus);
        AddGuidedField(advanced, 3, "Devices", devices);
        AddGuidedField(advanced, 4, "Load modes", loadModes);
        AddGuidedField(advanced, 5, "Fit target MiB", fitTargets);
        AddGuidedField(advanced, 6, "Fit minimum contexts", fitContexts);
        AddGuidedField(advanced, 7, "NUMA mode", numaModes);
        AddGuidedField(advanced, 8, "Priority", priorities);
        AddGuidedField(advanced, 9, "CPU masks", cpuMasks);
        AddGuidedField(advanced, 10, "CPU strict", cpuStrict);
        AddGuidedField(advanced, 11, "Poll", pollValues);
        AddGuidedField(advanced, 12, "Embeddings", embeddings);
        AddGuidedField(advanced, 13, "No-op offload", noOpOffload);
        AddGuidedField(advanced, 14, "No host buffer", noHost);
        AddGuidedField(advanced, 15, "Tensor overrides", tensorOverrides);
        var directPanel = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var directDescription = Muted("Configure low-level llama-bench-only matrix dimensions. Blank fields keep the inherited or upstream default; multiple values expand into separate direct benchmark combinations.");
        directDescription.TextWrapping = TextWrapping.Wrap;
        directPanel.Children.Add(directDescription);
        directPanel.Children.Add(advanced);
        var directMatrix = new Expander
        {
            Header = Loc.T("Benchmarks.DirectSettings"),
            IsExpanded = false,
            Content = directPanel,
            Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed
        };
        content.Children.Add(directMatrix);

        var additionalArgumentsHelp = BenchmarkFieldDescriptions.Get("Additional arguments");
        var additionalArguments = new TextBox
        {
            AcceptsReturn = true,
            MinHeight = 100,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            Margin = new Thickness(0, 0, 4, 0),
            ToolTip = additionalArgumentsHelp
        };
        AutomationProperties.SetName(additionalArguments, "Additional llama-bench arguments");
        AutomationProperties.SetHelpText(additionalArguments, additionalArgumentsHelp);
        var expertRules = GuidedVariablesGrid();
        AddGuidedField(expertRules, 0, "Failure policy", failurePolicy);
        AddGuidedField(expertRules, 1, "Cooldown between items", cooldown);
        AddGuidedField(expertRules, 2, "Equivalent profiles", repeatEquivalent);
        var expertPanel = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var expertDescription = Muted("Control how expanded benchmark items are executed and handled. Additional arguments apply only to Direct llama-bench; safety-owned model, output, progress, repetition, delay, and offline options cannot be overridden.");
        expertDescription.TextWrapping = TextWrapping.Wrap;
        expertPanel.Children.Add(expertDescription);
        expertPanel.Children.Add(expertRules);
        AddGuidedWideField(expertPanel, "Additional arguments", additionalArguments);
        content.Children.Add(new Expander
        {
            Header = Loc.T("Benchmarks.AutomationOptions"),
            IsExpanded = false,
            Content = expertPanel,
            Margin = new Thickness(0, 0, 0, 10)
        });

        var summary = Muted(Loc.T("PageSubtitle.Benchmarks"));
        summary.TextWrapping = TextWrapping.Wrap;
        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 4) };
        buttons.Children.Add(Button("Validate", controller.Validate));
        var runButton = Button("Start", controller.Run);
        VisualRole.SetButtonRole(runButton, VisualRole.Primary);
        buttons.Children.Add(runButton);
        var stopButton = Button("Stop", controller.Cancel);
        stopButton.IsEnabled = false;
        VisualRole.SetButtonRole(stopButton, VisualRole.Danger);
        buttons.Children.Add(stopButton);
        var activeStatus = Muted(Loc.T("Benchmarks.NoActiveRun"));
        activeStatus.Margin = new Thickness(0, 8, 0, 0);
        var progress = new ProgressBar { Height = 8, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 8, 0, 4) };
        var runPanel = new StackPanel { Margin = new Thickness(10) };
        runPanel.Children.Add(summary);
        runPanel.Children.Add(buttons);
        runPanel.Children.Add(activeStatus);
        runPanel.Children.Add(progress);
        content.Children.Add(PageSectionFactory.FramedSection("3. Review and run", runPanel));

        var history = PageSectionFactory.GridFor(
            ("Created", nameof(BenchmarkRunRow.Created), 1.1),
            ("Status", nameof(BenchmarkRunRow.Status), .8),
            ("Scope", nameof(BenchmarkRunRow.Scope), 1.7),
            ("Progress", nameof(BenchmarkRunRow.Progress), .8),
            ("Message", nameof(BenchmarkRunRow.Message), 1.8));
        history.MinHeight = 190;
        history.SelectionMode = DataGridSelectionMode.Extended;
        PageSectionFactory.AddButtonColumn(
            history, "", nameof(BenchmarkRunRow.RemoveAction), nameof(BenchmarkRunRow.CanRemove),
            controller.RemoveRun, .65, tooltipBinding: nameof(BenchmarkRunRow.RemoveToolTip), visualRole: VisualRole.Danger);
        content.Children.Add(PageSectionFactory.GridSection("Recent runs", history, "Double-click one run to view its report, or select exactly two and double-click to compare. Right-click for more actions."));
        var historyPage = Muted("Page 1");
        var historyPrevious = Button("Previous", controller.PreviousHistoryPage);
        var historyNext = Button("Next", controller.NextHistoryPage);
        historyPrevious.IsEnabled = false;
        content.Children.Add(CreateHistoryFooter(controller, history, historyPage, historyPrevious, historyNext));

        var root = new ScrollViewer
        {
            Content = content,
            HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        void FitContentToWidth(double viewportWidth)
        {
            if (!double.IsFinite(viewportWidth) || viewportWidth <= 0) return;
            var availableWidth = Math.Max(0, viewportWidth - content.Margin.Left - content.Margin.Right);
            if (double.IsNaN(content.Width) || Math.Abs(content.Width - availableWidth) > 0.5)
                content.Width = availableWidth;
            resizeSelector(availableWidth);
        }
        void FitContentToViewport()
            => FitContentToWidth(root.ViewportWidth > 0 ? root.ViewportWidth : root.ActualWidth);
        root.Loaded += (_, _) => FitContentToViewport();
        root.SizeChanged += (_, args) => FitContentToWidth(args.NewSize.Width);
        root.ScrollChanged += (_, args) =>
        {
            if (Math.Abs(args.ViewportWidthChange) > 0.5)
                FitContentToViewport();
        };
        foreach (var box in new[] { name, pp, tg, pg, depths, concurrencies, repetitions, delay, readyTimeout, requestTimeout, cooldown,
                     cpuMoeLayers, mainGpus, devices, fitTargets, fitContexts, cpuMasks, pollValues, tensorOverrides, additionalArguments })
            box.TextChanged += controller.PlanTextChanged;

        void UpdateVariableUi()
        {
            SyncComparison(compareContexts, contextSizes);
            SyncComparison(compareBatches, batches);
            SyncComparison(compareMicroBatches, microBatches);
            SyncComparison(compareThreads, threads);
            SyncComparison(compareGpuLayers, gpuLayers);
            SyncComparison(compareFlashAttention, flashAttention);
            SyncComparison(compareCacheKv, cacheKv);
            SyncComparison(compareKvOffload, kvOffload);
            compareGpuConfigurations.IsChecked = gpuConfigurations.Values.Count > 0;
            compareSpeculativeConfigurations.IsChecked = speculativeConfigurations.Values.Count > 0;
            variableSummary.Text = VariableCombinationSummary(
                (compareContexts.IsChecked == true, contextSizes.Text, "context length", "context lengths"),
                (compareBatches.IsChecked == true, batches.Text, "batch size", "batch sizes"),
                (compareMicroBatches.IsChecked == true, microBatches.Text, "micro-batch size", "micro-batch sizes"),
                (compareFlashAttention.IsChecked == true, flashAttention.Text, "Flash Attention value", "Flash Attention values"),
                (compareCacheKv.IsChecked == true, cacheKv.Text, "K/V cache type", "K/V cache types"),
                (compareGpuLayers.IsChecked == true, gpuLayers.Text, "GPU-layer value", "GPU-layer values"),
                (compareThreads.IsChecked == true, threads.Text, "thread count", "thread counts"),
                (compareKvOffload.IsChecked == true, kvOffload.Text, "KV-offload value", "KV-offload values"),
                (compareGpuConfigurations.IsChecked == true,
                    string.Join(',', Enumerable.Range(1, gpuConfigurations.Values.Count).Select(index => $"gpu{index}")),
                    "multi-GPU configuration", "multi-GPU configurations"),
                (compareSpeculativeConfigurations.IsChecked == true,
                    string.Join(',', Enumerable.Range(1, speculativeConfigurations.Values.Count).Select(index => $"spec{index}")),
                    "speculative configuration", "speculative configurations"));
        }

        var guidedPickers = new[] { contextSizes, batches, microBatches, gpuLayers, threads, flashAttention, cacheKv,
            kvOffload };
        foreach (var picker in guidedPickers)
            picker.Changed += (_, _) => { UpdateVariableUi(); controller.NotifyPlanChanged(); };
        gpuConfigurations.Changed += (_, _) => { UpdateVariableUi(); controller.NotifyPlanChanged(); };
        speculativeConfigurations.Changed += (_, _) => { UpdateVariableUi(); controller.NotifyPlanChanged(); };
        repeatEquivalent.Click += controller.PlanChanged;
        var optionCombos = new[] { loadModes, numaModes, priorities, cpuStrict, embeddings, noOpOffload, noHost };
        foreach (var combo in new[] { profile, runtime, failurePolicy, executionMode, warmup, requireSpeculative }.Concat(optionCombos))
            combo.SelectionChanged += controller.PlanSelectionChanged;
        foreach (var combo in optionCombos.Where(combo => combo.IsEditable))
            combo.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(controller.PlanTextChanged));
        preset.SelectionChanged += (_, _) =>
        {
            if (!WorkloadPresets.TryGetValue(preset.SelectedItem?.ToString() ?? "", out var selected)) return;
            pp.Text = "";
            tg.Text = "";
            contextSizes.Text = selected.ContextSize.ToString(CultureInfo.InvariantCulture);
            batches.Text = "";
            pg.Text = selected.PromptGenerationPairText;
            depths.Text = "0";
            repetitions.Text = selected.Repetitions.ToString(CultureInfo.InvariantCulture);
            delay.Text = "0";
            concurrencies.Text = "1";
            readyTimeout.Text = selected.ReadyTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            requestTimeout.Text = selected.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture);
            warmup.SelectedIndex = 0;
            UpdateVariableUi();
        };

        void UpdateModeUi()
        {
            var direct = executionMode.SelectedItem is BenchmarkModeItem { Mode: BenchmarkExecutionMode.LlamaBench };
            directMatrix.Visibility = direct ? Visibility.Visible : Visibility.Collapsed;
            additionalArguments.IsEnabled = direct;
            repeatEquivalent.IsEnabled = direct;
            compareContexts.IsEnabled = !direct;
            contextSizes.IsEnabled = !direct;
            requireSpeculative.IsEnabled = !direct;
            speculativeConfigurations.IsEnabled = !direct;
            UpdateVariableUi();
        }

        static void SyncComparison(CheckBox comparison, BenchmarkValuePicker picker)
            => comparison.IsChecked = CountValues(picker.Text) > 0;

        executionMode.SelectionChanged += (_, _) => UpdateModeUi();
        UpdateModeUi();

        return new BenchmarksPageControls(root, model, profile, runtime, scopeProfiles,
            warmup, repeatEquivalent, name, preset, executionMode, pp, tg, compareContexts, contextSizes, pg, depths, repetitions, delay,
            concurrencies, readyTimeout, requestTimeout, requireSpeculative, cooldown,
            failurePolicy, threads, compareThreads, compareBatches, batches, compareMicroBatches, microBatches, compareGpuLayers, gpuLayers, cpuMoeLayers,
            compareFlashAttention, flashAttention, compareCacheKv, cacheKv, compareKvOffload, kvOffload, compareGpuConfigurations, gpuConfigurations,
            compareSpeculativeConfigurations, speculativeConfigurations, mainGpus,
            devices, loadModes, fitTargets, fitContexts, numaModes, priorities, cpuMasks, cpuStrict, pollValues,
            embeddings, noOpOffload, noHost, tensorOverrides, additionalArguments, summary, activeStatus, progress, history,
            historyPage, historyPrevious, historyNext, runButton, stopButton);
    }

}
