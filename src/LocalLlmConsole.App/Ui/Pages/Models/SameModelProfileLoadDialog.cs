using System.Windows;

namespace LocalLlmConsole;

public static class SameModelProfileLoadDialog
{
    public static SameModelProfileLoadChoice Show(Window owner, ModelRecord model, IReadOnlyList<LoadedModelSessionSnapshot> existing, Action? beforeShow = null)
    {
        beforeShow?.Invoke();
        var result = ThemedMessageBox.Show(owner,
            Loc.T("Models.SameModelLoad.Message", model.Name,
                string.Join(Environment.NewLine, existing.Select(session => $"• {session.LaunchProfileName} (:{session.LaunchSettings.Port})"))),
            Loc.T("Models.SameModelLoad.Title"), MessageBoxButton.YesNoCancel, MessageBoxImage.Question,
            new Dictionary<MessageBoxResult, string>
            {
                [MessageBoxResult.Yes] = Loc.T("Pref.LoadAlongside"),
                [MessageBoxResult.No] = Loc.T("Pref.ReplaceProfiles")
            });
        return result switch
        {
            MessageBoxResult.Yes => SameModelProfileLoadChoice.Alongside,
            MessageBoxResult.No => SameModelProfileLoadChoice.Replace,
            _ => SameModelProfileLoadChoice.Cancel
        };
    }
}
