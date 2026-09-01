namespace LocalLlmConsole;

public partial class MainWindow
{
    private Task RefreshOverviewModelSelectorAsync()
        => _overviewSelection.RefreshModelsAsync();

    private Task RefreshOverviewModelChoicesAsync(
        IReadOnlyList<ModelRecord> models,
        IReadOnlyDictionary<string, string>? modelSizeLabels = null)
        => _overviewSelection.RefreshModelChoicesAsync(models, modelSizeLabels);

    private string SelectedOverviewLaunchProfileId()
        => _overviewSelection.SelectedLaunchProfileId;

    private Task SelectOverviewLaunchProfileAsync()
    {
        _overviewSelection.UpdateActions();
        return Task.CompletedTask;
    }

    private ModelRecord? SelectedOverviewModel()
        => _overviewSelection.SelectedModel();

    private ModelGroupRecord? SelectedOverviewGroup()
        => _overviewSelection.SelectedGroup();

    private void UpdateOverviewModelActions()
        => _overviewSelection.UpdateActions();

    private bool IsOverviewSelectedProfileLoaded(ModelRecord? model)
        => _overviewSelection.IsSelectedProfileLoaded(model);

    private static string LoadedSessionIdFromRowButton(object sender)
        => OverviewSelectionController.SessionIdFromRowButton(sender);

    private static OverviewSessionRow? EndpointRowFromLink(object sender)
        => OverviewSelectionController.EndpointRowFromLink(sender);

    private Task InspectSelectedOverviewEndpointAsync()
        => _overviewSelection.InspectSelectedEndpointAsync();

    private Task InspectOverviewEndpointRowAsync(OverviewSessionRow row)
        => _overviewSelection.InspectEndpointRowAsync(row);

    private Task SelectOverviewModelSessionAsync(CancellationToken cancellationToken)
        => _overviewSelection.SelectModelSessionAsync(cancellationToken);

    private Task SelectLoadedSessionRowAsync(CancellationToken cancellationToken)
        => _overviewSelection.SelectLoadedSessionRowAsync(cancellationToken);

    private Task<bool> SelectOverviewLoadedModelAsync(string modelId)
        => _overviewSelection.SelectLoadedModelAsync(modelId);

    private Task<(string Model, string Runtime)> ActiveRuntimeLabelsAsync()
        => _overviewSelection.ActiveRuntimeLabelsAsync();

    private Task<string> ActiveModelDisplayNameAsync(string modelId)
        => _overviewSelection.ActiveModelDisplayNameAsync(modelId);
}
