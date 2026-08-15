using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public static partial class ModelGroupDialogFactory
{
    public static ModelGroupManagerResult? ShowManager(
        WpfWindow owner,
        IReadOnlyList<ModelGroupRecord> groups,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        IReadOnlyDictionary<string, string> modelNames,
        IReadOnlyDictionary<string, ModelGroupAssignment> currentAssignments)
    {
        var rows = new ObservableCollection<ModelGroupEditorRow>(groups.Select(group => new ModelGroupEditorRow
        {
            EditorKey = group.Id,
            Id = group.Id,
            Name = group.Name,
            RetentionMode = group.RetentionMode,
            IdleMinutes = group.IdleMinutes,
            EvictionPriority = group.EvictionPriority
        }));
        var assignments = currentAssignments.ToDictionary(
            pair => pair.Key,
            pair => pair.Value.GroupId,
            StringComparer.OrdinalIgnoreCase);
        RefreshProfileCounts(rows, assignments);
        ModelGroupManagerResult? result = null;
        var dialog = Dialog(owner, Loc.T("ModelGroups.Title"), 780, 430);
        var layout = Layout();
        var grid = GroupGrid(rows);
        var add = Button(Loc.T("ModelGroups.NewGroup"));
        var edit = Button(Loc.T("ModelGroups.Edit"));
        var manageProfiles = Button(Loc.T("ModelGroups.ProfilesAction"));
        var remove = Button(Loc.T("ModelGroups.DeleteGroup"));
        edit.IsEnabled = false;
        remove.IsEnabled = false;
        manageProfiles.IsEnabled = false;
        grid.SelectionChanged += (_, _) =>
        {
            var hasSelection = grid.SelectedItem is ModelGroupEditorRow;
            edit.IsEnabled = hasSelection;
            remove.IsEnabled = hasSelection;
            manageProfiles.IsEnabled = hasSelection;
        };
        add.Click += (_, _) =>
        {
            var draft = new ModelGroupEditorRow
            {
                EditorKey = $"pending:{Guid.NewGuid():N}",
                Name = UniqueNewName(rows)
            };
            var created = ShowGroupEditor(dialog, draft, rows, isNew: true);
            if (created is null) return;
            rows.Add(created);
            grid.SelectedItem = created;
            grid.ScrollIntoView(created);
        };
        edit.Click += (_, _) =>
        {
            if (grid.SelectedItem is not ModelGroupEditorRow row) return;
            var edited = ShowGroupEditor(dialog, Clone(row), rows, isNew: false);
            if (edited is null) return;
            CopyValues(edited, row);
            grid.Items.Refresh();
        };
        remove.Click += (_, _) =>
        {
            if (grid.SelectedItem is ModelGroupEditorRow row)
            {
                foreach (var profileId in assignments.Where(pair => pair.Value.Equals(row.EditorKey, StringComparison.OrdinalIgnoreCase)).Select(pair => pair.Key).ToArray())
                    assignments.Remove(profileId);
                rows.Remove(row);
                RefreshProfileCounts(rows, assignments);
                grid.Items.Refresh();
            }
        };
        manageProfiles.Click += (_, _) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            if (grid.SelectedItem is not ModelGroupEditorRow target) return;
            var change = ShowProfilePicker(dialog, target, profiles, modelNames, rows, assignments);
            if (change is null) return;
            foreach (var profileId in change.ProfileIds)
            {
                if (change.Remove)
                {
                    if (assignments.GetValueOrDefault(profileId)?.Equals(target.EditorKey, StringComparison.OrdinalIgnoreCase) == true)
                        assignments.Remove(profileId);
                }
                else
                {
                    assignments[profileId] = target.EditorKey;
                }
            }
            RefreshProfileCounts(rows, assignments);
            grid.Items.Refresh();
        };
        grid.MouseDoubleClick += (_, _) =>
        {
            if (edit.IsEnabled)
                edit.RaiseEvent(new RoutedEventArgs(WpfButton.ClickEvent));
        };

        layout.Children.Add(CompactToolbar(Loc.T("ModelGroups.Title"), add, edit, manageProfiles, remove));

        var gridFrame = PageSectionFactory.GridFrame(grid);
        gridFrame.Margin = new Thickness(0, 6, 0, 6);
        Grid.SetRow(gridFrame, 1);
        layout.Children.Add(gridFrame);

        var footer = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var save = Button(Loc.T("Common.Save"), primary: true);
        var cancel = Button(Loc.T("Common.Cancel"));
        cancel.IsCancel = true;
        save.IsDefault = true;
        save.Click += (_, _) =>
        {
            grid.CommitEdit(DataGridEditingUnit.Cell, true);
            grid.CommitEdit(DataGridEditingUnit.Row, true);
            var error = Validate(rows);
            if (error is not null)
            {
                WpfMessageBox.Show(dialog, error, Loc.T("ModelGroups.ValidationTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            result = new ModelGroupManagerResult(
                rows.Select(Clone).ToArray(),
                new Dictionary<string, string>(assignments, StringComparer.OrdinalIgnoreCase));
            dialog.DialogResult = true;
        };
        footer.Children.Add(save);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);

        dialog.Content = Frame(layout);
        dialog.ShowDialog();
        return result;
    }
}
