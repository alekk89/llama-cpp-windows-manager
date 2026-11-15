using System.Windows;

namespace LocalLlmConsole;

public static class LaunchSettingsUnsavedChangesDialog
{
    public static LaunchSettingsUnsavedChoice Show()
    {
        var result = ThemedMessageBox.Show(
            System.Windows.Application.Current?.MainWindow,
            Loc.T("Launch.UnsavedChanges.Message"),
            Loc.T("Launch.UnsavedChanges.Title"),
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning,
            new Dictionary<MessageBoxResult, string>
            {
                [MessageBoxResult.Yes] = Loc.T("Common.Save"),
                [MessageBoxResult.No] = Loc.T("Launch.UnsavedChanges.Discard"),
                [MessageBoxResult.Cancel] = Loc.T("Common.Cancel")
            });
        return result switch
        {
            MessageBoxResult.Yes => LaunchSettingsUnsavedChoice.Save,
            MessageBoxResult.No => LaunchSettingsUnsavedChoice.Discard,
            _ => LaunchSettingsUnsavedChoice.Cancel
        };
    }
}
