using System.Windows.Controls;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class LogsPageState
{
    private DataGrid? LogsGrid { get; set; }

    private WpfTextBox? LogsBox { get; set; }

    private string PreviewIdentity { get; set; } = "";

    public UiRow? SelectedLogRow => LogsGrid?.SelectedItem as UiRow;

    public string SelectedLogPath => LogPathFromRow(SelectedLogRow);

    public void Apply(LogsPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        LogsGrid = controls.LogsGrid;
        LogsBox = controls.LogsBox;
        PreviewIdentity = "";
    }

    public void FocusLogsGrid()
        => LogsGrid?.Focus();

    public string[] SelectedLogPaths()
    {
        if (LogsGrid is null) return [];
        var paths = LogsGrid.SelectedItems
            .Cast<object>()
            .OfType<UiRow>()
            .Select(LogPathFromRow)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
        if (paths.Length > 0) return paths;
        var fallback = SelectedLogPath;
        return string.IsNullOrWhiteSpace(fallback) ? [] : [fallback];
    }

    public void RestoreSelection(ISet<string> selectedPaths, IReadOnlyList<UiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(selectedPaths);
        ArgumentNullException.ThrowIfNull(rows);
        if (LogsGrid is null) return;

        LogsGrid.SelectedItems.Clear();
        foreach (var row in rows.Where(row => selectedPaths.Contains(LogPathFromRow(row))))
            LogsGrid.SelectedItems.Add(row);
        if (LogsGrid.SelectedItems.Count == 0)
            LogsGrid.SelectedItem = rows.FirstOrDefault();
    }

    public bool HasPreviewBox => LogsBox is not null;

    public void ClearPreview()
    {
        PreviewIdentity = "";
        TextBoxTailPresenter.SetText(LogsBox, "", followTail: false);
    }

    public void SetPreviewText(string text, string identity = "", bool scrollToEnd = false)
    {
        var identityChanged = !string.Equals(PreviewIdentity, identity, StringComparison.OrdinalIgnoreCase);
        PreviewIdentity = identity;
        TextBoxTailPresenter.SetText(LogsBox, text, scrollToEnd, forceFollowTail: identityChanged);
    }

    private static string LogPathFromRow(UiRow? row)
        => row?.Data["Path"]?.ToString() ?? "";
}
