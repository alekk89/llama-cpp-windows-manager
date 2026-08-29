using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfProgressBar = System.Windows.Controls.ProgressBar;
using WpfTextBox = System.Windows.Controls.TextBox;
namespace LocalLlmConsole;

public partial class MainWindow
{
    private async Task ShowDownloadHistoryAsync(string selectedJobId = "")
    {
        var downloadHistory = AppServices.DownloadHistoryApplication;
        Require(downloadHistory);
        await downloadHistory!.ShowAsync(
            selectedJobId,
            new DownloadHistoryShowActions(
                () => _viewModel.CurrentPage == "Models" && _modelsPage.HasHuggingFaceGrid,
                ShowModels,
                ConfigureDownloadHistoryGrid,
                RefreshDownloadHistoryAsync,
                SelectDownloadHistoryJob,
                StartDownloadHistoryRefreshTimer,
                SetStatus));
    }

    private async Task RefreshDownloadHistoryAsync()
    {
        var downloadHistory = AppServices.DownloadHistoryApplication;
        if (downloadHistory is null) return;
        var selectedId = SelectedDownloadJob()?.Id;
        _viewModel.HuggingFace.ReplaceDownloadHistory(await downloadHistory.ListJobsAsync());
        _modelsPage.RestoreDownloadHistorySelection(selectedId, _viewModel.HuggingFace.DownloadHistoryRows);
    }

    private void SelectDownloadHistoryJob(string jobId)
    {
        _modelsPage.SelectDownloadHistoryJob(jobId, _viewModel.HuggingFace.DownloadHistoryRows);
    }

    private async Task ResumeDownloadAsync(JobRecord? job)
    {
        var downloadHistory = AppServices.DownloadHistoryApplication;
        Require(downloadHistory);
        await downloadHistory!.ResumeAsync(job, _settings, DownloadHistoryCommandActions());
    }

    private async Task PauseDownloadAsync(JobRecord? job)
    {
        var downloadHistory = AppServices.DownloadHistoryApplication;
        Require(downloadHistory);
        await downloadHistory!.PauseAsync(job, DownloadHistoryCommandActions());
    }

    private async Task StopDownloadAsync(JobRecord? job)
    {
        var downloadHistory = AppServices.DownloadHistoryApplication;
        Require(downloadHistory);
        await downloadHistory!.StopAsync(job, DownloadHistoryCommandActions());
    }

    private async Task DeleteDownloadAsync(JobRecord? job)
    {
        var downloadHistory = AppServices.DownloadHistoryApplication;
        Require(downloadHistory);
        await downloadHistory!.DeleteAsync(job, _settings, DownloadHistoryDeleteActions());
    }

    private async Task MonitorDownloadAsync(string jobId)
    {
        await _coreServices.App.DownloadCompletionApplication.MonitorAsync(
            jobId,
            new DownloadCompletionApplicationActions(
                async (completedJobId, interval) =>
                {
                    var downloadHistory = AppServices.DownloadHistoryApplication;
                    if (downloadHistory is not null)
                        await downloadHistory.WaitUntilInactiveOrTerminalAsync(completedJobId, interval);
                },
                RunDownloadCompletionOnUiThreadAsync,
                async () =>
                {
                    var catalog = ModelServices.Catalog;
                    if (catalog is not null)
                        await catalog.ScanAsync(_settings.ModelsRoot);
                },
                RefreshModelsAsync,
                RefreshOverviewAsync,
                RefreshDownloadHistoryAsync,
                RefreshHuggingFaceInstallStateAsync));
    }

    private DownloadHistoryCommandApplicationActions DownloadHistoryCommandActions()
        => new(
            RunAsync,
            RefreshDownloadHistoryAsync,
            SetStatus,
            jobId => RunBackground(() => MonitorDownloadAsync(jobId), "Download monitor failed"));

    private DownloadHistoryDeleteApplicationActions DownloadHistoryDeleteActions()
        => new(
            deletePlan => _coreServices.App.Dialogs.Confirm(
                this,
                deletePlan.ConfirmationMessage,
                "Delete model download",
                MessageBoxImage.Warning),
            DownloadHistoryCommandActions());

    private async Task RunDownloadCompletionOnUiThreadAsync(Func<Task> action)
        => await await Dispatcher.InvokeAsync(action);

    private void ConfigureHfSearchGrid()
    {
        var grid = _modelsPage.UseHuggingFaceSearchGrid();
        if (grid is null) return;
        StopDownloadHistoryRefreshTimer();
        _downloadHistoryPageState.ShowSearch();
        HuggingFaceGridModeFactory.ConfigureSearch(HuggingFaceGridModeRequest(grid));
    }

    private void ConfigureDownloadHistoryGrid()
    {
        var grid = _modelsPage.UseDownloadHistoryGrid();
        if (grid is null) return;
        _downloadHistoryPageState.ShowHistory();
        HuggingFaceGridModeFactory.ConfigureDownloadHistory(HuggingFaceGridModeRequest(grid));
    }

    private HuggingFaceGridModeRequest HuggingFaceGridModeRequest(DataGrid grid)
        => new(
            grid,
            _viewModel.HuggingFace.SearchRows,
            _viewModel.HuggingFace.DownloadHistoryRows,
            new HuggingFaceGridModeActions(
                _pageControllers.ModelRows.DownloadHfRow_Click,
                _pageControllers.ModelRows.OpenHuggingFaceModelCardRow_Click,
                _pageControllers.DownloadHistoryRows.ResumeDownloadRow_Click,
                _pageControllers.DownloadHistoryRows.PauseDownloadRow_Click,
                _pageControllers.DownloadHistoryRows.StopDownloadRow_Click,
                _pageControllers.DownloadHistoryRows.DeleteDownloadRow_Click),
            SetHfSearchGridColumnSizing,
            SetDownloadHistoryGridColumnSizing);

    private void StartDownloadHistoryRefreshTimer()
    {
        _coreServices.Ui.DownloadHistoryRefreshTimer.Start(
            TimeSpan.FromSeconds(1.5),
            DownloadHistoryTimerRefreshAsync,
            ex => SetStatus(Loc.T("Downloads.HistoryRefreshFailed", ex.Message)));
    }

    private void StopDownloadHistoryRefreshTimer()
    {
        _coreServices.Ui.DownloadHistoryRefreshTimer.Stop();
    }

    private async Task DownloadHistoryTimerRefreshAsync()
    {
        var downloadHistory = AppServices.DownloadHistoryApplication;
        if (downloadHistory is null) return;
        await downloadHistory.RefreshTimerAsync(new DownloadHistoryTimerRefreshActions(
            _downloadHistoryPageState.TryBeginTimerRefresh,
            RefreshDownloadHistoryAsync,
            _downloadHistoryPageState.CompleteTimerRefresh));
    }

}
