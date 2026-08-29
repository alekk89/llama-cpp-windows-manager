using System.Windows.Controls;
using LocalLlmConsole.Models;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using ProgressBar = System.Windows.Controls.ProgressBar;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed record BenchmarkSelectionItem(string Id, string Name)
{
    public override string ToString() => Name;
}

public sealed record BenchmarkScopeRow(
    string ModelId,
    string Model,
    string ProfileId,
    string Profile,
    string RuntimeId,
    string Runtime,
    string Environment)
{
    public string RemoveAction => "Remove";
    public bool CanRemove => true;
    public string RemoveToolTip => $"Remove {Profile} with {Runtime}";
}

public sealed record BenchmarkRunRow(
    string RunId,
    string Created,
    string Status,
    string Scope,
    string Progress,
    string Message)
{
    public string RemoveAction => "Delete";
    public bool CanRemove => Status is not ("Queued" or "Running" or "Paused");
    public string RemoveToolTip => CanRemove ? "Delete this benchmark run and its results" : "Stop this run before deleting it";
}

public sealed partial class BenchmarksPageState
{
    private readonly Func<(bool StopActiveSessions, bool PreventSystemSleep)>? _runPolicies;
    private bool _stopActiveSessions;
    private bool _preventSystemSleep = true;

    public BenchmarksPageState(Func<(bool StopActiveSessions, bool PreventSystemSleep)>? runPolicies = null)
        => _runPolicies = runPolicies;

    public ScrollViewer? Root { get; private set; }
    public ComboBox? Model { get; private set; }
    public ComboBox? Profile { get; private set; }
    public ComboBox? Runtime { get; private set; }
    public DataGrid? ScopeProfiles { get; private set; }
    public bool StopActiveSessions => _runPolicies?.Invoke().StopActiveSessions ?? _stopActiveSessions;
    public bool PreventSystemSleep => _runPolicies?.Invoke().PreventSystemSleep ?? _preventSystemSleep;
    public ComboBox? Warmup { get; private set; }
    public CheckBox? RepeatEquivalentProfiles { get; private set; }
    public TextBox? Name { get; private set; }
    public ComboBox? Preset { get; private set; }
    public ComboBox? ExecutionMode { get; private set; }
    public TextBox? PromptSizes { get; private set; }
    public TextBox? GenerationSizes { get; private set; }
    public CheckBox? CompareContextSizes { get; private set; }
    public BenchmarkValuePicker? ContextSizes { get; private set; }
    public TextBox? PromptGenerationPairs { get; private set; }
    public TextBox? Depths { get; private set; }
    public TextBox? Repetitions { get; private set; }
    public TextBox? DelaySeconds { get; private set; }
    public TextBox? Concurrencies { get; private set; }
    public TextBox? ReadyTimeoutSeconds { get; private set; }
    public TextBox? RequestTimeoutSeconds { get; private set; }
    public ComboBox? RequireSpeculativeMetrics { get; private set; }
    public TextBox? CooldownSeconds { get; private set; }
    public ComboBox? FailurePolicy { get; private set; }
    public BenchmarkValuePicker? Threads { get; private set; }
    public CheckBox? CompareThreads { get; private set; }
    public CheckBox? CompareBatchSizes { get; private set; }
    public BenchmarkValuePicker? BatchSizes { get; private set; }
    public CheckBox? CompareMicroBatchSizes { get; private set; }
    public BenchmarkValuePicker? MicroBatchSizes { get; private set; }
    public CheckBox? CompareGpuLayers { get; private set; }
    public BenchmarkValuePicker? GpuLayers { get; private set; }
    public TextBox? CpuMoeLayers { get; private set; }
    public CheckBox? CompareFlashAttention { get; private set; }
    public BenchmarkValuePicker? FlashAttention { get; private set; }
    public CheckBox? CompareCacheTypesK { get; private set; }
    public BenchmarkValuePicker? CacheTypesK { get; private set; }
    public CheckBox? CompareKvOffload { get; private set; }
    public BenchmarkValuePicker? KvOffload { get; private set; }
    public CheckBox? CompareGpuConfigurations { get; private set; }
    public BenchmarkGpuConfigurationPicker? GpuConfigurations { get; private set; }
    public CheckBox? CompareSpeculativeConfigurations { get; private set; }
    public BenchmarkSpeculativeConfigurationPicker? SpeculativeConfigurations { get; private set; }
    public TextBox? MainGpus { get; private set; }
    public TextBox? Devices { get; private set; }
    public ComboBox? LoadModes { get; private set; }
    public TextBox? FitTargetsMiB { get; private set; }
    public TextBox? FitContexts { get; private set; }
    public ComboBox? NumaModes { get; private set; }
    public ComboBox? Priorities { get; private set; }
    public TextBox? CpuMasks { get; private set; }
    public ComboBox? CpuStrict { get; private set; }
    public TextBox? PollValues { get; private set; }
    public ComboBox? Embeddings { get; private set; }
    public ComboBox? NoOpOffload { get; private set; }
    public ComboBox? NoHost { get; private set; }
    public TextBox? TensorOverrides { get; private set; }
    public TextBox? AdditionalArguments { get; private set; }
    public TextBlock? Summary { get; private set; }
    public TextBlock? ActiveStatus { get; private set; }
    public ProgressBar? Progress { get; private set; }
    public DataGrid? History { get; private set; }
    public TextBlock? HistoryPage { get; private set; }
    public Button? HistoryPrevious { get; private set; }
    public Button? HistoryNext { get; private set; }
    public Button? RunButton { get; private set; }
    public Button? StopButton { get; private set; }
    public string ActiveRunId { get; set; } = "";
    public bool IsRunActive { get; set; }
    public int HistoryOffset { get; set; }
    public int HistoryPageSize { get; } = 25;
    private IReadOnlyList<ModelRecord> _models = [];
    private IReadOnlyList<NamedModelLaunchProfile> _profiles = [];
    private IReadOnlyList<RuntimeRecord> _runtimes = [];
    private readonly List<BenchmarkScopeRow> _scopeRows = [];

    public string SelectedRunId => (History?.SelectedItem as BenchmarkRunRow)?.RunId ?? ActiveRunId;
    public IReadOnlyList<string> SelectedRunIds => History?.SelectedItems.Cast<BenchmarkRunRow>().Select(row => row.RunId).ToArray() ?? [];
    public IReadOnlyList<BenchmarkScopeRow> ScopeRows => _scopeRows;

    public void Apply(BenchmarksPageControls controls)
    {
        Root = controls.Root;
        Model = controls.Model;
        Profile = controls.Profile;
        Runtime = controls.Runtime;
        ScopeProfiles = controls.ScopeProfiles;
        Warmup = controls.Warmup;
        RepeatEquivalentProfiles = controls.RepeatEquivalentProfiles;
        Name = controls.Name;
        Preset = controls.Preset;
        ExecutionMode = controls.ExecutionMode;
        PromptSizes = controls.PromptSizes;
        GenerationSizes = controls.GenerationSizes;
        CompareContextSizes = controls.CompareContextSizes;
        ContextSizes = controls.ContextSizes;
        PromptGenerationPairs = controls.PromptGenerationPairs;
        Depths = controls.Depths;
        Repetitions = controls.Repetitions;
        DelaySeconds = controls.DelaySeconds;
        Concurrencies = controls.Concurrencies;
        ReadyTimeoutSeconds = controls.ReadyTimeoutSeconds;
        RequestTimeoutSeconds = controls.RequestTimeoutSeconds;
        RequireSpeculativeMetrics = controls.RequireSpeculativeMetrics;
        CooldownSeconds = controls.CooldownSeconds;
        FailurePolicy = controls.FailurePolicy;
        Threads = controls.Threads;
        CompareThreads = controls.CompareThreads;
        CompareBatchSizes = controls.CompareBatchSizes;
        BatchSizes = controls.BatchSizes;
        CompareMicroBatchSizes = controls.CompareMicroBatchSizes;
        MicroBatchSizes = controls.MicroBatchSizes;
        CompareGpuLayers = controls.CompareGpuLayers;
        GpuLayers = controls.GpuLayers;
        CpuMoeLayers = controls.CpuMoeLayers;
        CompareFlashAttention = controls.CompareFlashAttention;
        FlashAttention = controls.FlashAttention;
        CompareCacheTypesK = controls.CompareCacheTypesK;
        CacheTypesK = controls.CacheTypesK;
        CompareKvOffload = controls.CompareKvOffload;
        KvOffload = controls.KvOffload;
        CompareGpuConfigurations = controls.CompareGpuConfigurations;
        GpuConfigurations = controls.GpuConfigurations;
        CompareSpeculativeConfigurations = controls.CompareSpeculativeConfigurations;
        SpeculativeConfigurations = controls.SpeculativeConfigurations;
        MainGpus = controls.MainGpus;
        Devices = controls.Devices;
        LoadModes = controls.LoadModes;
        FitTargetsMiB = controls.FitTargetsMiB;
        FitContexts = controls.FitContexts;
        NumaModes = controls.NumaModes;
        Priorities = controls.Priorities;
        CpuMasks = controls.CpuMasks;
        CpuStrict = controls.CpuStrict;
        PollValues = controls.PollValues;
        Embeddings = controls.Embeddings;
        NoOpOffload = controls.NoOpOffload;
        NoHost = controls.NoHost;
        TensorOverrides = controls.TensorOverrides;
        AdditionalArguments = controls.AdditionalArguments;
        Summary = controls.Summary;
        ActiveStatus = controls.ActiveStatus;
        Progress = controls.Progress;
        History = controls.History;
        HistoryPage = controls.HistoryPage;
        HistoryPrevious = controls.HistoryPrevious;
        HistoryNext = controls.HistoryNext;
        RunButton = controls.RunButton;
        StopButton = controls.StopButton;
    }

    public void SetCatalog(
        IReadOnlyList<ModelRecord> models,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        IReadOnlyList<RuntimeRecord> runtimes)
    {
        _models = models;
        _profiles = profiles;
        _runtimes = runtimes;
        SetItems(Model, models.Select(model => new BenchmarkSelectionItem(model.Id, model.Name)).ToArray());
        SetItems(Runtime, [new BenchmarkSelectionItem("", "Profile runtime"), .. runtimes.Select(runtime => new BenchmarkSelectionItem(runtime.Id, $"{runtime.Name} · {runtime.Mode}/{runtime.Backend}"))]);
        SetProfileItems(profiles);
        RefreshScopeRows();
    }

    public void SetRunPolicies(bool stopActiveSessions, bool preventSystemSleep)
    {
        _stopActiveSessions = stopActiveSessions;
        _preventSystemSleep = preventSystemSleep;
    }

    public void SetHistory(IReadOnlyList<BenchmarkRunRow> rows)
    {
        if (History is null) return;
        var selected = SelectedRunId;
        History.ItemsSource = rows;
        History.SelectedItem = rows.FirstOrDefault(row => row.RunId.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? rows.FirstOrDefault();
    }

    public void ReleaseView()
    {
        Root = null;
        Model = null;
        Profile = null;
        Runtime = null;
        ScopeProfiles = null;
        Warmup = null;
        RepeatEquivalentProfiles = null;
        Name = null;
        Preset = null;
        ExecutionMode = null;
        PromptSizes = null;
        GenerationSizes = null;
        CompareContextSizes = null;
        ContextSizes = null;
        PromptGenerationPairs = null;
        Depths = null;
        Repetitions = null;
        DelaySeconds = null;
        Concurrencies = null;
        ReadyTimeoutSeconds = null;
        RequestTimeoutSeconds = null;
        RequireSpeculativeMetrics = null;
        CooldownSeconds = null;
        FailurePolicy = null;
        Threads = null;
        CompareThreads = null;
        CompareBatchSizes = null;
        BatchSizes = null;
        CompareMicroBatchSizes = null;
        MicroBatchSizes = null;
        CompareGpuLayers = null;
        GpuLayers = null;
        CpuMoeLayers = null;
        CompareFlashAttention = null;
        FlashAttention = null;
        CompareCacheTypesK = null;
        CacheTypesK = null;
        CompareKvOffload = null;
        KvOffload = null;
        CompareGpuConfigurations = null;
        GpuConfigurations = null;
        CompareSpeculativeConfigurations = null;
        SpeculativeConfigurations = null;
        MainGpus = null;
        Devices = null;
        LoadModes = null;
        FitTargetsMiB = null;
        FitContexts = null;
        NumaModes = null;
        Priorities = null;
        CpuMasks = null;
        CpuStrict = null;
        PollValues = null;
        Embeddings = null;
        NoOpOffload = null;
        NoHost = null;
        TensorOverrides = null;
        AdditionalArguments = null;
        Summary = null;
        ActiveStatus = null;
        Progress = null;
        History = null;
        HistoryPage = null;
        HistoryPrevious = null;
        HistoryNext = null;
        RunButton = null;
        StopButton = null;
        _models = [];
        _profiles = [];
        _runtimes = [];
        _scopeRows.Clear();
    }

}
