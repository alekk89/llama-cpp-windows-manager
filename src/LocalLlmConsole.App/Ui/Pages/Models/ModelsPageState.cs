using System.Windows.Controls;
using System.Windows;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed class ModelsPageState
{
    private ModelsPageControls? _controls;

    public TextBlock? ModelsFolderText { get; private set; }

    public DataGrid? ModelsGrid { get; private set; }

    public DataGrid? ModelVariantsGrid { get; private set; }

    public WpfTextBox? HuggingFaceQueryBox { get; private set; }

    public DataGrid? HuggingFaceGrid { get; private set; }

    public DataGrid? DownloadHistoryGrid { get; private set; }

    public bool HasHuggingFaceGrid => HuggingFaceGrid is not null;

    public string HuggingFaceQuery => HuggingFaceQueryBox?.Text.Trim() ?? "";

    public ModelRecord? SelectedModel =>
        ModelsGrid?.SelectedItem is ModelGridRow row
            ? row.Model
            : ModelVariantsGrid?.SelectedItem is ModelGridRow variantRow
                ? variantRow.Model
                : null;

    public ModelGridRow? SelectedModelRow =>
        ModelsGrid?.SelectedItem as ModelGridRow
        ?? ModelVariantsGrid?.SelectedItem as ModelGridRow;

    public NamedModelLaunchProfile? SelectedLaunchProfile =>
        ModelVariantsGrid?.SelectedItem is ModelGridRow { LaunchProfile: { } profile } row
        && (SelectedModel is null || string.Equals(row.Model.Id, SelectedModel.Id, StringComparison.OrdinalIgnoreCase))
            ? profile
            : null;

    public string SelectedLaunchProfileId => SelectedLaunchProfile?.Id ?? "";

    public UiRow? SelectedHuggingFaceRow => HuggingFaceGrid?.SelectedItem as UiRow;

    public UiRow? SelectedDownloadHistoryRow => DownloadHistoryGrid?.SelectedItem as UiRow;

    public void Apply(ModelsPageControls controls)
    {
        ArgumentNullException.ThrowIfNull(controls);

        _controls = controls;
        ModelsFolderText = controls.ModelsFolderText;
        ModelsGrid = controls.ModelsGrid;
        ModelVariantsGrid = controls.ModelVariantsGrid;
        HuggingFaceQueryBox = controls.HuggingFaceQueryBox;
        HuggingFaceGrid = controls.HuggingFaceGrid;
        DownloadHistoryGrid = null;
    }

    public void ApplyUiPreferences(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        if (_controls is not { } controls) return;

        var visible = settings.ShowModelsHuggingFace;
        controls.HuggingFaceSection.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        controls.HuggingFaceSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        controls.Root.RowDefinitions[2].Height = visible ? new GridLength(8) : new GridLength(0);
        controls.Root.RowDefinitions[3].MinHeight = visible ? 120 : 0;
        controls.Root.RowDefinitions[3].Height = visible ? new GridLength(230) : new GridLength(0);
    }

    public void FocusModelsGrid()
        => ModelsGrid?.Focus();

    public void FocusHuggingFaceQueryBox()
        => HuggingFaceQueryBox?.Focus();

    public bool TrySelectModelGridRow(DataGrid? selectedGrid, DataGrid? otherGrid)
    {
        if (selectedGrid?.SelectedItem is not ModelGridRow)
            return false;
        return true;
    }

    public void SelectDefaultLaunchProfile(IReadOnlyList<ModelGridRow> profileRows)
    {
        ArgumentNullException.ThrowIfNull(profileRows);
        if (ModelVariantsGrid is null) return;
        ModelVariantsGrid.SelectedItem = profileRows.FirstOrDefault(row => row.LaunchProfile?.IsDefault == true)
            ?? profileRows.FirstOrDefault();
    }

    public void SelectModelAfterRefresh(
        string? selectedId,
        string? selectedProfileId,
        IReadOnlyList<ModelGridRow> modelRows,
        IReadOnlyList<ModelGridRow> variantRows)
    {
        ArgumentNullException.ThrowIfNull(modelRows);
        ArgumentNullException.ThrowIfNull(variantRows);
        if (ModelsGrid is null) return;

        var requestedProfile = variantRows.FirstOrDefault(row => string.Equals(
            row.LaunchProfile?.Id,
            selectedProfileId,
            StringComparison.OrdinalIgnoreCase));
        var modelId = requestedProfile?.Model.Id ?? selectedId;
        var modelRow = modelRows.FirstOrDefault(row => string.Equals(row.Model.Id, modelId, StringComparison.OrdinalIgnoreCase))
            ?? modelRows.FirstOrDefault();
        ModelsGrid.SelectedItem = modelRow;

        if (ModelVariantsGrid is null) return;
        ModelVariantsGrid.SelectedItem = requestedProfile
            ?? variantRows.FirstOrDefault(row => row.LaunchProfile?.IsDefault == true)
            ?? variantRows.FirstOrDefault();
    }

    public DataGrid? UseHuggingFaceSearchGrid()
    {
        DownloadHistoryGrid = null;
        return HuggingFaceGrid;
    }

    public DataGrid? UseDownloadHistoryGrid()
    {
        DownloadHistoryGrid = HuggingFaceGrid;
        return DownloadHistoryGrid;
    }

    public void RestoreDownloadHistorySelection(string? selectedId, IReadOnlyList<UiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (DownloadHistoryGrid is null || string.IsNullOrWhiteSpace(selectedId)) return;

        DownloadHistoryGrid.SelectedItem = rows.FirstOrDefault(row =>
            string.Equals(row.Data["Id"]?.ToString(), selectedId, StringComparison.OrdinalIgnoreCase));
    }

    public void SelectDownloadHistoryJob(string? jobId, IReadOnlyList<UiRow> rows)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (DownloadHistoryGrid is null || string.IsNullOrWhiteSpace(jobId)) return;

        DownloadHistoryGrid.SelectedItem = rows.FirstOrDefault(row =>
            string.Equals(row.Data["Id"]?.ToString(), jobId, StringComparison.OrdinalIgnoreCase));
        if (DownloadHistoryGrid.SelectedItem is not null)
            DownloadHistoryGrid.ScrollIntoView(DownloadHistoryGrid.SelectedItem);
    }
}
