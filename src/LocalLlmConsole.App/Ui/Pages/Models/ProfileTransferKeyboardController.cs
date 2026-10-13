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

    public void Attach(FrameworkElement page)
    {
        var bindings = new[] { Binding(ApplicationCommands.Copy, Key.C), Binding(ApplicationCommands.Paste, Key.V) };
        foreach (var binding in bindings) page.CommandBindings.Add(binding);
        Window? window = null;
        page.Loaded += (_, _) =>
        {
            window = Window.GetWindow(page);
            if (window is null) return;
            foreach (var binding in bindings) window.CommandBindings.Add(binding);
        };
        page.Unloaded += (_, _) =>
        {
            if (window is null) return;
            foreach (var binding in bindings) window.CommandBindings.Remove(binding);
            window = null;
        };
    }

    private CommandBinding Binding(RoutedCommand command, Key key)
    {
        var binding = new CommandBinding(command);
        void CanExecute(object sender, CanExecuteRoutedEventArgs args)
        {
            if (SelectedRow(key, args.OriginalSource, IsWithin(args.OriginalSource, _profiles)) is null) return;
            args.CanExecute = true; args.Handled = true;
        }
        binding.PreviewCanExecute += CanExecute;
        binding.CanExecute += CanExecute;
        binding.PreviewExecuted += async (_, args) => await HandleAsync(key, ModifierKeys.Control,
            args.OriginalSource, IsWithin(args.OriginalSource, _profiles), () => args.Handled = true);
        return binding;
    }

    public async Task<bool> HandleAsync(Key key, ModifierKeys modifiers, object? originalSource,
        bool fromProfiles, Action markHandled)
    {
        if (modifiers != ModifierKeys.Control || key is not (Key.C or Key.V)) return false;
        var row = SelectedRow(key, originalSource, fromProfiles);
        if (row is null) return false;
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

    private ModelGridRow? SelectedRow(Key key, object? source, bool fromProfiles)
    {
        if (IsEditingText(source)) return null;
        if (key == Key.C) return fromProfiles && _profiles.SelectedItem is ModelGridRow { LaunchProfile: not null } profile ? profile : null;
        // Model selection remains authoritative when the profile list is empty or rebuilding.
        return _models.SelectedItem as ModelGridRow ?? _profiles.SelectedItem as ModelGridRow;
    }

    private static bool IsWithin(object? source, DependencyObject ancestor)
    {
        for (var node = source as DependencyObject; node is not null; node = Parent(node))
            if (ReferenceEquals(node, ancestor)) return true;
        return false;
    }

    private static DependencyObject? Parent(DependencyObject node)
        => node is FrameworkContentElement content ? content.Parent
            : node is Visual ? VisualTreeHelper.GetParent(node) : LogicalTreeHelper.GetParent(node);

    private static bool IsEditingText(object? source)
    {
        for (var node = source as DependencyObject; node is not null; node = Parent(node))
        {
            if (node is System.Windows.Controls.Primitives.TextBoxBase or PasswordBox || node is System.Windows.Controls.ComboBox { IsEditable: true }) return true;
        }
        return false;
    }
}
