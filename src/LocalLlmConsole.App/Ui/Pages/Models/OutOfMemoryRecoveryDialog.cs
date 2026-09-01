using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

public enum OutOfMemoryRecoveryAction
{
    Close,
    CreateFittedProfile,
    EditMemorySettings,
    ViewLog
}

public static class OutOfMemoryRecoveryDialog
{
    public static OutOfMemoryRecoveryAction Show(Window owner, string modelName)
    {
        var result = OutOfMemoryRecoveryAction.Close;
        var window = new Window
        {
            Owner = owner,
            Title = Loc.T("ProfileFit.OomTitle"),
            Width = 570,
            Height = 245,
            MinWidth = 500,
            MinHeight = 220,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false
        };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock
        {
            Text = string.Format(Loc.FormatCulture, Loc.T("ProfileFit.OomMessage"), modelName),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        panel.Children.Add(new TextBlock
        {
            Text = Loc.T("ProfileFit.OomDescription"),
            Margin = new Thickness(0, 10, 0, 18),
            TextWrapping = TextWrapping.Wrap
        });
        var buttons = new WrapPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        buttons.Children.Add(Button("Create fitted profile", true, OutOfMemoryRecoveryAction.CreateFittedProfile));
        buttons.Children.Add(Button("Edit memory settings", false, OutOfMemoryRecoveryAction.EditMemorySettings));
        buttons.Children.Add(Button("View log", false, OutOfMemoryRecoveryAction.ViewLog));
        buttons.Children.Add(Button("Close", false, OutOfMemoryRecoveryAction.Close));
        panel.Children.Add(buttons);
        window.Content = panel;
        window.ShowDialog();
        return result;

        System.Windows.Controls.Button Button(string text, bool isDefault, OutOfMemoryRecoveryAction action)
        {
            var button = new System.Windows.Controls.Button { Content = text, IsDefault = isDefault, MinWidth = 92, Height = 31, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(9, 2, 9, 2) };
            button.Click += (_, _) => { result = action; window.DialogResult = action != OutOfMemoryRecoveryAction.Close; };
            return button;
        }
    }
}
