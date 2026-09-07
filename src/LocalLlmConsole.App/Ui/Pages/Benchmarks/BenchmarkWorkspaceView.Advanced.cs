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
        System.Windows.Automation.AutomationProperties.SetName(_validationDetails, "Validation details");
        _advanced.Children.Add(Field("Engine", _c.ExecutionMode));
        _advanced.Children.Add(Field("Search settings", _search));
        _advanced.Children.Add(new Expander { Header = Loc.T("Benchmarks.RuntimeOptions"), Content = RuntimeOptions });
        var requests = new StackPanel();
        Add(requests, "Prompt-only tokens · --n-prompt", _c.PromptSizes);
        Add(requests, "Generation-only tokens · --n-gen", _c.GenerationSizes);
        Add(requests, "Warm-up", _c.Warmup);
        Add(requests, "Concurrency", _c.Concurrencies);
        Add(requests, "Seed", Seed);
        Add(requests, "Temperature", Temperature);
        Add(requests, "Delay (seconds)", _c.DelaySeconds);
        Add(requests, "Ready timeout (seconds)", _c.ReadyTimeoutSeconds);
        Add(requests, "Request timeout (seconds)", _c.RequestTimeoutSeconds);
        Add(requests, "Require speculative metrics", _c.RequireSpeculativeMetrics);
        Add(requests, "Independent K cache · --cache-type-k", CacheK);
        Add(requests, "Independent V cache · --cache-type-v", CacheV);
        requests.Children.Add(Label("Leave independent cache lists empty when comparing matched K/V pairs. Multiple values are separated by commas."));
        _advanced.Children.Add(Section("Request and cache settings", requests));
        _profileExpander.Content = ProfileEditor;
        _advanced.Children.Add(_profileExpander);
        Add(_direct, "Depth · --n-depth", _c.Depths);
        Add(_direct, "CPU MoE layers · --n-cpu-moe", _c.CpuMoeLayers);
        Add(_direct, "Main GPU · --main-gpu", _c.MainGpus);
        Add(_direct, "Devices · --device", _c.Devices);
        _c.Devices.ToolTip = Loc.T("Benchmarks.DeviceGroupHelp");
        Add(_direct, "Load mode · --load-mode", _c.LoadModes);
        Add(_direct, "Lazy mode · --lazy-mode", LazyModes);
        LazyModes.ToolTip = Loc.T("Benchmarks.LazyModeHelp");
        Add(_direct, "Fit target MiB · --fit-target", _c.FitTargetsMiB);
        Add(_direct, "Fit context · --fit-ctx", _c.FitContexts);
        Add(_direct, "NUMA · --numa", _c.NumaModes);
        Add(_direct, "Priority · --prio", _c.Priorities);
        Add(_direct, "CPU mask · --cpu-mask", _c.CpuMasks);
        Add(_direct, "Strict CPU · --cpu-strict", _c.CpuStrict);
        Add(_direct, "Poll · --poll", _c.PollValues);
        Add(_direct, "Embeddings · --embeddings", _c.Embeddings);
        Add(_direct, "No operation offload · --no-op-offload", _c.NoOpOffload);
        Add(_direct, "No host buffer · --no-host", _c.NoHost);
        Add(_direct, "Tensor buffers · --override-tensor", _c.TensorOverrides);
        _direct.Children.Add(Label("Additional llama-bench arguments: one argument token per line. Runtime support is checked by Validate."));
        Add(_direct, "Additional arguments", _c.AdditionalArguments);
        _advanced.Children.Add(Section("Direct llama-bench settings", _direct));
        var rules = new StackPanel();
        Add(rules, "Failure policy", _c.FailurePolicy);
        Add(rules, "Cooldown (seconds)", _c.CooldownSeconds);
        Add(rules, "Repeat equivalent profiles", _c.RepeatEquivalentProfiles);
        _advanced.Children.Add(Section("Run behavior", rules));
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
            throw new InvalidOperationException("This flag is already present. Edit its value in Additional arguments.");
        existing.AddRange(tokens);
        BenchmarkCommandBuilder.ValidateAdditionalArguments(existing);
        _c.AdditionalArguments.Text = string.Join(Environment.NewLine, existing);
    }

    private void BuildResults()
    {
        var actions = new WrapPanel();
        actions.Children.Add(Action("View results", _controller.Details));
        actions.Children.Add(Action("Compare selected runs", _controller.Compare));
        actions.Children.Add(Action("Clone", _controller.Clone));
        actions.Children.Add(Action("Export", _controller.Export));
        actions.Children.Add(Action("Resume", _controller.Resume));
        actions.Children.Add(Action("Log", _controller.OpenLog));
        actions.Children.Add(Action("Refresh", _controller.Refresh));
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
