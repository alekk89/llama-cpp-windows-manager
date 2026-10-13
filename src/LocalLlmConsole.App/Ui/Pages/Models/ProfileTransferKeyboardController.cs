using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace LocalLlmConsole;

public sealed class ProfileTransferKeyboardController
{
    private readonly DataGrid _models;
    private readonly DataGrid _profiles;
    private readonly ProfileTransferUiActions _actions;
    private bool _busy;

    public ProfileTransferKeyboardController(DataGrid models, DataGrid profiles, ProfileTransferUiActions actions)
    {
        _models = models; _profiles = profiles; _actions = actions;
    }

    public void Attach()
    {
        _models.PreviewKeyDown += async (_, args) => await HandleAsync(args.Key, Keyboard.Modifiers,
            args.OriginalSource, false, () => args.Handled = true);
        _profiles.PreviewKeyDown += async (_, args) => await HandleAsync(args.Key, Keyboard.Modifiers,
            args.OriginalSource, true, () => args.Handled = true);
    }

    public async Task<bool> HandleAsync(Key key, ModifierKeys modifiers, object? originalSource,
        bool fromProfiles, Action markHandled)
    {
        if (modifiers != ModifierKeys.Control || key is not (Key.C or Key.V) || IsEditingText(originalSource)) return false;
        var row = (fromProfiles ? _profiles.SelectedItem : _models.SelectedItem) as ModelGridRow;
        if (key == Key.V) row ??= (fromProfiles ? _models.SelectedItem : _profiles.SelectedItem) as ModelGridRow;
        if (row is null || key == Key.C && (!fromProfiles || row.LaunchProfile is null)) return false;
        // Consume before awaiting so DataGrid's built-in cell copy cannot replace the profile bundle.
        markHandled();
        if (_busy) return true;
        _busy = true;
        try
        {
            if (key == Key.C) await _actions.Export(row.Model, row.LaunchProfile!.Id, true);
            else await _actions.Import(row.Model, true);
        }
        finally { _busy = false; }
        return true;
    }

    private static bool IsEditingText(object? source)
    {
        for (var node = source as DependencyObject; node is not null;)
        {
            if (node is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox || node is System.Windows.Controls.ComboBox { IsEditable: true }) return true;
            node = node is FrameworkContentElement content ? content.Parent
                : node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);
        }
        return false;
    }
}
