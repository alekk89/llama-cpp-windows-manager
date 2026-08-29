using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

internal static class OverviewDashboardMetricPicker
{
    public static IReadOnlyList<string> Show(
        string title,
        IReadOnlyList<OverviewDashboardMetricDefinition> definitions,
        IEnumerable<string>? excluded = null,
        bool multiple = true)
    {
        var excludedIds = (excluded ?? []).ToHashSet(StringComparer.Ordinal);
        var available = definitions.Where(item => !excludedIds.Contains(item.Id)).ToArray();
        var filtered = new ObservableCollection<OverviewDashboardMetricDefinition>(available);
        var window = DialogWindow(title);
        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition());
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        root.Children.Add(DialogHeader(window, title));

        var search = new WpfTextBox
        {
            MinHeight = 30,
            Margin = new Thickness(0, 12, 0, 10),
            ToolTip = Loc.T("Dashboard.MetricSearchTooltip")
        };
        System.Windows.Automation.AutomationProperties.SetName(search, Loc.T("Dashboard.MetricSearchTooltip"));
        Grid.SetRow(search, 1);
        root.Children.Add(search);
        var list = new System.Windows.Controls.ListBox
        {
            ItemsSource = filtered,
            SelectionMode = multiple ? System.Windows.Controls.SelectionMode.Multiple : System.Windows.Controls.SelectionMode.Single,
            DisplayMemberPath = nameof(OverviewDashboardMetricDefinition.DisplayName),
            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
            ItemContainerStyle = MetricItemStyle()
        };
        Grid.SetRow(list, 2);
        root.Children.Add(list);

        var buttons = new Grid
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 12, 0, 0),
            Width = 280
        };
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition());
        var cancel = new WpfButton
        {
            Content = Loc.T("Common.Cancel"),
            MinHeight = 30,
            IsCancel = true
        };
        var add = new WpfButton
        {
            Content = Loc.T("Dashboard.AddSelected"),
            MinHeight = 30,
            IsDefault = true,
            IsEnabled = false
        };
        VisualRole.SetButtonRole(add, VisualRole.Primary);
        cancel.Click += (_, _) => window.DialogResult = false;
        add.Click += (_, _) => window.DialogResult = true;
        buttons.Children.Add(cancel);
        Grid.SetColumn(add, 2);
        buttons.Children.Add(add);
        Grid.SetRow(buttons, 3);
        root.Children.Add(buttons);
        window.Content = DialogFrame(root);

        search.TextChanged += (_, _) => ReplaceFiltered(filtered, available, search.Text);
        list.SelectionChanged += (_, _) => add.IsEnabled = list.SelectedItems.Count > 0;
        list.MouseDoubleClick += (_, _) => window.DialogResult = list.SelectedItem is not null;
        window.Loaded += (_, _) => search.Focus();
        if (window.ShowDialog() != true) return [];
        return list.SelectedItems.Cast<OverviewDashboardMetricDefinition>().Select(item => item.Id).ToArray();
    }

    private static void ReplaceFiltered(
        ObservableCollection<OverviewDashboardMetricDefinition> target,
        IReadOnlyList<OverviewDashboardMetricDefinition> definitions,
        string query)
    {
        var words = (query ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var matches = definitions.Where(definition => words.All(word =>
            definition.DisplayName.Contains(word, StringComparison.CurrentCultureIgnoreCase)
            || definition.Category.Contains(word, StringComparison.CurrentCultureIgnoreCase)
            || definition.Tooltip.Contains(word, StringComparison.CurrentCultureIgnoreCase)));
        target.Clear();
        foreach (var definition in matches) target.Add(definition);
    }

    private static Window DialogWindow(string title)
    {
        var window = new Window
        {
            Title = title,
            Width = 560,
            Height = 520,
            MinWidth = 420,
            MinHeight = 360,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
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

    private static WpfBorder DialogFrame(Grid content)
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

    private static Grid DialogHeader(Window window, string title)
    {
        var header = new Grid { Cursor = System.Windows.Input.Cursors.SizeAll };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain"),
            VerticalAlignment = VerticalAlignment.Center
        });
        var close = new WpfButton
        {
            Content = "×",
            MinWidth = 28,
            MinHeight = 26,
            Padding = new Thickness(2, 0, 2, 1)
        };
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

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];

    private static Style MetricItemStyle()
    {
        var style = new Style(typeof(ListBoxItem), WpfApplication.Current.TryFindResource(typeof(ListBoxItem)) as Style);
        style.Setters.Add(new Setter(FrameworkElement.ToolTipProperty,
            new System.Windows.Data.Binding(nameof(OverviewDashboardMetricDefinition.Tooltip))));
        style.Setters.Add(new Setter(ToolTipService.ShowDurationProperty, 20000));
        return style;
    }
}
