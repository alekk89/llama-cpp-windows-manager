using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed record HuggingFaceGridModeActions(
    RoutedEventHandler DownloadSearchRow,
    RoutedEventHandler OpenModelCardRow,
    RoutedEventHandler ResumeDownloadRow,
    RoutedEventHandler PauseDownloadRow,
    RoutedEventHandler StopDownloadRow,
    RoutedEventHandler DeleteDownloadRow);

public sealed record HuggingFaceGridModeRequest(
    DataGrid Grid,
    IEnumerable SearchRows,
    IEnumerable DownloadHistoryRows,
    HuggingFaceGridModeActions Actions,
    Action<DataGrid> ConfigureSearchColumnSizing,
    Action<DataGrid> ConfigureDownloadHistoryColumnSizing);

public static class HuggingFaceGridModeFactory
{
    public static void ConfigureSearch(HuggingFaceGridModeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Grid);
        ArgumentNullException.ThrowIfNull(request.Actions);

        PageSectionFactory.ConfigureGridColumns(
            request.Grid,
            (Loc.T("HfSearch.Col.Repo"), nameof(HuggingFaceSearchRow.Repo), 1.3),
            (Loc.T("HfSearch.Col.File"), nameof(HuggingFaceSearchRow.FilePath), 2.3),
            (Loc.T("HfSearch.Col.Quant"), nameof(HuggingFaceSearchRow.Quant), .6),
            (Loc.T("HfSearch.Col.Size"), nameof(HuggingFaceSearchRow.Size), .8),
            (Loc.T("HfSearch.Col.Downloads"), nameof(HuggingFaceSearchRow.Downloads), .8),
            (Loc.T("HfSearch.Col.Signals"), nameof(HuggingFaceSearchRow.Signals), 1.4));
        PageSectionFactory.AddButtonColumn(request.Grid, Loc.T("HfSearch.Col.Actions"), nameof(HuggingFaceSearchRow.DownloadAction), nameof(HuggingFaceSearchRow.CanDownload), request.Actions.DownloadSearchRow, .8, tooltipBinding: nameof(HuggingFaceSearchRow.DownloadToolTip), visualRole: VisualRole.Primary);
        PageSectionFactory.AddButtonColumn(request.Grid, Loc.T("HfSearch.Col.Card"), nameof(HuggingFaceSearchRow.CardAction), nameof(HuggingFaceSearchRow.CanOpenCard), request.Actions.OpenModelCardRow, .6, tooltipBinding: nameof(HuggingFaceSearchRow.CardToolTip));
        PageSectionFactory.ApplyGridTextMargin(request.Grid, new Thickness(6, 0, 6, 0));
        request.ConfigureSearchColumnSizing(request.Grid);
        request.Grid.SelectedItem = null;
        request.Grid.ItemsSource = request.SearchRows;
    }

    public static void ConfigureDownloadHistory(HuggingFaceGridModeRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Grid);
        ArgumentNullException.ThrowIfNull(request.Actions);

        PageSectionFactory.ConfigureGridColumns(
            request.Grid,
            (Loc.T("DownloadHistory.Col.Status"), nameof(HuggingFaceDownloadRow.Status), .8),
            (Loc.T("DownloadHistory.Col.Model"), nameof(HuggingFaceDownloadRow.Model), 2.1),
            (Loc.T("DownloadHistory.Col.Progress"), nameof(HuggingFaceDownloadRow.Progress), 1.1),
            (Loc.T("DownloadHistory.Col.Size"), nameof(HuggingFaceDownloadRow.Size), .8),
            (Loc.T("DownloadHistory.Col.Updated"), nameof(HuggingFaceDownloadRow.Updated), 1),
            (Loc.T("DownloadHistory.Col.Destination"), nameof(HuggingFaceDownloadRow.Destination), 2.4));
        PageSectionFactory.AddButtonColumn(request.Grid, Loc.T("DownloadHistory.Action.Start"), nameof(HuggingFaceDownloadRow.StartAction), nameof(HuggingFaceDownloadRow.CanStart), request.Actions.ResumeDownloadRow, .7, tooltipBinding: nameof(HuggingFaceDownloadRow.StartToolTip), visualRole: VisualRole.Primary);
        PageSectionFactory.AddButtonColumn(request.Grid, Loc.T("DownloadHistory.Action.Pause"), nameof(HuggingFaceDownloadRow.PauseAction), nameof(HuggingFaceDownloadRow.CanPause), request.Actions.PauseDownloadRow, .7, tooltipBinding: nameof(HuggingFaceDownloadRow.PauseToolTip));
        PageSectionFactory.AddButtonColumn(request.Grid, Loc.T("DownloadHistory.Action.Stop"), nameof(HuggingFaceDownloadRow.StopAction), nameof(HuggingFaceDownloadRow.CanStop), request.Actions.StopDownloadRow, .7, tooltipBinding: nameof(HuggingFaceDownloadRow.StopToolTip), visualRole: VisualRole.Danger);
        PageSectionFactory.AddButtonColumn(request.Grid, Loc.T("Common.DeleteButton"), nameof(HuggingFaceDownloadRow.DeleteAction), nameof(HuggingFaceDownloadRow.CanDelete), request.Actions.DeleteDownloadRow, .7, tooltipBinding: nameof(HuggingFaceDownloadRow.DeleteToolTip), visualRole: VisualRole.Danger);
        PageSectionFactory.ApplyGridTextMargin(request.Grid, new Thickness(6, 0, 6, 0));
        request.ConfigureDownloadHistoryColumnSizing(request.Grid);
        request.Grid.SelectedItem = null;
        request.Grid.ItemsSource = request.DownloadHistoryRows;
    }
}
