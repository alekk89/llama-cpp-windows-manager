using System.Windows;
using LocalLlmConsole.Models;

namespace LocalLlmConsole;

public partial class MainWindow
{
    private BenchmarksPageState? _benchmarksPage;
    private BenchmarksPageController? _benchmarksController;
    private EventHandler<BenchmarkRunSnapshot>? _benchmarkProgressHandler;
    private IReadOnlyList<NamedModelLaunchProfile> _benchmarkProfiles = [];

    private void ShowBenchmarks()
    {
        SetPage("Benchmarks", Loc.T("PageSubtitle.Benchmarks"));
        Require(_stateStore);
        _benchmarksPage = new BenchmarksPageState(() => (_settings.BenchmarkStopActiveSessions, _settings.BenchmarkPreventSystemSleep));
        _benchmarksController = new BenchmarksPageController(new BenchmarksPageActions(
            RefreshBenchmarksAsync,
            ValidateBenchmarkPlanAsync,
            StartBenchmarkPlanAsync,
            PauseBenchmarkAsync,
            ResumeBenchmarkAsync,
            CancelBenchmarkAsync,
            ShowBenchmarkDetailsAsync,
            ExportBenchmarkAsync,
            CompareBenchmarksAsync,
            CloneBenchmarkPlanAsync,
            ImportBenchmarkPlanAsync,
            ExportBenchmarkPlanAsync,
            OpenBenchmarkLogAsync,
            PreviousBenchmarkHistoryPageAsync,
            NextBenchmarkHistoryPageAsync,
            BenchmarkSelectionChangedAsync,
            () => _benchmarksPage?.SelectedRunIds.Count ?? 0,
            row => DeleteBenchmarkAsync(row.RunId),
            () => _benchmarksPage?.AddSelectedProfile(), row => _benchmarksPage?.RemoveProfile(row),
            () => _benchmarksPage?.ClearScopeProfiles(),
            InvalidateBenchmarkPlan,
            RunEventAsync));
        var controls = BenchmarksPageFactory.Create(_benchmarksController);
        _benchmarksPage.Apply(controls);
        PageHost.Content = controls.Root;
        SubscribeBenchmarkProgress();
        RunBackground(RefreshBenchmarksAsync, "Benchmark refresh failed");
    }

    private async Task RefreshBenchmarksAsync()
    {
        Require(_stateStore);
        var modelsTask = _stateStore!.ListModelsAsync();
        var profilesTask = _stateStore.ListNamedModelLaunchProfilesAsync();
        var runtimesTask = _stateStore.ListRuntimesAsync();
        var historyOffset = _benchmarksPage?.HistoryOffset ?? 0;
        var historyPageSize = _benchmarksPage?.HistoryPageSize ?? 25;
        var runsTask = AppServices.Benchmarks.Value.ListAsync(historyPageSize, offset: historyOffset);
        await Task.WhenAll(modelsTask, profilesTask, runtimesTask, runsTask);
        if (_benchmarksPage is null) return;
        _benchmarkProfiles = await profilesTask;
        _benchmarksPage.SetCatalog(await modelsTask, _benchmarkProfiles, await runtimesTask);
        ApplyBenchmarkRuns(await runsTask);
    }

    private Task BenchmarkSelectionChangedAsync()
    {
        _benchmarksPage?.SetProfileItems(_benchmarkProfiles);
        InvalidateBenchmarkPlan();
        return Task.CompletedTask;
    }

    private async Task ValidateBenchmarkPlanAsync() =>
        _ = await BenchmarksPageWorkflowService.ValidateAsync(AppServices.Benchmarks.Value, BuildBenchmarkPlan(), _benchmarksPage);

    private async Task StartBenchmarkPlanAsync()
    {
        var plan = BuildBenchmarkPlan();
        var run = await BenchmarksPageWorkflowService.StartAsync(
            AppServices.Benchmarks.Value, plan, _sessions.Snapshots(), _benchmarksPage, this, _coreServices.App.Dialogs);
        if (run is null) return;
        if (_benchmarksPage is not null) { _benchmarksPage.ActiveRunId = run.Job.Id; _benchmarksPage.IsRunActive = true; }
        InvalidateBenchmarkPlan();
        await RefreshBenchmarksAsync();
    }

    private async Task PauseBenchmarkAsync()
    {
        var id = RequiredBenchmarkRunId();
        await AppServices.Benchmarks.Value.PauseAsync(id);
    }

    private async Task ResumeBenchmarkAsync()
    {
        var id = RequiredBenchmarkRunId();
        await AppServices.Benchmarks.Value.ResumeAsync(id);
        if (_benchmarksPage is not null) _benchmarksPage.ActiveRunId = id;
    }

    private async Task CancelBenchmarkAsync()
    {
        var id = RequiredActiveBenchmarkRunId();
        if (!_coreServices.App.Dialogs.Confirm(this, "Stop the active benchmark run and its current benchmark process? Completed results will be kept.", "Stop benchmark", MessageBoxImage.Warning)) return;
        await AppServices.Benchmarks.Value.CancelAsync(id);
    }

    private Task ShowBenchmarkDetailsAsync() => BenchmarksPageHistoryService.ShowDetailsAsync(AppServices.Benchmarks.Value, _stateStore!, RequiredBenchmarkRunId(), this);

    private Task ExportBenchmarkAsync() => BenchmarksPageHistoryService.ExportAsync(
        AppServices.Benchmarks.Value, _stateStore!, RequiredBenchmarkRunId(), SetStatus);

    private Task CompareBenchmarksAsync() => BenchmarksPageHistoryService.ShowComparisonAsync(
        _stateStore!, _benchmarksPage?.SelectedRunIds ?? [], this, _coreServices.App.Dialogs);

    private Task CloneBenchmarkPlanAsync() => BenchmarksPageWorkflowService.CloneAndValidateAsync(
        AppServices.Benchmarks.Value, RequiredBenchmarkRunId(), _benchmarksPage!, _benchmarkProfiles, _settings.WslDistro);

    private Task ImportBenchmarkPlanAsync() => BenchmarksPageWorkflowService.ImportAndValidateAsync(
        AppServices.Benchmarks.Value, _benchmarksPage!, _benchmarkProfiles, _settings.WslDistro);

    private Task ExportBenchmarkPlanAsync() =>
        BenchmarksPageWorkflowService.ExportPlanAsync(BuildBenchmarkPlan(), SetStatus);

    private Task OpenBenchmarkLogAsync() =>
        BenchmarksPageWorkflowService.OpenLogAsync(AppServices.Benchmarks.Value, RequiredBenchmarkRunId(), OpenLogPath);

    private Task DeleteBenchmarkAsync(string runId) => BenchmarksPageWorkflowService.DeleteAndRefreshAsync(AppServices.Benchmarks.Value, runId, this, _coreServices.App.Dialogs, RefreshBenchmarksAsync);

    private Task PreviousBenchmarkHistoryPageAsync() =>
        BenchmarksPageWorkflowService.PreviousPageAsync(_benchmarksPage, RefreshBenchmarksAsync);

    private Task NextBenchmarkHistoryPageAsync() =>
        BenchmarksPageWorkflowService.NextPageAsync(_benchmarksPage, RefreshBenchmarksAsync);

    private void InvalidateBenchmarkPlan()
    {
        if (_benchmarksPage?.RunButton is not null) _benchmarksPage.RunButton.IsEnabled = !_benchmarksPage.IsRunActive;
        if (_benchmarksPage?.Summary is not null) _benchmarksPage.Summary.Text = Loc.T("Benchmarks.PlanChanged");
    }

    private BenchmarkPlan BuildBenchmarkPlan() => BenchmarksPagePlanService.Build(
            _benchmarksPage ?? throw new InvalidOperationException("The Benchmarks page is not open."),
            _settings.WslDistro);

    private void SubscribeBenchmarkProgress()
    {
        var service = AppServices.Benchmarks.Value;
        _benchmarkProgressHandler = (_, snapshot) => Dispatcher.BeginInvoke(() =>
        {
            if (_benchmarksPage is null) return;
            _benchmarksPage.ActiveRunId = snapshot.Job.Id;
            ApplyBenchmarkProgress(snapshot);
            if (snapshot.Job.Status is JobStatus.Paused or JobStatus.Completed or JobStatus.Failed or JobStatus.Cancelled or JobStatus.Interrupted)
                RunBackground(RefreshBenchmarksAsync, "Benchmark history refresh failed");
        });
        service.ProgressChanged += _benchmarkProgressHandler;
    }

    private void ReleaseBenchmarksPage()
    {
        if (_benchmarkProgressHandler is not null && _appServices?.Benchmarks.IsValueCreated == true)
            _appServices.Benchmarks.Value.ProgressChanged -= _benchmarkProgressHandler;
        _benchmarkProgressHandler = null;
        _benchmarksPage?.ReleaseView();
        _benchmarksPage = null;
        _benchmarksController = null;
        _benchmarkProfiles = [];
    }

    private void ApplyBenchmarkRuns(IReadOnlyList<BenchmarkRunSnapshot> runs)
    {
        if (_benchmarksPage is null) return;
        BenchmarksPagePresentationService.ApplyRuns(_benchmarksPage, runs, Loc.FormatCulture);
    }

    private void ApplyBenchmarkProgress(BenchmarkRunSnapshot run)
    {
        if (_benchmarksPage is not null) BenchmarksPagePresentationService.ApplyProgress(_benchmarksPage, run);
    }

    private string RequiredBenchmarkRunId()
        => _benchmarksPage?.SelectedRunId is { Length: > 0 } id ? id : throw new InvalidOperationException("Select a benchmark run first.");

    private string RequiredActiveBenchmarkRunId() => _benchmarksPage?.ActiveRunId is { Length: > 0 } id ? id : throw new InvalidOperationException("There is no active benchmark run to stop.");

}
