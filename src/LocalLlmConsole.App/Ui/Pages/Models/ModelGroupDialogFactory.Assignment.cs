using System.Windows;
using System.Windows.Controls;
using WpfBinding = System.Windows.Data.Binding;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public static partial class ModelGroupDialogFactory
{
    public static string? ShowAssignment(
        WpfWindow owner,
        ModelRecord model,
        NamedModelLaunchProfile profile,
        IReadOnlyList<ModelGroupRecord> groups,
        string currentGroupId,
        out bool accepted)
    {
        accepted = false;
        var acceptedResult = false;
        string? result = null;
        var dialog = Dialog(owner, Loc.T("ModelGroups.AssignTitle"), 520, 230);
        var layout = Layout();
        layout.Children.Add(Header(
            Loc.T("ModelGroups.AssignHeading", model.Name, profile.Name),
            Loc.T("ModelGroups.AssignDescription")));
        var choices = new[] { new AssignmentChoice("", Loc.T("ModelGroups.Ungrouped")) }
            .Concat(groups.Select(group => new AssignmentChoice(group.Id, group.Name)))
            .ToArray();
        var combo = new WpfComboBox
        {
            ItemsSource = choices,
            SelectedItem = choices.FirstOrDefault(choice => choice.Id.Equals(currentGroupId, StringComparison.OrdinalIgnoreCase)) ?? choices[0],
            MinWidth = 360,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch
        };
        Grid.SetRow(combo, 1);
        layout.Children.Add(combo);

        var footer = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        var save = Button(Loc.T("Common.Save"), primary: true);
        var cancel = Button(Loc.T("Common.Cancel"));
        save.IsDefault = true;
        cancel.IsCancel = true;
        save.Click += (_, _) =>
        {
            result = (combo.SelectedItem as AssignmentChoice)?.Id ?? "";
            acceptedResult = true;
            dialog.DialogResult = true;
        };
        footer.Children.Add(save);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        dialog.Content = Frame(layout);
        dialog.ShowDialog();
        accepted = acceptedResult;
        return result;
    }

    private static ProfileMembershipChange? ShowProfilePicker(
        WpfWindow owner,
        ModelGroupEditorRow target,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        IReadOnlyDictionary<string, string> modelNames,
        IReadOnlyList<ModelGroupEditorRow> groups,
        IReadOnlyDictionary<string, string> assignments)
    {
        ProfileMembershipChange? result = null;
        var dialog = Dialog(owner, Loc.T("ModelGroups.ManageProfilesTitle"), 720, 430);
        var layout = Layout();
        layout.Children.Add(Header(
            Loc.T("ModelGroups.ProfilesIn", target.Name),
            Loc.T("ModelGroups.ProfilesDescription")));

        var groupNames = groups.ToDictionary(group => group.EditorKey, group => group.Name, StringComparer.OrdinalIgnoreCase);
        var choices = profiles
            .OrderBy(profile => modelNames.GetValueOrDefault(profile.ModelId), StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.Name, StringComparer.OrdinalIgnoreCase)
            .Select(profile => new LaunchProfileGroupChoice(
                profile.Id,
                modelNames.GetValueOrDefault(profile.ModelId) ?? profile.ModelId,
                profile.Name,
                assignments.TryGetValue(profile.Id, out var groupKey)
                    ? groupNames.GetValueOrDefault(groupKey) ?? Loc.T("ModelGroups.Ungrouped")
                    : Loc.T("ModelGroups.Ungrouped"),
                assignments.GetValueOrDefault(profile.Id)?.Equals(target.EditorKey, StringComparison.OrdinalIgnoreCase) == true))
            .ToArray();
        var grid = new DataGrid
        {
            ItemsSource = choices,
            AutoGenerateColumns = false,
            CanUserAddRows = false,
            CanUserDeleteRows = false,
            IsReadOnly = true,
            SelectionMode = DataGridSelectionMode.Extended,
            SelectionUnit = DataGridSelectionUnit.FullRow,
            MinHeight = 240
        };
        PageSectionFactory.PolishGrid(grid);
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("Overview.SessionsCol.Model"),
            Binding = new WpfBinding(nameof(LaunchProfileGroupChoice.ModelName)),
            Width = new DataGridLength(1.35, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("ModelGroups.Column.LaunchProfile"),
            Binding = new WpfBinding(nameof(LaunchProfileGroupChoice.ProfileName)),
            Width = new DataGridLength(1.15, DataGridLengthUnitType.Star)
        });
        grid.Columns.Add(new DataGridTextColumn
        {
            Header = Loc.T("ModelGroups.Column.CurrentGroup"),
            Binding = new WpfBinding(nameof(LaunchProfileGroupChoice.CurrentGroup)),
            Width = new DataGridLength(1, DataGridLengthUnitType.Star)
        });
        Grid.SetRow(grid, 1);
        layout.Children.Add(grid);

        var footer = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = WpfHorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0)
        };
        var add = Button(Loc.T("ModelGroups.AddSelected"), primary: true);
        var remove = Button(Loc.T("ModelGroups.RemoveSelected"));
        var cancel = Button(Loc.T("Common.Cancel"));
        add.IsEnabled = false;
        remove.IsEnabled = false;
        grid.SelectionChanged += (_, _) =>
        {
            var selected = grid.SelectedItems.Cast<LaunchProfileGroupChoice>().ToArray();
            add.IsEnabled = selected.Length > 0;
            remove.IsEnabled = selected.Any(choice => choice.IsInSelectedGroup);
        };
        cancel.IsCancel = true;
        add.Click += (_, _) =>
        {
            var selected = grid.SelectedItems.Cast<LaunchProfileGroupChoice>().ToArray();
            if (selected.Length == 0)
            {
                WpfMessageBox.Show(dialog, Loc.T("ModelGroups.SelectProfile"), Loc.T("ModelGroups.Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            result = new ProfileMembershipChange(selected.Select(choice => choice.ProfileId).ToArray(), Remove: false);
            dialog.DialogResult = true;
        };
        remove.Click += (_, _) =>
        {
            var selected = grid.SelectedItems.Cast<LaunchProfileGroupChoice>()
                .Where(choice => choice.IsInSelectedGroup)
                .ToArray();
            if (selected.Length == 0) return;
            result = new ProfileMembershipChange(selected.Select(choice => choice.ProfileId).ToArray(), Remove: true);
            dialog.DialogResult = true;
        };
        footer.Children.Add(add);
        footer.Children.Add(remove);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        dialog.Content = Frame(layout);
        dialog.ShowDialog();
        return result;
    }
}
