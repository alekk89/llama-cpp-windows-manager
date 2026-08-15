using System.Windows;
using System.Windows.Controls;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfMessageBox = System.Windows.MessageBox;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public static partial class ModelGroupDialogFactory
{
    private static ModelGroupEditorRow? ShowGroupEditor(
        WpfWindow owner,
        ModelGroupEditorRow draft,
        IReadOnlyCollection<ModelGroupEditorRow> groups,
        bool isNew)
    {
        ModelGroupEditorRow? result = null;
        var title = isNew ? Loc.T("ModelGroups.NewTitle") : Loc.T("ModelGroups.EditTitle");
        var dialog = Dialog(owner, title, 440, 330);
        var layout = Layout();
        layout.Children.Add(Header(
            title,
            isNew
                ? Loc.T("ModelGroups.NewDescription")
                : Loc.T("ModelGroups.EditDescription")));
        var nameBox = new WpfTextBox
        {
            Text = draft.Name,
            MaxLength = 80,
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var retentionChoices = new[]
        {
            new EnumChoice<ModelGroupRetentionMode>(ModelGroupRetentionMode.Inherit, Loc.T("ModelGroups.Retention.Inherit")),
            new EnumChoice<ModelGroupRetentionMode>(ModelGroupRetentionMode.Pinned, Loc.T("ModelGroups.Retention.Pinned")),
            new EnumChoice<ModelGroupRetentionMode>(ModelGroupRetentionMode.IdleTimeout, Loc.T("ModelGroups.Retention.IdleTimeout"))
        };
        var retentionCombo = new WpfComboBox
        {
            ItemsSource = retentionChoices,
            SelectedValuePath = nameof(EnumChoice<ModelGroupRetentionMode>.Value),
            SelectedValue = draft.RetentionMode,
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0)
        };
        var idleBox = new WpfTextBox
        {
            Text = draft.IdleMinutes.ToString(System.Globalization.CultureInfo.InvariantCulture),
            MaxLength = 5,
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0),
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var priorityChoices = new[]
        {
            new EnumChoice<ModelGroupEvictionPriority>(ModelGroupEvictionPriority.Low, Loc.T("ModelGroups.Priority.Low")),
            new EnumChoice<ModelGroupEvictionPriority>(ModelGroupEvictionPriority.Normal, Loc.T("ModelGroups.Priority.Normal")),
            new EnumChoice<ModelGroupEvictionPriority>(ModelGroupEvictionPriority.High, Loc.T("ModelGroups.Priority.High"))
        };
        var priorityCombo = new WpfComboBox
        {
            ItemsSource = priorityChoices,
            SelectedValuePath = nameof(EnumChoice<ModelGroupEvictionPriority>.Value),
            SelectedValue = draft.EvictionPriority,
            Height = 32,
            MinHeight = 32,
            Padding = new Thickness(9, 3, 9, 3),
            Margin = new Thickness(0)
        };
        void UpdateIdleAvailability()
            => idleBox.IsEnabled = retentionCombo.SelectedValue is ModelGroupRetentionMode.IdleTimeout;
        retentionCombo.SelectionChanged += (_, _) => UpdateIdleAvailability();
        UpdateIdleAvailability();

        var fields = EditorFields(
            (Loc.T("Runtimes.CustomRepo.NameLabel"), nameBox),
            (Loc.T("ModelGroups.Column.Policy"), retentionCombo),
            (Loc.T("ModelGroups.Column.IdleMinutes"), idleBox),
            (Loc.T("ModelGroups.Column.Priority"), priorityCombo));
        Grid.SetRow(fields, 1);
        layout.Children.Add(fields);

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
            var proposedName = nameBox.Text.Trim();
            var error = ValidateProposedName(groups, proposedName, isNew ? "" : draft.EditorKey);
            var idleMinutes = draft.IdleMinutes;
            if (error is null && !int.TryParse(
                    idleBox.Text.Trim(),
                    System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out idleMinutes))
                error = Loc.T("ModelGroups.Validation.IdleWholeNumber");
            else if (error is null && idleMinutes is < ModelGroupService.MinimumIdleMinutes or > ModelGroupService.MaximumIdleMinutes)
                error = Loc.T("ModelGroups.Validation.IdleRange", ModelGroupService.MinimumIdleMinutes, ModelGroupService.MaximumIdleMinutes);
            if (error is not null)
            {
                WpfMessageBox.Show(dialog, error, title, MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            result = Clone(draft);
            result.Name = proposedName;
            result.RetentionMode = retentionCombo.SelectedValue is ModelGroupRetentionMode retention
                ? retention
                : ModelGroupRetentionMode.Inherit;
            result.IdleMinutes = idleMinutes;
            result.EvictionPriority = priorityCombo.SelectedValue is ModelGroupEvictionPriority priority
                ? priority
                : ModelGroupEvictionPriority.Normal;
            dialog.DialogResult = true;
        };
        footer.Children.Add(save);
        footer.Children.Add(cancel);
        Grid.SetRow(footer, 2);
        layout.Children.Add(footer);
        dialog.Content = Frame(layout);
        dialog.ContentRendered += (_, _) =>
        {
            nameBox.Focus();
            nameBox.SelectAll();
        };
        dialog.ShowDialog();
        return result;
    }
}
