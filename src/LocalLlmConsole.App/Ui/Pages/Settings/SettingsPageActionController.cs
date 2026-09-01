using System.Windows;

namespace LocalLlmConsole;

public sealed record SettingsPageActionControllerActions(
    Action PreviewTheme,
    Action CommitSlider,
    Func<object, EditableSettingRow?> RowFromSender,
    Func<EditableSettingRow?, Task> RunRowActionAsync,
    Action<EditableSettingRow?> ToggleSecret,
    Action<EditableSettingRow?> CopySecret,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class SettingsPageActionController
{
    private readonly SettingsPageActionControllerActions _actions;

    public SettingsPageActionController(SettingsPageActionControllerActions actions)
    {
        _actions = actions;
    }

    public SettingsPageActions Build()
        => new(
            (_, _) => _actions.PreviewTheme(),
            RevealSecretRow_Click,
            CopySecretRow_Click,
            RowAction_Click,
            _actions.CommitSlider);

    private async void RowAction_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            await _actions.RunRowActionAsync(_actions.RowFromSender(sender));
        });
    }

    private async void RevealSecretRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(() =>
        {
            _actions.ToggleSecret(_actions.RowFromSender(sender));
            return Task.CompletedTask;
        });
    }

    private async void CopySecretRow_Click(object sender, RoutedEventArgs e)
    {
        await _actions.RunEventAsync(() =>
        {
            _actions.CopySecret(_actions.RowFromSender(sender));
            return Task.CompletedTask;
        });
    }
}
