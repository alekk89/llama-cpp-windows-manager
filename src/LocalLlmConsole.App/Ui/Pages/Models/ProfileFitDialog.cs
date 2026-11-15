using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LocalLlmConsole.Models;

namespace LocalLlmConsole;

public sealed record ProfileFitDialogInput(int DesiredMaximumContext, int MinimumContext, int ReservedVramMiB);

public enum ProfileFitPreviewAction
{
    Cancel,
    SaveAsNewProfile,
    ApplyTemporarily,
    SaveAndBenchmark
}

public static class ProfileFitDialog
{
    public static ProfileFitDialogInput? ShowInput(Window owner, int currentContext)
    {
        var maximum = Box(currentContext.ToString(CultureInfo.InvariantCulture));
        var minimum = Box(Math.Min(currentContext, 32_768).ToString(CultureInfo.InvariantCulture));
        var reserve = Box("1536");
        var window = Window(owner, Loc.T("ProfileFit.FitTitle"), 500, 330);
        var panel = Panel();
        panel.Children.Add(Description(Loc.T("ProfileFit.FitDescription")));
        panel.Children.Add(Field(Loc.T("ProfileFit.MaximumContext"), maximum));
        panel.Children.Add(Field(Loc.T("ProfileFit.MinimumContext"), minimum));
        panel.Children.Add(Field(Loc.T("ProfileFit.VramReserve"), reserve));
        var buttons = Buttons();
        var accepted = false;
        buttons.Children.Add(ActionButton(Loc.T("ProfileFit.FitAndReview"), true, () => { accepted = true; window.DialogResult = true; }));
        buttons.Children.Add(ActionButton(Loc.T("Common.Cancel"), false, () => window.DialogResult = false));
        panel.Children.Add(buttons);
        window.Content = panel;
        if (window.ShowDialog() != true || !accepted) return null;
        if (!int.TryParse(maximum.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var max) || max < 1
            || !int.TryParse(minimum.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var min) || min < 1
            || !int.TryParse(reserve.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var reserveMiB) || reserveMiB < 0
            || max < min)
            throw new InvalidOperationException(Loc.T("ProfileFit.InvalidInput"));
        return new ProfileFitDialogInput(max, min, reserveMiB);
    }

    public static ProfileFitPreviewAction ShowPreview(
        Window owner,
        ModelLaunchSettings current,
        ProfileFitResult result)
    {
        if (!result.Success || result.Proposal is null) throw new InvalidOperationException(result.Error);
        var proposed = result.Proposal;
        var window = Window(owner, Loc.T("ProfileFit.ReviewTitle"), 720, 610);
        var root = Panel();
        root.Children.Add(Description(Loc.T("ProfileFit.ReviewDescription")));
        var table = new Grid { Margin = new Thickness(0, 10, 0, 8) };
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.6, GridUnitType.Star) });
        AddRow(table, 0, Loc.T("ProfileFit.Column.Setting"), Loc.T("ProfileFit.Column.Current"), Loc.T("ProfileFit.Column.Proposed"), true);
        AddRow(table, 1, Loc.T("ProfileFit.Row.Context"), current.ContextSize.ToString("N0"), proposed.ContextSize.ToString("N0"));
        AddRow(table, 2, Loc.T("ProfileFit.Row.GpuLayers"), current.GpuLayers.ToString(CultureInfo.InvariantCulture), proposed.GpuLayers.ToString(CultureInfo.InvariantCulture));
        AddRow(table, 3, Loc.T("ProfileFit.Row.TensorSplit"), Empty(current.GpuSplit), Empty(proposed.GpuSplit));
        AddRow(table, 4, Loc.T("ProfileFit.Row.TensorBufferOverrides"), Empty(current.TensorBufferOverrides), Empty(proposed.TensorBufferOverrides));
        root.Children.Add(table);
        if (result.DeviceEstimates.Count > 0)
        {
            root.Children.Add(Heading(Loc.T("ProfileFit.EstimatedMemory")));
            foreach (var device in result.DeviceEstimates)
                root.Children.Add(Description(Loc.T("ProfileFit.DeviceEstimate", device.Device, device.UsedMiB, device.FreeMiB)));
        }
        if (result.Warnings.Count > 0)
        {
            root.Children.Add(Heading(Loc.T("ProfileFit.Notes")));
            foreach (var warning in result.Warnings) root.Children.Add(Description($"• {warning}"));
        }
        var action = ProfileFitPreviewAction.Cancel;
        var buttons = Buttons();
        buttons.Children.Add(ActionButton(Loc.T("ProfileFit.SaveAsNewProfile"), true, () => Close(ProfileFitPreviewAction.SaveAsNewProfile)));
        buttons.Children.Add(ActionButton(Loc.T("ProfileFit.ApplyTemporarily"), false, () => Close(ProfileFitPreviewAction.ApplyTemporarily)));
        buttons.Children.Add(ActionButton(Loc.T("ProfileFit.SaveAndBenchmark"), false, () => Close(ProfileFitPreviewAction.SaveAndBenchmark)));
        buttons.Children.Add(ActionButton(Loc.T("Common.Cancel"), false, () => Close(ProfileFitPreviewAction.Cancel)));
        root.Children.Add(buttons);
        window.Content = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        window.ShowDialog();
        return action;

        void Close(ProfileFitPreviewAction selected)
        {
            action = selected;
            window.DialogResult = selected != ProfileFitPreviewAction.Cancel;
        }
    }

    private static System.Windows.Window Window(System.Windows.Window owner, string title, double width, double height)
        => new()
        {
            Owner = owner,
            Title = title,
            Width = width,
            Height = height,
            MinWidth = Math.Min(width, 460),
            MinHeight = Math.Min(height, 300),
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false
        };

    private static StackPanel Panel() => new() { Margin = new Thickness(22) };
    private static WrapPanel Buttons() => new() { Margin = new Thickness(0, 18, 0, 0), HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
    private static System.Windows.Controls.TextBox Box(string text) => new() { Text = text, MinWidth = 180, Height = 29, Padding = new Thickness(7, 2, 7, 2) };
    private static TextBlock Description(string text) => new() { Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 4) };
    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 4) };
    private static FrameworkElement Field(string label, System.Windows.Controls.Control editor)
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(editor, 1);
        grid.Children.Add(editor);
        return grid;
    }

    private static System.Windows.Controls.Button ActionButton(string text, bool isDefault, Action action)
    {
        var button = new System.Windows.Controls.Button { Content = text, IsDefault = isDefault, MinWidth = 104, Height = 30, Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(10, 2, 10, 2) };
        button.Click += (_, _) => action();
        return button;
    }

    private static void AddRow(Grid grid, int row, string setting, string current, string proposed, bool heading = false)
    {
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var values = new[] { setting, current, proposed };
        for (var column = 0; column < values.Length; column++)
        {
            var text = new TextBlock
            {
                Text = values[column],
                FontWeight = heading ? FontWeights.SemiBold : FontWeights.Normal,
                TextWrapping = TextWrapping.Wrap,
                Padding = new Thickness(7),
                Background = row % 2 == 0 ? System.Windows.Media.Brushes.Transparent : new SolidColorBrush(System.Windows.Media.Color.FromArgb(18, 128, 128, 128))
            };
            Grid.SetRow(text, row);
            Grid.SetColumn(text, column);
            grid.Children.Add(text);
        }
    }

    private static string Empty(string? value) => string.IsNullOrWhiteSpace(value) ? Loc.T("ProfileFit.None") : value;
}
