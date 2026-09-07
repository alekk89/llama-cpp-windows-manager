using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class BenchmarkResultsView : StackPanel
{
    private readonly TextBlock _title = BenchmarkWorkspaceView.Label(Loc.T("Benchmarks.NoResults"));
    private readonly ComboBox _baseline = new() { Height = 30, DisplayMemberPath = nameof(BenchmarkResultTableRow.Name) };
    public DataGrid Table { get; }
    private readonly TextBlock _details = BenchmarkWorkspaceView.Label("");
    private readonly TextBlock _selection = BenchmarkWorkspaceView.Label("");
    private readonly WrapPanel _speeds = new() { Margin = new Thickness(0, 0, 0, 8), Visibility = Visibility.Collapsed };
    private readonly TextBlock _generationSpeed = new();
    private readonly TextBlock _promptSpeed = new();
    private readonly TextBlock _totalSpeed = new();
    private readonly TextBlock _totalLabel = new();
    private readonly Border _totalCard;
    private readonly TextBox _raw = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
    private IReadOnlyList<BenchmarkResultTableRow> _rows = [];

    public BenchmarkResultsView()
    {
        System.Windows.Automation.AutomationProperties.SetName(_raw, "Raw measurement JSON");
        System.Windows.Automation.AutomationProperties.SetName(_baseline, "Baseline configuration");
        Children.Add(_title);
        Children.Add(_selection);
        _speeds.Children.Add(SpeedCard(new TextBlock { Text = Loc.T("Lifetime.Insight.GenerationRate") }, _generationSpeed,
            "Output tokens generated per second, using runtime generation timings when available."));
        _speeds.Children.Add(SpeedCard(new TextBlock { Text = Loc.T("Lifetime.Insight.PromptRate") }, _promptSpeed,
            "Input tokens processed per second. Missing runtime timings show —."));
        _totalCard = SpeedCard(_totalLabel, _totalSpeed,
            "Serving: output tokens across all requests divided by elapsed time. Direct llama-bench: combined prompt and generation speed.");
        _speeds.Children.Add(_totalCard);
        Children.Add(_speeds);
        Children.Add(BenchmarkWorkspaceView.Field("Baseline", _baseline));
        Table = PageSectionFactory.GridFor(
            ("Profile / configuration", nameof(BenchmarkResultTableRow.Name), 2.5),
            ("Runtime", nameof(BenchmarkResultTableRow.Runtime), 1.4),
            ("Generation tok/s", nameof(BenchmarkResultTableRow.Decode), 1),
            ("Prompt tok/s", nameof(BenchmarkResultTableRow.Prompt), .9),
            ("Total tok/s", nameof(BenchmarkResultTableRow.Total), .9),
            ("Change", nameof(BenchmarkResultTableRow.Change), .9));
        foreach (var column in Table.Columns.Skip(2).Take(3).Cast<DataGridTextColumn>())
        {
            var style = new Style(typeof(TextBlock), column.ElementStyle);
            style.Setters.Add(new Setter(TextBlock.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Right));
            column.ElementStyle = style;
            column.Header = new TextBlock { Text = column.Header.ToString()!.Replace(" tok/s", "\ntokens/s") };
            column.Width = DataGridLength.SizeToHeader;
        }
        Table.MinHeight = 140;
        Table.MaxHeight = 420;
        Table.SelectionMode = DataGridSelectionMode.Single;
        Children.Add(Table);
        Children.Add(BenchmarkWorkspaceView.Label(Loc.T("Benchmarks.ResultMetricHelp")));
        Children.Add(new Expander { Header = Loc.T("Benchmarks.SelectedConfiguration"), Content = _details });
        Children.Add(new Expander { Header = Loc.T("Benchmarks.MeasurementDetails"), Content = _raw });
        Table.SelectionChanged += (_, _) => ShowSelected();
        _baseline.SelectionChanged += (_, _) => UpdateBaseline();
    }

    public void SetResults(string title, IReadOnlyList<StoredBenchmarkResult> results)
    {
        _title.Text = title + $" · {results.Count:N0} measurements";
        _rows = results.Select(row => new BenchmarkResultTableRow(row)).ToArray();
        Table.ItemsSource = _rows;
        Table.Columns[4].Visibility = _rows.Any(row => row.HasTotalRate) ? Visibility.Visible : Visibility.Collapsed;
        _baseline.ItemsSource = _rows.Where(row => !row.Stored.IsPartialAttempt).ToArray();
        _baseline.SelectedIndex = 0;
        Table.SelectedIndex = 0;
        if (_rows.Count == 0) { _details.Text = Loc.T("Benchmarks.NoResults"); _raw.Text = ""; }
        ShowSelected();
    }

    private void UpdateBaseline()
    {
        var baseline = _baseline.SelectedItem as BenchmarkResultTableRow;
        foreach (var row in _rows) row.CompareTo(baseline);
        Table.Items.Refresh();
    }

    private void ShowSelected()
    {
        if (Table.SelectedItem is not BenchmarkResultTableRow row)
        {
            _speeds.Visibility = Visibility.Collapsed;
            _selection.Text = "";
            return;
        }
        _speeds.Visibility = Visibility.Visible;
        _selection.Text = $"{Loc.T("Benchmarks.SelectedConfiguration")}: {row.Name}";
        _generationSpeed.Text = row.Decode;
        _promptSpeed.Text = row.Prompt;
        _totalSpeed.Text = row.Total;
        _totalLabel.Text = row.Stored.Result.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? "Request throughput" : "Combined speed";
        _totalCard.Visibility = row.HasTotalRate ? Visibility.Visible : Visibility.Collapsed;
        var r = row.Stored.Result;
        _details.Text = $"{r.ManagerModelName} · {r.ModelFilename}\n{r.ProfileName} · {row.Runtime} · {r.ExecutionMode}\n" +
            $"Workload {r.PromptTokens:N0}/{r.GenerationTokens:N0} · context {r.ContextSize:N0} · depth {r.Depth:N0} · concurrency {r.Concurrency}\n" +
            $"Batch {r.BatchSize:N0} · micro-batch {r.MicroBatchSize:N0} · threads {r.Threads} · GPU layers {r.GpuLayers} · " +
            $"cache {r.CacheTypeK}/{r.CacheTypeV} · flash {r.FlashAttention}\n" +
            $"Devices {r.Devices} · split {r.SplitMode} {r.TensorSplit} · load {r.LoadMode} · speculative {r.SpeculativeType}\n" +
            $"Latency {row.Latency} s · GPU {row.Memory} GiB · build {r.BuildCommit} · {r.OperatingEnvironment}\n" +
            BenchmarkMemoryReportService.Label(r) +
            (row.Stored.IsPartialAttempt ? "\nPartial attempt: excluded from baseline comparisons." : "");
        using var document = JsonDocument.Parse(r.RawJson);
        _raw.Text = JsonSerializer.Serialize(document.RootElement, new JsonSerializerOptions { WriteIndented = true });
    }

    private static Border SpeedCard(TextBlock label, TextBlock value, string help)
    {
        var content = new StackPanel();
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        content.Children.Add(label);
        value.FontSize = 28;
        value.FontWeight = FontWeights.SemiBold;
        value.Margin = new Thickness(0, 3, 0, 0);
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
        content.Children.Add(value);
        var unit = new TextBlock { Text = Loc.T("Benchmarks.TokensPerSecondUnit") };
        unit.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        content.Children.Add(unit);
        var card = new Border
        {
            Child = content,
            MinWidth = 174,
            Padding = new Thickness(14, 10, 14, 10),
            Margin = new Thickness(0, 0, 8, 6),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            ToolTip = help
        };
        card.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        card.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        return card;
    }
}

public sealed class BenchmarkResultTableRow
{
    public StoredBenchmarkResult Stored { get; }
    private BenchmarkParsedResult R => Stored.Result;
    public string Name => $"{(string.IsNullOrWhiteSpace(R.ProfileName) ? R.ManagerModelName : R.ProfileName)} · {R.PromptTokens}/{R.GenerationTokens} · {R.CacheTypeK}/{R.CacheTypeV} · t{R.Threads}" + (Stored.IsPartialAttempt ? " · partial" : "");
    public string Runtime => string.IsNullOrWhiteSpace(R.ManagerRuntimeName) ? $"{R.Backends} {R.BuildCommit}" : R.ManagerRuntimeName;
    public double DecodeRate => R.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? R.AverageGenerationTokensPerSecond :
        R.Classification == BenchmarkResultClassification.TokenGeneration ? R.AverageTokensPerSecond : 0;
    public double PromptRate => R.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? R.AveragePromptTokensPerSecond :
        R.Classification == BenchmarkResultClassification.PromptProcessing ? R.AverageTokensPerSecond : 0;
    private double ComparisonRate => R.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? DecodeRate : R.AverageTokensPerSecond;
    public string Prompt => Rate(PromptRate);
    public string Decode => Rate(DecodeRate);
    public bool HasTotalRate => R.ExecutionMode == BenchmarkExecutionMode.ProfileServing || R.Classification == BenchmarkResultClassification.PromptAndGeneration;
    public string Total => HasTotalRate ? Rate(R.AverageTokensPerSecond) : "—";
    public string Change { get; private set; } = "—";
    public string Latency => Rate(R.ExecutionMode == BenchmarkExecutionMode.ProfileServing ? R.AverageLatencyMilliseconds / 1000 : R.AverageNanoseconds / 1e9);
    public string Memory => R.GpuMemoryPeaks is { Count: > 0 } peaks && peaks.Any(peak => peak.PeakDedicatedUsedMiB.HasValue)
        ? (peaks.Sum(peak => peak.PeakDedicatedUsedMiB ?? 0) / 1024d).ToString("N2", CultureInfo.CurrentCulture) : "—";
    public BenchmarkResultTableRow(StoredBenchmarkResult stored) => Stored = stored;
    public override string ToString() => Name;
    public void CompareTo(BenchmarkResultTableRow? baseline)
    {
        Change = "—";
        if (baseline is null || Stored.IsPartialAttempt || baseline.ComparisonRate <= 0 || ComparisonRate <= 0) return;
        var b = baseline.R;
        if (R.ModelFilename != b.ModelFilename || R.ExecutionMode != b.ExecutionMode || R.Classification != b.Classification
            || R.PromptTokens != b.PromptTokens || R.GenerationTokens != b.GenerationTokens || R.Depth != b.Depth || R.Concurrency != b.Concurrency) return;
        Change = ReferenceEquals(this, baseline) ? "Baseline" : ((ComparisonRate / baseline.ComparisonRate - 1) * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%";
        if (R.EnvironmentSignature != b.EnvironmentSignature) Change += "*";
    }
    private static string Rate(double rate) => rate > 0 ? rate.ToString("N2", CultureInfo.CurrentCulture) : "—";
}
