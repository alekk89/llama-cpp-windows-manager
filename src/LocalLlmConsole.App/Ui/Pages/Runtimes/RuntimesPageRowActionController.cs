using System.Windows;

namespace LocalLlmConsole;

public sealed record RuntimesPageRowActionControllerActions(
    Func<object, RuntimeRecord?> RuntimeFromRowButton,
    Func<object, RuntimeSourceEntry?> RuntimeSourceFromRowButton,
    Func<object, RuntimePackagePreset?> RuntimePackagePresetFromRowButton,
    Func<RuntimePackagePresetRow, Task> RunRuntimeSourceRowActionAsync,
    Func<RuntimePackagePreset, Task> InstallRuntimePackageAsync,
    Func<RuntimePackagePreset, RuntimePackagePresetRow?, Task> CheckRuntimePackageUpdateAsync,
    Func<RuntimePackagePresetRow, Task> DeleteRuntimeDownloadRowAsync,
    Func<RuntimeSourceEntry, Task> DeleteRuntimeSourceAsync,
    Func<RuntimeRecord, Task> DeleteRuntimeBuildAsync,
    Func<RuntimeRecord, Task> VerifyRuntimeInstallationAsync,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class RuntimesPageRowActionController
{
    private readonly RuntimesPageRowActionControllerActions _actions;

    public RuntimesPageRowActionController(RuntimesPageRowActionControllerActions actions)
    {
        _actions = actions;
    }

    public async void RuntimeSourceRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            if ((sender as FrameworkElement)?.Tag is RuntimePackagePresetRow row)
                await _actions.RunRuntimeSourceRowActionAsync(row);
        });
    }

    public async void InstallRuntimePackageRow_Click(object sender, RoutedEventArgs e)
        => await RunRuntimePackageActionAsync(sender, preset => _actions.InstallRuntimePackageAsync(preset));

    public async void CheckRuntimePackageUpdateRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            var row = (sender as FrameworkElement)?.Tag as RuntimePackagePresetRow;
            var preset = _actions.RuntimePackagePresetFromRowButton(sender);
            if (preset is not null) await _actions.CheckRuntimePackageUpdateAsync(preset, row);
        });
    }

    public async void DeleteRuntimePackageRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            if ((sender as FrameworkElement)?.Tag is RuntimePackagePresetRow row)
                await _actions.DeleteRuntimeDownloadRowAsync(row);
        });
    }

    public async void DeleteRuntimeRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            var source = _actions.RuntimeSourceFromRowButton(sender);
            if (source is not null)
            {
                await _actions.DeleteRuntimeSourceAsync(source);
                return;
            }

            var runtime = _actions.RuntimeFromRowButton(sender);
            if (runtime is not null) await _actions.DeleteRuntimeBuildAsync(runtime);
        });
    }

    public async void VerifyRuntimeRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            var runtime = _actions.RuntimeFromRowButton(sender);
            if (runtime is not null) await _actions.VerifyRuntimeInstallationAsync(runtime);
        });
    }

    private async Task RunRuntimePackageActionAsync(object sender, Func<RuntimePackagePreset, Task> action)
    {
        await _actions.RunEventAsync(async () =>
        {
            var preset = _actions.RuntimePackagePresetFromRowButton(sender);
            if (preset is not null) await action(preset);
        });
    }

}
