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
    private void ShowLogs()
    {
        SetPage("Logs", "App and model log files.");
        var page = LogsPageFactory.Create(new LogsPageRequest(
            _viewModel.Logs.Rows,
            _pageControllers.Logs.Build(),
            ButtonToolTip));
        _logsPage.Apply(page.Controls);
        PageHost.Content = page.Content;
        RunBackground(RefreshLogsAsync, "Logs refresh failed");
    }

    private async Task RefreshLogsAsync()
    {
        var logPageApplication = AppServices.LogPageApplication;
        Require(logPageApplication);
        var selectedPaths = SelectedLogPaths().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var refresh = await logPageApplication!.LoadAsync(_sessions.SelectedSnapshot());
        _viewModel.Logs.ReplaceLogs(refresh.Files, refresh.JobsByLogPath, refresh.ActiveLogPath, refresh.ActiveModel);
        _logsPage.RestoreSelection(selectedPaths, _viewModel.Logs.Rows);

        LoadSelectedLog();
    }

    private void LoadSelectedLog()
        => RunBackground(LoadSelectedLogAsync, "Log preview failed");

    private async Task LoadSelectedLogAsync()
    {
        if (!_logsPage.HasPreviewBox) return;
        var row = _logsPage.SelectedLogRow;
        var path = LogPathFromRow(row);

        try
        {
            var logPageApplication = AppServices.LogPageApplication;
            Require(logPageApplication);
            var preview = await logPageApplication!.BuildPreviewAsync(new LogPreviewApplicationRequest(
                row,
                _settings.ModelApiKey,
                _viewModel.Logs.Rows.Count > 0));
            if (!string.Equals(path, SelectedLogPath(), StringComparison.OrdinalIgnoreCase)) return;
            _logsPage.SetPreviewText(preview, path, scrollToEnd: !string.IsNullOrWhiteSpace(path));
        }
        catch (Exception ex)
        {
            _logsPage.SetPreviewText($"Could not read log file.{Environment.NewLine}{Environment.NewLine}{ex.Message}", path);
        }
    }

    private void OpenSelectedLogFile()
        => OpenLogPath(SelectedLogPath());

    private async Task CreateDiagnosticsBundleAsync()
    {
        var stateStore = AppServices.StateStore;
        var wsl = await BestEffortAsync(
            async () => (await _coreServices.Environment.WslPageWorkflow.DetectRecommendedDistroAsync(_settings)).Report,
            new WslEnvironmentReport(false, false, "WSL probe unavailable", "", "", "", "", []));
        var gpu = await BestEffortAsync(() => _coreServices.App.GpuStatus.WindowsSummaryAsync(), "GPU probe unavailable");
        var cpu = await BestEffortAsync(() => _coreServices.App.GpuStatus.CpuSummaryAsync(), "CPU probe unavailable");
        var result = await DiagnosticsBundleService.CreateAsync(new DiagnosticsBundleRequest(
            Path.Combine(_workspaceRoot, "diagnostics"),
            Path.Combine(_workspaceRoot, "logs"),
            AppVersionLabel,
            _settings,
            await stateStore.ListModelsAsync(),
            await stateStore.ListRuntimesAsync(),
            await stateStore.ListJobsAsync(),
            _sessions.Snapshots(),
            wsl,
            gpu,
            cpu));

        _coreServices.App.ShellIntegration.OpenFolder(Path.GetDirectoryName(result.ArchivePath)!);
        SetStatus($"{Loc.T("Status.DiagnosticsBundleCreated")} {Path.GetFileName(result.ArchivePath)}");
    }

    private static async Task<T> BestEffortAsync<T>(Func<Task<T>> action, T fallback)
    {
        try
        {
            return await action();
        }
        catch
        {
            return fallback;
        }
    }

    private async Task DeleteSelectedLogAsync()
    {
        var logPageApplication = AppServices.LogPageApplication;
        Require(logPageApplication);
        await DeleteLogsAsync(logPageApplication!.BuildSelectedDeletionCommand(SelectedLogPaths(), _sessions.Snapshots()));
    }

    private async Task DeleteLogPathAsync(string path)
    {
        var logPageApplication = AppServices.LogPageApplication;
        Require(logPageApplication);
        await DeleteLogsAsync(logPageApplication!.BuildSingleDeletionCommand(path, _sessions.Snapshots()));
    }

    private async Task DeleteAllLogsAsync()
    {
        var logPageApplication = AppServices.LogPageApplication;
        Require(logPageApplication);
        await DeleteLogsAsync(await logPageApplication!.BuildAllDeletionCommandAsync(_sessions.Snapshots()));
    }

    private async Task DeleteLogsAsync(LogDeleteCommandPlan commandPlan)
    {
        var logPageApplication = AppServices.LogPageApplication;
        Require(logPageApplication);
        await logPageApplication!.DeleteAsync(commandPlan, LogPageDeleteActions());
    }

    private void OpenLogPath(string path)
    {
        var logPageApplication = AppServices.LogPageApplication;
        if (logPageApplication is null)
        {
            SetStatus(Loc.T("Status.LogsNotReady"));
            return;
        }

        logPageApplication.Open(path, LogPageOpenActions());
    }

    private LogPageOpenApplicationActions LogPageOpenActions()
        => new(
            _coreServices.App.ShellIntegration.OpenPath,
            SetStatus);

    private LogPageDeleteApplicationActions LogPageDeleteActions()
        => new(
            commandPlan => _coreServices.App.Dialogs.Confirm(
                this,
                commandPlan.ConfirmationMessage,
                commandPlan.ConfirmationTitle,
                MessageBoxImage.Warning),
            RunAsync,
            _logsPage.ClearPreview,
            RefreshLogsAsync,
            SetStatus);

    private string SelectedLogPath() => _logsPage.SelectedLogPath;

    private string[] SelectedLogPaths() => _logsPage.SelectedLogPaths();

    private static string LogPathFromRow(UiRow? row)
        => row?.Data["Path"]?.ToString() ?? "";

    private async Task WriteAppLogAsync(Exception ex)
        => await _coreServices.App.AppLogApplication.WriteExceptionAsync(ex, _settings.ModelApiKey, MaxLogBytes());
}
