using System.Windows;
using System.Windows.Controls;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public sealed record RuntimeCustomRepositoryDialogRequest(
    WpfWindow Owner,
    Func<RuntimeCustomRepositoryDraft, RuntimeCustomRepositoryResult> ValidateDraft,
    Action<WpfWindow, string> ShowValidationWarning);

public static class RuntimeCustomRepositoryDialogFactory
{
    public static RuntimeCustomRepositoryDraft? Show(RuntimeCustomRepositoryDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Owner);
        ArgumentNullException.ThrowIfNull(request.ValidateDraft);
        ArgumentNullException.ThrowIfNull(request.ShowValidationWarning);

        RuntimeCustomRepositoryDraft? result = null;
        var dialog = new WpfWindow
        {
            Title = Loc.T("Runtimes.CustomRepo.DialogTitle"),
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = request.Owner,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = true,
            ShowInTaskbar = false,
            MinWidth = 540
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        layout.Children.Add(new TextBlock
        {
            Text = Loc.T("Runtimes.CustomRepo.Heading"),
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            Margin = new Thickness(0, 0, 0, 14)
        });

        var nameBox = DialogTextBox(Loc.T("Runtimes.CustomRepo.NameTooltip"));
        var repoBox = DialogTextBox(Loc.T("Runtimes.CustomRepo.RepoTooltip"));
        var branchBox = DialogTextBox(Loc.T("Runtimes.CustomRepo.BranchTooltip"));
        var backendBox = new WpfComboBox
        {
            ItemsSource = RuntimeCustomRepositoryService.BackendOptions,
            SelectedIndex = 0,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
        };
        var fields = Fields(nameBox, repoBox, branchBox, backendBox);
        Grid.SetRow(fields, 1);
        layout.Children.Add(fields);

        var actions = Actions(dialog, request, nameBox, repoBox, branchBox, backendBox, draft => result = draft);
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);

        dialog.Content = new WpfBorder
        {
            Background = (WpfBrush)WpfApplication.Current.Resources["PanelBack"],
            BorderBrush = (WpfBrush)WpfApplication.Current.Resources["PanelBorderStrong"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Child = layout
        };
        dialog.ShowDialog();
        return result;
    }

    private static Grid Fields(params FrameworkElement[] editors)
    {
        var fields = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(118) });
        fields.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(390) });
        AddDialogRow(fields, 0, Loc.T("Runtimes.CustomRepo.NameLabel"), editors[0]);
        AddDialogRow(fields, 1, Loc.T("Runtimes.CustomRepo.RepoLabel"), editors[1]);
        AddDialogRow(fields, 2, Loc.T("Runtimes.CustomRepo.BranchLabel"), editors[2]);
        AddDialogRow(fields, 3, Loc.T("Runtimes.CustomRepo.BackendLabel"), editors[3]);
        return fields;
    }

    private static StackPanel Actions(
        WpfWindow dialog,
        RuntimeCustomRepositoryDialogRequest request,
        WpfTextBox nameBox,
        WpfTextBox repoBox,
        WpfTextBox branchBox,
        WpfComboBox backendBox,
        Action<RuntimeCustomRepositoryDraft> accept)
    {
        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        var addButton = new WpfButton { Content = Loc.T("Runtimes.CustomRepo.AddButton"), MinWidth = 90, IsDefault = true, Margin = new Thickness(7, 0, 0, 0) };
        var cancelButton = new WpfButton { Content = Loc.T("Runtimes.CustomRepo.CancelButton"), MinWidth = 90, IsCancel = true, Margin = new Thickness(7, 0, 0, 0) };
        VisualRole.SetButtonRole(addButton, VisualRole.Primary);
        SetButtonToolTip(addButton, Loc.T("Runtimes.CustomRepo.AddTooltip"));
        SetButtonToolTip(cancelButton, Loc.T("Runtimes.CustomRepo.CancelTooltip"));
        addButton.Click += (_, _) =>
        {
            var draft = new RuntimeCustomRepositoryDraft(
                nameBox.Text.Trim(),
                repoBox.Text.Trim(),
                branchBox.Text.Trim(),
                backendBox.SelectedItem?.ToString() ?? "");
            var validation = request.ValidateDraft(draft);
            if (!validation.Success)
            {
                request.ShowValidationWarning(dialog, validation.StatusMessage);
                return;
            }

            accept(draft);
            dialog.DialogResult = true;
        };
        actions.Children.Add(addButton);
        actions.Children.Add(cancelButton);
        return actions;
    }

    private static WpfTextBox DialogTextBox(string toolTip) => new()
    {
        ToolTip = toolTip,
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
    };

    private static void AddDialogRow(Grid grid, int row, string label, FrameworkElement editor)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var text = new TextBlock
        {
            Text = label,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 8)
        };
        Grid.SetRow(text, row);
        grid.Children.Add(text);

        editor.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(editor, row);
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
    }

    private static void SetButtonToolTip(WpfButton button, string text)
    {
        button.ToolTip = text;
        ToolTipService.SetShowOnDisabled(button, true);
    }
}
