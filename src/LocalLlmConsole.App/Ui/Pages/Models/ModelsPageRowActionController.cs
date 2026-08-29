using System.Windows;

namespace LocalLlmConsole;

public sealed record ModelsPageRowActionControllerActions(
    Func<object, ModelRecord?> ModelFromRowButton,
    Func<object, ModelGridRow?> ModelRowFromButton,
    Func<ModelFolderApplicationActions> ModelFolderActions,
    Func<ModelGridRow, Task> DeleteModelRowAsync,
    Func<HuggingFaceFile, Task> StartHuggingFaceDownloadAsync,
    Func<HuggingFaceModelCardApplicationActions> ModelCardActions,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class ModelsPageRowActionController
{
    private readonly ModelFolderApplicationService _modelFolders;
    private readonly HuggingFaceModelCardApplicationService _modelCards;
    private readonly ModelsPageRowActionControllerActions _actions;

    public ModelsPageRowActionController(
        ModelFolderApplicationService modelFolders,
        HuggingFaceModelCardApplicationService modelCards,
        ModelsPageRowActionControllerActions actions)
    {
        _modelFolders = modelFolders;
        _modelCards = modelCards;
        _actions = actions;
    }

    public void OpenModelFolderRow_Click(object sender, RoutedEventArgs e)
        => _modelFolders.Open(_actions.ModelFromRowButton(sender), _actions.ModelFolderActions());

    public async void DeleteModelRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            var row = _actions.ModelRowFromButton(sender);
            if (row is not null) await _actions.DeleteModelRowAsync(row);
        });
    }

    public async void DownloadHfRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            if ((sender as FrameworkElement)?.Tag is not HuggingFaceSearchRow { CanDownload: true } row) return;
            await _actions.StartHuggingFaceDownloadAsync(row.File);
        });
    }

    public void OpenHuggingFaceModelCardRow_Click(object sender, RoutedEventArgs e)
    {
        _modelCards.OpenFromRow(
            (sender as FrameworkElement)?.Tag as HuggingFaceSearchRow,
            _actions.ModelCardActions());
    }
}
