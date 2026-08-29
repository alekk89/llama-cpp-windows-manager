using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class LogsViewModel
{
    public ObservableCollection<LogFileRow> Rows { get; } = new();

    public void ReplaceLogs(
        IEnumerable<LogPageFile> files,
        IReadOnlyDictionary<string, JobRecord> jobsByLogPath,
        string activeLogPath,
        string activeModel)
    {
        Rows.Clear();
        var normalizedActiveLogPath = LogFileService.NormalizePath(activeLogPath);
        foreach (var file in files.OrderByDescending(file => file.LastWriteTimeUtc))
        {
            var path = LogFileService.NormalizePath(file.FullPath);
            jobsByLogPath.TryGetValue(path, out var job);
            var (type, related) = LogFileService.Describe(file.FullPath, job, path == normalizedActiveLogPath, activeModel);
            Rows.Add(new LogFileRow
            {
                Type = type,
                FileName = file.Name,
                Related = related,
                Updated = file.LastWriteTime.ToString("g"),
                Size = DisplayFormatService.Bytes(file.Length),
                FullPath = file.FullPath,
                OpenAction = Localization.Loc.T("Logs.ActionBtn.Open"),
                DeleteAction = Localization.Loc.T("Logs.ActionBtn.Delete"),
                OpenToolTip = Localization.Loc.T("Tooltip.OpenSelectedLog"),
                DeleteToolTip = path == normalizedActiveLogPath ? Localization.Loc.T("Logs.DeleteBlockedActive") : Localization.Loc.T("Tooltip.DeleteSelectedLogs")
            });
        }
    }
}
