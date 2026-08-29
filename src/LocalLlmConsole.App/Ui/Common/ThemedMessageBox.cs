using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfColor = System.Windows.Media.Color;

namespace LocalLlmConsole;

public static class ThemedMessageBox
{
    public static MessageBoxResult Show(string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
        => Show(System.Windows.Application.Current?.MainWindow, message, title, buttons, image);

    public static MessageBoxResult Show(Window? owner, string message, string title, MessageBoxButton buttons = MessageBoxButton.OK, MessageBoxImage image = MessageBoxImage.None)
    {
        var dialog = new Window
        {
            Title = title,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            Owner = owner,
            Background = WpfBrushes.Transparent,
            AllowsTransparency = true,
            ShowInTaskbar = owner is null,
            MinWidth = 360,
            MaxWidth = 620,
            MaxHeight = DialogMaxHeight(owner),
            FlowDirection = owner?.FlowDirection ?? (Loc.IsRightToLeft(Loc.CurrentLanguage)
                ? System.Windows.FlowDirection.RightToLeft
                : System.Windows.FlowDirection.LeftToRight)
        };

        var root = new Border
        {
            Background = Brush("PanelBack"),
            BorderBrush = Brush("PanelBorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(18),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.38,
                Color = WpfColor.FromRgb(0, 0, 0)
            }
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        layout.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 15,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush("TextMain"),
            Margin = new Thickness(0, 0, 0, 12)
        });

        var body = new Grid { Margin = new Thickness(0, 0, 0, 18) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());

        var icon = new Border
        {
            Width = 34,
            Height = 34,
            CornerRadius = new CornerRadius(17),
            Background = IconBackground(image),
            Margin = new Thickness(0, 2, 12, 0),
            Child = new TextBlock
            {
                Text = IconText(image),
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brush("TextMain"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        body.Children.Add(icon);

        var messageText = new TextBlock
        {
            Text = message,
            FontSize = 13,
            LineHeight = 19,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextSoft"),
            MaxWidth = 520
        };
        var messageViewer = new ScrollViewer
        {
            Content = messageText,
            MaxHeight = DialogMessageMaxHeight(owner),
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetColumn(messageViewer, 1);
        body.Children.Add(messageViewer);
        Grid.SetRow(body, 1);
        layout.Children.Add(body);

        var actions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        Grid.SetRow(actions, 2);
        layout.Children.Add(actions);

        MessageBoxResult result = DefaultResult(buttons);
        foreach (var (label, value, isDefault) in ButtonSpecs(buttons))
        {
            var button = new WpfButton
            {
                Content = label,
                MinWidth = 86,
                Margin = new Thickness(7, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = value is MessageBoxResult.Cancel or MessageBoxResult.No
            };
            if (value is MessageBoxResult.OK or MessageBoxResult.Yes)
                VisualRole.SetButtonRole(button, VisualRole.Primary);
            button.ToolTip = DialogButtonToolTip(value);
            ToolTipService.SetShowOnDisabled(button, true);
            button.Click += (_, _) =>
            {
                result = value;
                dialog.DialogResult = true;
            };
            actions.Children.Add(button);
        }

        root.Child = layout;
        dialog.Content = root;
        dialog.PreviewKeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Escape) return;
            result = CancelResult(buttons);
            dialog.DialogResult = false;
        };

        dialog.ShowDialog();
        return result;
    }

    private static double DialogMaxHeight(Window? owner)
    {
        var workAreaHeight = SystemParameters.WorkArea.Height;
        if (owner is not null)
        {
            var handle = new System.Windows.Interop.WindowInteropHelper(owner).Handle;
            var workingArea = Forms.Screen.FromHandle(handle).WorkingArea;
            var source = PresentationSource.FromVisual(owner);
            if (source?.CompositionTarget is not null)
            {
                var top = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(workingArea.Left, workingArea.Top));
                var bottom = source.CompositionTarget.TransformFromDevice.Transform(new System.Windows.Point(workingArea.Right, workingArea.Bottom));
                workAreaHeight = Math.Max(360, bottom.Y - top.Y);
            }
        }

        return Math.Max(360, workAreaHeight - 80);
    }

    private static double DialogMessageMaxHeight(Window? owner)
        => Math.Max(160, DialogMaxHeight(owner) - 178);

    private static WpfBrush Brush(string key)
        => (WpfBrush)(System.Windows.Application.Current.TryFindResource(key) ?? WpfBrushes.White);

    private static WpfBrush IconBackground(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => Brush("ControlPressed"),
        MessageBoxImage.Warning => Brush("Warning"),
        MessageBoxImage.Question => Brush("AccentSoft"),
        MessageBoxImage.Information => Brush("InfoSoft"),
        _ => Brush("PanelBackAlt")
    };

    private static string IconText(MessageBoxImage image) => image switch
    {
        MessageBoxImage.Error => "X",
        MessageBoxImage.Warning => "!",
        MessageBoxImage.Question => "?",
        MessageBoxImage.Information => "i",
        _ => "i"
    };

    private static string DialogButtonToolTip(MessageBoxResult value) => value switch
    {
        MessageBoxResult.OK => Loc.T("Dialog.Accessibility.Ok"),
        MessageBoxResult.Yes => Loc.T("Dialog.Accessibility.Yes"),
        MessageBoxResult.No => Loc.T("Dialog.Accessibility.No"),
        MessageBoxResult.Cancel => Loc.T("Runtimes.CustomRepo.CancelTooltip"),
        _ => Loc.T("Dialog.Accessibility.Close")
    };

    private static MessageBoxResult DefaultResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => MessageBoxResult.Cancel,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        MessageBoxButton.YesNoCancel => MessageBoxResult.Cancel,
        _ => MessageBoxResult.OK
    };

    private static MessageBoxResult CancelResult(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OK => MessageBoxResult.OK,
        MessageBoxButton.YesNo => MessageBoxResult.No,
        _ => MessageBoxResult.Cancel
    };

    private static IEnumerable<(string Label, MessageBoxResult Result, bool IsDefault)> ButtonSpecs(MessageBoxButton buttons) => buttons switch
    {
        MessageBoxButton.OKCancel => [("OK", MessageBoxResult.OK, false), (Loc.T("Runtimes.ActionBtn.Cancel"), MessageBoxResult.Cancel, true)],
        MessageBoxButton.YesNo => [(Loc.T("Pref.Yes"), MessageBoxResult.Yes, false), (Loc.T("Pref.No"), MessageBoxResult.No, true)],
        MessageBoxButton.YesNoCancel => [(Loc.T("Pref.Yes"), MessageBoxResult.Yes, false), (Loc.T("Pref.No"), MessageBoxResult.No, false), (Loc.T("Runtimes.ActionBtn.Cancel"), MessageBoxResult.Cancel, true)],
        _ => [("OK", MessageBoxResult.OK, true)]
    };
}
