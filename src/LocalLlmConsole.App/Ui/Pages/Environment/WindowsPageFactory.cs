using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed record WindowsPageActions(
    Func<Task> RefreshAsync,
    Func<Task> InstallCpuToolsAsync,
    Func<Task> InstallCudaToolkitAsync,
    Func<Task> InstallVulkanToolsAsync,
    Func<Task> InstallSyclToolsAsync);

public sealed record WindowsPageRequest(
    MainWindowViewModel ViewModel,
    WindowsPageActions Actions,
    Func<string, string> ButtonToolTip);

public sealed record WindowsPageControls(
    DockPanel Root,
    Grid StatusMetric,
    Grid CpuMetric,
    Grid CudaMetric,
    Grid VulkanMetric,
    Grid SyclMetric,
    DataGrid ToolsGrid,
    WpfButton InstallCpuToolsButton,
    WpfButton InstallCudaToolkitButton,
    WpfButton InstallVulkanToolsButton,
    WpfButton InstallSyclToolsButton);

public static class WindowsPageFactory
{
    public static WindowsPageControls Create(WindowsPageRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.ViewModel);
        ArgumentNullException.ThrowIfNull(request.Actions);
        ArgumentNullException.ThrowIfNull(request.ButtonToolTip);

        var root = new DockPanel { Margin = new Thickness(16) };
        var toolbar = Bar();
        toolbar.Children.Add(Button(Loc.T("Environment.RefreshButton"), request.Actions.RefreshAsync, request.ButtonToolTip));
        DockPanel.SetDock(toolbar, Dock.Top);
        root.Children.Add(toolbar);

        var body = new StackPanel();
        body.Children.Add(SetupRows(request, out var installCpuToolsButton, out var installCudaToolkitButton, out var installVulkanToolsButton, out var installSyclToolsButton));

        var statusGrid = StatusGrid(out var statusMetric, out var cpuMetric, out var cudaMetric, out var vulkanMetric, out var syclMetric);
        body.Children.Add(statusGrid);

        body.Children.Add(Text(
            Loc.T("Windows.DescriptionText"),
            muted: true));

        var toolsGrid = ToolsGrid(request);
        body.Children.Add(PageSectionFactory.GridSection(Loc.T("Windows.ToolsSectionTitle"), toolsGrid));

        root.Children.Add(Scroll(body, new Thickness(16)));

        return new WindowsPageControls(
            root,
            statusMetric,
            cpuMetric,
            cudaMetric,
            vulkanMetric,
            syclMetric,
            toolsGrid,
            installCpuToolsButton,
            installCudaToolkitButton,
            installVulkanToolsButton,
            installSyclToolsButton);
    }

    private static UIElement SetupRows(
        WindowsPageRequest request,
        out WpfButton installCpuToolsButton,
        out WpfButton installCudaToolkitButton,
        out WpfButton installVulkanToolsButton,
        out WpfButton installSyclToolsButton)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
        panel.Children.Add(ToolActionRow(
            Loc.T("Windows.Tool.CpuLabel"),
            Loc.T("Windows.Tool.CpuDescription"),
            out installCpuToolsButton,
            Loc.T("Windows.Tool.InstallCpuButton"),
            request.Actions.InstallCpuToolsAsync));
        panel.Children.Add(ToolActionRow(
            Loc.T("Windows.Tool.CudaLabel"),
            Loc.T("Windows.Tool.CudaDescription"),
            out installCudaToolkitButton,
            Loc.T("Windows.Tool.InstallCudaButton"),
            request.Actions.InstallCudaToolkitAsync));
        panel.Children.Add(ToolActionRow(
            Loc.T("Windows.Tool.VulkanLabel"),
            Loc.T("Windows.Tool.VulkanDescription"),
            out installVulkanToolsButton,
            Loc.T("Windows.Tool.InstallVulkanButton"),
            request.Actions.InstallVulkanToolsAsync));
        panel.Children.Add(ToolActionRow(
            Loc.T("Windows.Tool.SyclLabel"),
            Loc.T("Windows.Tool.SyclDescription"),
            out installSyclToolsButton,
            Loc.T("Windows.Tool.InstallSyclButton"),
            request.Actions.InstallSyclToolsAsync));
        return panel;
    }

    private static Grid StatusGrid(out Grid statusMetric, out Grid cpuMetric, out Grid cudaMetric, out Grid vulkanMetric, out Grid syclMetric)
    {
        var statusGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition());
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition());
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        statusMetric = MetricCardFactory.AddMetric(statusGrid, Loc.T("Windows.StatusCard.WindowsStatus"), 0, 0);
        cpuMetric = MetricCardFactory.AddMetric(statusGrid, Loc.T("Windows.StatusCard.CpuBuild"), 0, 1);
        cudaMetric = MetricCardFactory.AddMetric(statusGrid, Loc.T("Windows.StatusCard.CudaBuild"), 1, 0);
        vulkanMetric = MetricCardFactory.AddMetric(statusGrid, Loc.T("Windows.StatusCard.VulkanBuild"), 1, 1);
        syclMetric = MetricCardFactory.AddMetric(statusGrid, Loc.T("Windows.StatusCard.SyclBuild"), 2, 0);
        return statusGrid;
    }

    private static DataGrid ToolsGrid(WindowsPageRequest request)
    {
        var grid = PageSectionFactory.GridFor(
            (Loc.T("Windows.Col.Toolchain"), nameof(WindowsToolRow.Toolchain), .75),
            (Loc.T("Windows.Col.Status"), nameof(WindowsToolRow.Status), .6),
            (Loc.T("Windows.Col.Details"), nameof(WindowsToolRow.Details), 2.8),
            (Loc.T("Windows.Col.Driver"), nameof(WindowsToolRow.Driver), 1.7));
        grid.ItemsSource = request.ViewModel.Windows.Rows;
        return grid;
    }

    private static Grid ToolActionRow(
        string label,
        string description,
        out WpfButton actionButton,
        string actionText,
        Func<Task> actionClick)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        });
        var descriptionText = new TextBlock
        {
            Text = description,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 0)
        };
        Grid.SetColumn(descriptionText, 1);
        row.Children.Add(descriptionText);
        actionButton = Button(actionText, actionClick, _ => Loc.T("Tooltip.InstallRepairOnWindows", label));
        VisualRole.SetButtonRole(actionButton, VisualRole.Primary);
        Grid.SetColumn(actionButton, 2);
        row.Children.Add(actionButton);
        return row;
    }

    private static ScrollViewer Scroll(UIElement child, Thickness padding)
    {
        var viewer = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var content = new Border { Padding = padding, Child = child };
        content.SetBinding(FrameworkElement.WidthProperty, new WpfBinding(nameof(ScrollViewer.ViewportWidth)) { Source = viewer });
        viewer.Content = content;
        viewer.Loaded += (_, _) => viewer.Dispatcher.BeginInvoke(new Action(viewer.ScrollToTop), System.Windows.Threading.DispatcherPriority.ContextIdle);
        return viewer;
    }

    private static WrapPanel Bar()
        => new() { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

    private static TextBlock Text(string text, bool muted = false) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = muted ? (WpfBrush)WpfApplication.Current.Resources["TextMuted"] : (WpfBrush)WpfApplication.Current.Resources["TextMain"],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 8)
    };

    private static WpfButton Button(string text, Func<Task> click, Func<string, string> toolTip)
    {
        var button = new WpfButton
        {
            Content = text,
            ToolTip = toolTip(text)
        };
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += async (_, _) => await click();
        return button;
    }
}
