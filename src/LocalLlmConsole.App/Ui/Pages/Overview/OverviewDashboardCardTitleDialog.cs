using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

internal static class OverviewDashboardCardTitleDialog
{
    public static string? Show(string currentTitle)
    {
        var result = (string?)null;
        var window = DialogWindow();
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(Header(window));
        var field = new StackPanel { Margin = new Thickness(0, 14, 0, 14) };
        field.Children.Add(new TextBlock
        {
            Text = Loc.T("Dashboard.CardTitleLabel"),
            Foreground = ResourceBrush("TextSoft"),
            Margin = new Thickness(0, 0, 0, 6)
        });
        var title = new WpfTextBox
        {
            Text = currentTitle ?? "",
            MaxLength = OverviewDashboardLayoutPolicy.MaximumCardTitleLength,
            MinHeight = 30,
            ToolTip = Loc.T("Dashboard.CardTitleTooltip")
        };
        field.Children.Add(title);
        Grid.SetRow(field, 1);
        root.Children.Add(field);

        var actions = new Grid { Width = 280, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        actions.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new WpfButton { Content = Loc.T("Common.Cancel"), MinHeight = 30, IsCancel = true };
        var save = new WpfButton { Content = Loc.T("Common.Save"), MinHeight = 30, IsDefault = true };
        VisualRole.SetButtonRole(save, VisualRole.Primary);
        cancel.Click += (_, _) => window.DialogResult = false;
        save.Click += (_, _) =>
        {
            result = title.Text;
            window.DialogResult = true;
        };
        actions.Children.Add(cancel);
        Grid.SetColumn(save, 2);
        actions.Children.Add(save);
        Grid.SetRow(actions, 2);
        root.Children.Add(actions);
        window.Content = Frame(root);
        window.Loaded += (_, _) =>
        {
            title.Focus();
            title.SelectAll();
        };
        return window.ShowDialog() == true ? result ?? "" : null;
    }

    private static Window DialogWindow()
    {
        var window = new Window
        {
            Title = Loc.T("Dashboard.CardTitleDialogTitle"),
            Width = 460,
            SizeToContent = SizeToContent.Height,
            MinWidth = 380,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            FlowDirection = WpfApplication.Current.MainWindow?.FlowDirection ?? System.Windows.FlowDirection.LeftToRight
        };
        if (WpfApplication.Current.MainWindow is { IsVisible: true } owner)
            window.Owner = owner;
        window.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) window.DialogResult = false;
        };
        return window;
    }

    private static Grid Header(Window window)
    {
        var header = new Grid { Cursor = System.Windows.Input.Cursors.SizeAll };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("Dashboard.CardTitleDialogTitle"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new WpfButton { Content = "×", MinWidth = 28, MinHeight = 26, Padding = new Thickness(2, 0, 2, 1) };
        System.Windows.Automation.AutomationProperties.SetName(close, Loc.T("Accessibility.CloseDialog"));
        close.ToolTip = Loc.T("Accessibility.CloseDialog");
        VisualRole.SetButtonRole(close, VisualRole.Quiet);
        close.Click += (_, _) => window.DialogResult = false;
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (VisualTreeTraversal.FindAncestor<WpfButton>(args.OriginalSource as DependencyObject) is null)
                window.DragMove();
        };
        return header;
    }

    private static Border Frame(Grid content)
        => new()
        {
            Background = ResourceBrush("PanelBack"),
            BorderBrush = ResourceBrush("PanelBorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 18,
                ShadowDepth = 3,
                Direction = 270,
                Opacity = .28,
                RenderingBias = RenderingBias.Quality
            },
            Child = content
        };

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];
}
