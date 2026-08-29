using System.Windows;

namespace LocalLlmConsole;

public sealed record WslPageActionControllerActions(
    Func<Task> RefreshAsync,
    Func<Task> InstallWslAsync,
    Func<Task> CheckWslUpdatesAsync,
    Func<Task> DeleteWslAsync,
    Func<Task> InstallUbuntuAsync,
    Func<Task> CheckUbuntuUpdatesAsync,
    Func<Task> DeleteUbuntuAsync,
    Func<Task> InstallBuildToolsAsync,
    Func<Task> DeleteBuildToolsAsync,
    Func<Task> InstallCudaToolkitAsync,
    Func<Task> DeleteCudaToolkitAsync,
    Func<Task> InstallVulkanToolsAsync,
    Func<Task> DeleteVulkanToolsAsync,
    Func<Task> InstallSyclRuntimeAsync,
    Func<Task> DeleteSyclRuntimeAsync,
    Func<Task> InstallSyclOneApiAsync,
    Func<Task> DeleteSyclOneApiAsync,
    Func<WslDistroRow?, Task> SelectDistroAsync,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class WslPageActionController
{
    private readonly WslPageActionControllerActions _actions;

    public WslPageActionController(WslPageActionControllerActions actions)
    {
        _actions = actions;
    }

    public WslPageActions Build()
        => new(
            _actions.RefreshAsync,
            _actions.InstallWslAsync,
            _actions.CheckWslUpdatesAsync,
            _actions.DeleteWslAsync,
            _actions.InstallUbuntuAsync,
            _actions.CheckUbuntuUpdatesAsync,
            _actions.DeleteUbuntuAsync,
            _actions.InstallBuildToolsAsync,
            _actions.DeleteBuildToolsAsync,
            _actions.InstallCudaToolkitAsync,
            _actions.DeleteCudaToolkitAsync,
            _actions.InstallVulkanToolsAsync,
            _actions.DeleteVulkanToolsAsync,
            _actions.InstallSyclRuntimeAsync,
            _actions.DeleteSyclRuntimeAsync,
            _actions.InstallSyclOneApiAsync,
            _actions.DeleteSyclOneApiAsync,
            UseDistroRow_Click);

    private async void UseDistroRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            await _actions.SelectDistroAsync((sender as FrameworkElement)?.Tag as WslDistroRow);
        });
    }
}
