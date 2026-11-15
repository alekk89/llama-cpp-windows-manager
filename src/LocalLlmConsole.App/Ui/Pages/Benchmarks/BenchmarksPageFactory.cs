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
    Button StopButton)
{
    public BenchmarkWorkspaceView? Workspace { get; init; }
}

public sealed record BenchmarkModeItem(BenchmarkExecutionMode Mode, string Name)
{
    public override string ToString() => Name;
}

internal sealed record BenchmarkBooleanItem(bool Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A workload preset choice; <see cref="Id"/> is the stable value and the display text is localized.</summary>
public sealed record BenchmarkPresetItem(string Id)
{
    public override string ToString() => Loc.T($"Benchmarks.Preset.{Id}");
}

internal sealed record BenchmarkFailurePolicyItem(BenchmarkFailurePolicy Policy)
{
    public override string ToString() => Loc.T($"Benchmarks.FailurePolicy.{Policy}");
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
        var name = Text(Loc.T("Benchmarks.DefaultRunName"));
        var preset = Combo(BenchmarkFieldDescriptions.Label("Preset"));
        preset.ItemsSource = new[] { "Short", "Medium", "Long", "Custom" }.Select(id => new BenchmarkPresetItem(id)).ToArray();
        preset.SelectedItem = new BenchmarkPresetItem("Medium");
        var pp = Text("");
        var tg = Text("");
        var (variablesPanel, variableSummary, contextSizes, batches, microBatches, threads, gpuLayers,
            flashAttention, cacheKv, kvOffload, gpuConfigurations, speculativeConfigurations,
            compareContexts, compareBatches, compareMicroBatches, compareThreads, compareGpuLayers,
            compareFlashAttention, compareCacheKv, compareKvOffload,
            compareGpuConfigurations, compareSpeculativeConfigurations) = CreateGuidedBenchmarkMatrix();
        contextSizes.Text = mediumPreset.ContextSize.ToString(CultureInfo.InvariantCulture);
        content.Children.Add(PageSectionFactory.FramedSection(Loc.T("Benchmarks.Section.LaunchSettings"), variablesPanel));
        var pg = Text(mediumPreset.PromptGenerationPairText);
        var depths = Text("0");
        var repetitions = Text(mediumPreset.Repetitions.ToString(CultureInfo.InvariantCulture), 90);
        var warmup = BooleanChoice(BenchmarkFieldDescriptions.Label("Warmup"), Loc.T("Benchmarks.Choice.Enabled"), Loc.T("Benchmarks.Choice.Disabled"));
        var delay = Text("0", 90);
        var concurrencies = Text("1", 90);
        var readyTimeout = Text(mediumPreset.ReadyTimeoutSeconds.ToString(CultureInfo.InvariantCulture), 90);
        var requestTimeout = Text(mediumPreset.RequestTimeoutSeconds.ToString(CultureInfo.InvariantCulture), 90);
        var requireSpeculative = BooleanChoice(BenchmarkFieldDescriptions.Label("SpeculativeProof"), Loc.T("Benchmarks.Choice.Required"), Loc.T("Benchmarks.Choice.NotRequired"));
        var workloadFields = GuidedVariablesGrid();
        AddGuidedField(workloadFields, 0, "RunName", name);
        AddGuidedField(workloadFields, 1, "Preset", preset);
        AddGuidedField(workloadFields, 2, "BenchmarkType", executionMode);
        AddGuidedField(workloadFields, 3, "PromptTargets", pp);
        AddGuidedField(workloadFields, 4, "GenerationTargets", tg);
        AddGuidedField(workloadFields, 5, "PromptGenerationPairs", pg);
        AddGuidedField(workloadFields, 6, "ConcurrentRequests", concurrencies);
        AddGuidedField(workloadFields, 7, "RequestBatches", repetitions);
        AddGuidedField(workloadFields, 8, "DelayBetweenRepetitions", delay);
        AddGuidedField(workloadFields, 9, "ReadyTimeout", readyTimeout);
        AddGuidedField(workloadFields, 10, "RequestTimeout", requestTimeout);
        AddGuidedField(workloadFields, 11, "Warmup", warmup);
        AddGuidedField(workloadFields, 12, "SpeculativeProof", requireSpeculative);
        var workloadPanel = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var workloadDescription = Muted(Loc.T("Benchmarks.WorkloadDescription"));
        workloadDescription.TextWrapping = TextWrapping.Wrap;
        workloadPanel.Children.Add(workloadDescription);
        workloadPanel.Children.Add(workloadFields);
        content.Children.Add(PageSectionFactory.FramedSection(Loc.T("Benchmarks.Section.RequestWorkload"), workloadPanel));

        var cpuMoeLayers = Text("");
        var mainGpus = Text("");
        var devices = Text("");
        var loadModes = MatrixChoice(BenchmarkFieldDescriptions.Label("LoadModes"), "none", "mmap", "mlock", "mmap+mlock", "dio");
        var fitTargets = Text("");
        var fitContexts = Text("");
        var numaModes = Choice(BenchmarkFieldDescriptions.Label("NumaMode"), "", "distribute", "isolate", "numactl");
        var priorities = Choice(BenchmarkFieldDescriptions.Label("Priority"), "", "-1", "0", "1", "2", "3");
        var cpuMasks = Text("");
        var cpuStrict = MatrixChoice(BenchmarkFieldDescriptions.Label("CpuStrict"), "0", "1");
        var pollValues = Text("");
        var embeddings = MatrixChoice(BenchmarkFieldDescriptions.Label("Embeddings"), "0", "1");
        var noOpOffload = MatrixChoice(BenchmarkFieldDescriptions.Label("NoOpOffload"), "0", "1");
        var noHost = MatrixChoice(BenchmarkFieldDescriptions.Label("NoHostBuffer"), "0", "1");
        var tensorOverrides = Text("");
        var cooldown = Text("0", 90);
        var failurePolicy = Combo(BenchmarkFieldDescriptions.Label("FailurePolicy"));
        failurePolicy.ItemsSource = Enum.GetValues<BenchmarkFailurePolicy>().Select(policy => new BenchmarkFailurePolicyItem(policy)).ToArray();
        failurePolicy.SelectedItem = new BenchmarkFailurePolicyItem(BenchmarkFailurePolicy.Stop);
        var repeatEquivalent = Check(Loc.T("Benchmarks.Choice.Enabled"));
        var advanced = GuidedVariablesGrid();
        AddGuidedField(advanced, 0, "ContextDepths", depths);
        AddGuidedField(advanced, 1, "CpuMoeLayers", cpuMoeLayers);
        AddGuidedField(advanced, 2, "MainGpus", mainGpus);
        AddGuidedField(advanced, 3, "Devices", devices);
        AddGuidedField(advanced, 4, "LoadModes", loadModes);
        AddGuidedField(advanced, 5, "FitTargetMib", fitTargets);
        AddGuidedField(advanced, 6, "FitMinimumContexts", fitContexts);
        AddGuidedField(advanced, 7, "NumaMode", numaModes);
        AddGuidedField(advanced, 8, "Priority", priorities);
        AddGuidedField(advanced, 9, "CpuMasks", cpuMasks);
        AddGuidedField(advanced, 10, "CpuStrict", cpuStrict);
        AddGuidedField(advanced, 11, "Poll", pollValues);
        AddGuidedField(advanced, 12, "Embeddings", embeddings);
        AddGuidedField(advanced, 13, "NoOpOffload", noOpOffload);
        AddGuidedField(advanced, 14, "NoHostBuffer", noHost);
        AddGuidedField(advanced, 15, "TensorOverrides", tensorOverrides);
        var directPanel = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var directDescription = Muted(Loc.T("Benchmarks.DirectDescription"));
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

        var additionalArgumentsHelp = BenchmarkFieldDescriptions.Get("AdditionalArguments");
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
        AutomationProperties.SetName(additionalArguments, Loc.T("Benchmarks.AdditionalArgumentsName"));
        AutomationProperties.SetHelpText(additionalArguments, additionalArgumentsHelp);
        var expertRules = GuidedVariablesGrid();
        AddGuidedField(expertRules, 0, "FailurePolicy", failurePolicy);
        AddGuidedField(expertRules, 1, "CooldownBetweenItems", cooldown);
        AddGuidedField(expertRules, 2, "EquivalentProfiles", repeatEquivalent);
        var expertPanel = new StackPanel { Margin = new Thickness(10, 7, 10, 10) };
        var expertDescription = Muted(Loc.T("Benchmarks.ExpertDescription"));
        expertDescription.TextWrapping = TextWrapping.Wrap;
        expertPanel.Children.Add(expertDescription);
        expertPanel.Children.Add(expertRules);
        AddGuidedWideField(expertPanel, "AdditionalArguments", additionalArguments);
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
        buttons.Children.Add(Button(Loc.T("Benchmarks.Action.Validate"), controller.Validate));
        var runButton = Button(Loc.T("Benchmarks.Action.Start"), controller.Run);
        VisualRole.SetButtonRole(runButton, VisualRole.Primary);
        buttons.Children.Add(runButton);
        var stopButton = Button(Loc.T("Benchmarks.Action.Stop"), controller.Cancel);
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
        content.Children.Add(PageSectionFactory.FramedSection(Loc.T("Benchmarks.Section.ReviewAndRun"), runPanel));

        var history = PageSectionFactory.GridFor(
            (Loc.T("Benchmarks.Column.Created"), nameof(BenchmarkRunRow.Created), 1.1),
            (Loc.T("Benchmarks.Column.Status"), nameof(BenchmarkRunRow.Status), .8),
            (Loc.T("Benchmarks.Column.Scope"), nameof(BenchmarkRunRow.Scope), 1.7),
            (Loc.T("Benchmarks.Column.Progress"), nameof(BenchmarkRunRow.Progress), .8),
            (Loc.T("Benchmarks.Column.Name"), nameof(BenchmarkRunRow.Name), 1.8));
        history.MinHeight = 190;
        history.SelectionMode = DataGridSelectionMode.Extended;
        PageSectionFactory.AddButtonColumn(
            history, "", nameof(BenchmarkRunRow.RemoveAction), nameof(BenchmarkRunRow.CanRemove),
            controller.RemoveRun, .65, tooltipBinding: nameof(BenchmarkRunRow.RemoveToolTip), visualRole: VisualRole.Danger, compactContent: "×");
        content.Children.Add(PageSectionFactory.GridSection(Loc.T("Benchmarks.RecentRuns"), history, Loc.T("Benchmarks.RecentRunsHelp")));
        var historyPage = Muted(Loc.T("Benchmarks.HistoryPage", 1));
        var historyPrevious = Button(Loc.T("Benchmarks.Action.Previous"), controller.PreviousHistoryPage);
        var historyNext = Button(Loc.T("Benchmarks.Action.Next"), controller.NextHistoryPage);
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
                (compareContexts.IsChecked == true, contextSizes.Text, "ContextLength"),
                (compareBatches.IsChecked == true, batches.Text, "BatchSize"),
                (compareMicroBatches.IsChecked == true, microBatches.Text, "MicroBatchSize"),
                (compareFlashAttention.IsChecked == true, flashAttention.Text, "FlashAttention"),
                (compareCacheKv.IsChecked == true, cacheKv.Text, "CacheType"),
                (compareGpuLayers.IsChecked == true, gpuLayers.Text, "GpuLayer"),
                (compareThreads.IsChecked == true, threads.Text, "ThreadCount"),
                (compareKvOffload.IsChecked == true, kvOffload.Text, "KvOffload"),
                (compareGpuConfigurations.IsChecked == true,
                    string.Join(',', Enumerable.Range(1, gpuConfigurations.Values.Count).Select(index => $"gpu{index}")),
                    "MultiGpu"),
                (compareSpeculativeConfigurations.IsChecked == true,
                    string.Join(',', Enumerable.Range(1, speculativeConfigurations.Values.Count).Select(index => $"spec{index}")),
                    "Speculative"));
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
            if (!WorkloadPresets.TryGetValue((preset.SelectedItem as BenchmarkPresetItem)?.Id ?? "", out var selected)) return;
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

        var controls = new BenchmarksPageControls(root, model, profile, runtime, scopeProfiles,
            warmup, repeatEquivalent, name, preset, executionMode, pp, tg, compareContexts, contextSizes, pg, depths, repetitions, delay,
            concurrencies, readyTimeout, requestTimeout, requireSpeculative, cooldown,
            failurePolicy, threads, compareThreads, compareBatches, batches, compareMicroBatches, microBatches, compareGpuLayers, gpuLayers, cpuMoeLayers,
            compareFlashAttention, flashAttention, compareCacheKv, cacheKv, compareKvOffload, kvOffload, compareGpuConfigurations, gpuConfigurations,
            compareSpeculativeConfigurations, speculativeConfigurations, mainGpus,
            devices, loadModes, fitTargets, fitContexts, numaModes, priorities, cpuMasks, cpuStrict, pollValues,
            embeddings, noOpOffload, noHost, tensorOverrides, additionalArguments, summary, activeStatus, progress, history,
            historyPage, historyPrevious, historyNext, runButton, stopButton);
        return controls with { Workspace = new BenchmarkWorkspaceView(controls, controller, content) };
    }

}
