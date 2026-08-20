using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed record LogsPageActionControllerActions(
    Func<Task> RefreshLogsAsync,
    Action OpenSelectedLogFile,
    Action OpenLogsFolder,
    Func<Task> CreateDiagnosticsBundleAsync,
    Func<Task> DeleteSelectedLogAsync,
    Func<Task> DeleteAllLogsAsync,
    Action<string> OpenLogPath,
    Func<string, Task> DeleteLogPathAsync,
    Func<UiRow?, string> LogPathFromRow,
    Action LoadSelectedLog,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class LogsPageActionController
{
    private readonly LogsPageActionControllerActions _actions;

    public LogsPageActionController(LogsPageActionControllerActions actions)
    {
        _actions = actions;
    }

    public LogsPageActions Build()
        => new(
            async (_, _) => await _actions.RefreshLogsAsync(),
            (_, _) => _actions.OpenSelectedLogFile(),
            (_, _) => _actions.OpenLogsFolder(),
            async (_, _) => await _actions.CreateDiagnosticsBundleAsync(),
            async (_, _) => await _actions.DeleteSelectedLogAsync(),
            async (_, _) => await _actions.DeleteAllLogsAsync(),
            OpenLogRow_Click,
            DeleteLogRow_Click,
            (_, _) => _actions.LoadSelectedLog());

    private void OpenLogRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is UiRow row)
            _actions.OpenLogPath(_actions.LogPathFromRow(row));
    }

    private async void DeleteLogRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            if ((sender as FrameworkElement)?.Tag is UiRow row)
                await _actions.DeleteLogPathAsync(_actions.LogPathFromRow(row));
        });
    }
}
