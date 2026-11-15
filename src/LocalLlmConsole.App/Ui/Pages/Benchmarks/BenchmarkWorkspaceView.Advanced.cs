using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using TextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed partial class BenchmarkWorkspaceView
{
    private readonly StackPanel _direct = new();
    private readonly List<(string Search, FrameworkElement Row)> _advancedRows = [];
    private readonly TextBox _search = Input("");
    private readonly Expander _profileExpander = new() { Header = Loc.T("Benchmarks.TemporaryProfileSettings") };
    private readonly TextBox _validationDetails = new()
    {
        IsReadOnly = true,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MaxHeight = 240,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private void BuildAdvanced()
    {
        System.Windows.Automation.AutomationProperties.SetName(_validationDetails, Loc.T("Benchmarks.ValidationDetails"));
        _advanced.Children.Add(Field(Loc.T("Benchmarks.Label.Engine"), _c.ExecutionMode));
        _advanced.Children.Add(Field(Loc.T("Benchmarks.Label.SearchSettings"), _search));
        _advanced.Children.Add(new Expander { Header = Loc.T("Benchmarks.RuntimeOptions"), Content = RuntimeOptions });
        var requests = new StackPanel();
        Add(requests, $"{Loc.T("Benchmarks.Label.PromptOnlyTokens")} · --n-prompt", _c.PromptSizes);
        Add(requests, $"{Loc.T("Benchmarks.Label.GenerationOnlyTokens")} · --n-gen", _c.GenerationSizes);
        Add(requests, BenchmarkFieldDescriptions.Label("Warmup"), _c.Warmup);
        Add(requests, Loc.T("Benchmarks.Label.Concurrency"), _c.Concurrencies);
        Add(requests, Loc.T("Launch.Field.Seed"), Seed);
        Add(requests, Loc.T("Launch.Field.Temperature"), Temperature);
        Add(requests, Loc.T("Benchmarks.Label.DelaySeconds"), _c.DelaySeconds);
        Add(requests, Loc.T("Benchmarks.Label.ReadyTimeoutSeconds"), _c.ReadyTimeoutSeconds);
        Add(requests, Loc.T("Benchmarks.Label.RequestTimeoutSeconds"), _c.RequestTimeoutSeconds);
        Add(requests, Loc.T("Benchmarks.Label.RequireSpeculativeMetrics"), _c.RequireSpeculativeMetrics);
        Add(requests, $"{Loc.T("Benchmarks.Label.IndependentKCache")} · --cache-type-k", CacheK);
        Add(requests, $"{Loc.T("Benchmarks.Label.IndependentVCache")} · --cache-type-v", CacheV);
        requests.Children.Add(Label(Loc.T("Benchmarks.IndependentCacheHelp")));
        _advanced.Children.Add(Section(Loc.T("Benchmarks.Section.RequestAndCache"), requests));
        _profileExpander.Content = ProfileEditor;
        _advanced.Children.Add(_profileExpander);
        Add(_direct, $"{Loc.T("Benchmarks.Label.Depth")} · --n-depth", _c.Depths);
        Add(_direct, $"{BenchmarkFieldDescriptions.Label("CpuMoeLayers")} · --n-cpu-moe", _c.CpuMoeLayers);
        Add(_direct, $"{Loc.T("Benchmarks.Label.MainGpu")} · --main-gpu", _c.MainGpus);
        Add(_direct, $"{BenchmarkFieldDescriptions.Label("Devices")} · --device", _c.Devices);
        _c.Devices.ToolTip = Loc.T("Benchmarks.DeviceGroupHelp");
        Add(_direct, $"{Loc.T("Benchmarks.Label.LoadMode")} · --load-mode", _c.LoadModes);
        Add(_direct, $"{Loc.T("Benchmarks.Label.LazyMode")} · --lazy-mode", LazyModes);
        LazyModes.ToolTip = Loc.T("Benchmarks.LazyModeHelp");
        Add(_direct, $"{BenchmarkFieldDescriptions.Label("FitTargetMib")} · --fit-target", _c.FitTargetsMiB);
        Add(_direct, $"{Loc.T("Benchmarks.Label.FitContext")} · --fit-ctx", _c.FitContexts);
        Add(_direct, "NUMA · --numa", _c.NumaModes);
        Add(_direct, $"{BenchmarkFieldDescriptions.Label("Priority")} · --prio", _c.Priorities);
        Add(_direct, $"{Loc.T("Benchmarks.Label.CpuMask")} · --cpu-mask", _c.CpuMasks);
        Add(_direct, $"{Loc.T("Benchmarks.Label.StrictCpu")} · --cpu-strict", _c.CpuStrict);
        Add(_direct, $"{BenchmarkFieldDescriptions.Label("Poll")} · --poll", _c.PollValues);
        Add(_direct, $"{BenchmarkFieldDescriptions.Label("Embeddings")} · --embeddings", _c.Embeddings);
        Add(_direct, $"{Loc.T("Benchmarks.Label.NoOperationOffload")} · --no-op-offload", _c.NoOpOffload);
        Add(_direct, $"{BenchmarkFieldDescriptions.Label("NoHostBuffer")} · --no-host", _c.NoHost);
        Add(_direct, $"{Loc.T("Benchmarks.Label.TensorBuffers")} · --override-tensor", _c.TensorOverrides);
        _direct.Children.Add(Label(Loc.T("Benchmarks.AdditionalArgumentsHelp")));
        Add(_direct, BenchmarkFieldDescriptions.Label("AdditionalArguments"), _c.AdditionalArguments);
        _advanced.Children.Add(Section(Loc.T("Benchmarks.Section.DirectSettings"), _direct));
        var rules = new StackPanel();
        Add(rules, BenchmarkFieldDescriptions.Label("FailurePolicy"), _c.FailurePolicy);
        Add(rules, Loc.T("Benchmarks.Label.CooldownSeconds"), _c.CooldownSeconds);
        Add(rules, Loc.T("Benchmarks.Label.RepeatEquivalentProfiles"), _c.RepeatEquivalentProfiles);
        _advanced.Children.Add(Section(Loc.T("Benchmarks.Section.RunBehavior"), rules));
        _advanced.Children.Add(new Expander { Header = Loc.T("Benchmarks.ValidationDetails"), Content = _validationDetails });
        _search.TextChanged += (_, _) =>
        {
            var query = _search.Text.Trim();
            foreach (var (text, row) in _advancedRows)
                row.Visibility = text.Contains(query, StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
            ProfileEditor.Filter(query);
            if (query.Length > 0) _profileExpander.IsExpanded = true;
            RuntimeOptions.Filter(query);
        };
    }

    private void Add(StackPanel panel, string name, FrameworkElement control)
    {
        var row = Field(name, control);
        _advancedRows.Add((name, row));
        panel.Children.Add(row);
    }

    private void UpdateEngine()
    {
        var direct = _c.ExecutionMode.SelectedItem is BenchmarkModeItem { Mode: BenchmarkExecutionMode.LlamaBench };
        SetSectionVisibility(_direct, direct);
        _profileExpander.Visibility = direct ? Visibility.Collapsed : Visibility.Visible;
        Seed.IsEnabled = Temperature.IsEnabled = !direct;
        _c.Concurrencies.IsEnabled = !direct;
        RuntimeOptions.SetOptions([]);
    }

    public void SetValidationDetails(string details) => _validationDetails.Text = details;

    private void AddRuntimeArguments(IReadOnlyList<string> tokens)
    {
        if (_c.ExecutionMode.SelectedItem is not BenchmarkModeItem { Mode: BenchmarkExecutionMode.LlamaBench })
        { ProfileEditor.AddCustomArguments(tokens); return; }
        var existing = _c.AdditionalArguments.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
        if (existing.Contains(tokens[0], StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(Loc.T("Benchmarks.FlagPresentInArguments"));
        existing.AddRange(tokens);
        BenchmarkCommandBuilder.ValidateAdditionalArguments(existing);
        _c.AdditionalArguments.Text = string.Join(Environment.NewLine, existing);
    }

    private void BuildResults()
    {
        var actions = new WrapPanel();
        actions.Children.Add(Action(Loc.T("Benchmarks.Action.ViewResults"), _controller.Details));
        actions.Children.Add(Action(Loc.T("Benchmarks.Action.CompareSelectedRuns"), _controller.Compare));
        actions.Children.Add(Action(Loc.T("Benchmarks.Action.Clone"), _controller.Clone));
        actions.Children.Add(Action(Loc.T("Benchmarks.Action.Export"), _controller.Export));
        actions.Children.Add(Action(Loc.T("Benchmarks.Action.Resume"), _controller.Resume));
        actions.Children.Add(Action(Loc.T("Common.LogButton"), _controller.OpenLog));
        actions.Children.Add(Action(Loc.T("Benchmarks.Action.Refresh"), _controller.Refresh));
        _results.Children.Add(actions);
        _c.History.MinHeight = 130;
        _c.History.MaxHeight = 220;
        _results.Children.Add(Move(_c.History));
        var pages = new WrapPanel { Margin = new Thickness(0, 6, 0, 10) };
        pages.Children.Add(Move(_c.HistoryPrevious));
        pages.Children.Add(Move(_c.HistoryPage));
        pages.Children.Add(Move(_c.HistoryNext));
        _results.Children.Add(pages);
        _results.Children.Add(Results);
    }
}
