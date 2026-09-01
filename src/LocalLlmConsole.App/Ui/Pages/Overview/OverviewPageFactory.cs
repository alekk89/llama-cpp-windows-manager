using LocalLlmConsole.ViewModels;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfBinding = System.Windows.Data.Binding;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed record OverviewPageActions(
    Func<Task> SelectModelSessionAsync,
    Func<Task> SelectLaunchProfileAsync,
    Func<Task> LoadSelectedModelAsync,
    Func<Task> SelectLoadedSessionRowAsync,
    Func<Task> InspectSelectedEndpointAsync,
    RoutedEventHandler InspectEndpointRowClick,
    RoutedEventHandler UnloadLoadedSessionRowClick,
    Func<OverviewDashboardLayout, Task> PersistDashboardLayoutAsync,
    Func<Func<Task>, Task> RunEventAsync,
    Func<Func<Task>, Task>? DispatchDashboardMenuActionAsync = null);

public sealed record OverviewPageRequest(
    MainWindowViewModel ViewModel,
    OverviewPageActions Actions,
    Action<DataGrid> ConfigureRuntimeMetricsGrid,
    OverviewDashboardLayout? DashboardLayout);

public sealed record OverviewPageControls(
    Grid Root,
    ScrollViewer Scroller,
    FrameworkElement ModelStatusSection,
    WpfComboBox ModelCombo,
    WpfComboBox LaunchProfileCombo,
    WpfButton LoadButton,
    DataGrid LoadedSessionsGrid,
    OverviewDashboardController DashboardController,
    Grid RuntimeLogSection,
    GridSplitter RuntimeSectionsSplitter,
    WpfTextBox RuntimeLogBox,
    Grid MetricsSection,
    DataGrid RuntimeMetricsGrid);

public static class OverviewPageFactory
{


    public static OverviewPageControls Create(OverviewPageRequest request)
    {
        var root = new Grid { Margin = new Thickness(16) };
        var scroller = new ScrollViewer
        {
            Content = root,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
            PanningMode = PanningMode.VerticalOnly,
            CanContentScroll = false
        };
        scroller.SizeChanged += (_, args) => root.MinHeight = Math.Max(0, args.NewSize.Height);
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(0) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.92, GridUnitType.Star), MinHeight = 130 });

        var modelSessionSection = Stack();
        var modelBar = ModelBar(request, out var modelCombo, out var launchProfileCombo, out var loadButton);
        modelSessionSection.Children.Add(modelBar);
        var loadedSessionsGrid = PageSectionFactory.GridFor(
            (Loc.T("Overview.SessionsCol.Model"), nameof(OverviewSessionRow.ModelName), 1.35),
            ("Profile", nameof(OverviewSessionRow.ProfileName), .8),
            (Loc.T("Overview.SessionsCol.Size"), nameof(OverviewSessionRow.Size), .62),
            (Loc.T("Overview.SessionsCol.State"), nameof(OverviewSessionRow.State), .8),
            (Loc.T("Overview.SessionsCol.Runtime"), nameof(OverviewSessionRow.Runtime), 1.7),
            (Loc.T("Overview.SessionsCol.Backend"), nameof(OverviewSessionRow.Backend), .85));
        loadedSessionsGrid.Columns.Insert(4, EndpointColumn(request.Actions.InspectEndpointRowClick));
        loadedSessionsGrid.ItemsSource = request.ViewModel.Overview.SessionRows;
        loadedSessionsGrid.SelectionChanged += async (_, _) => await request.Actions.SelectLoadedSessionRowAsync();
        loadedSessionsGrid.MouseDoubleClick += async (_, args) =>
        {
            if (VisualTreeTraversal.FindAncestor<WpfButton>(args.OriginalSource as DependencyObject) is not null
                || VisualTreeTraversal.FindAncestor<Hyperlink>(args.OriginalSource as DependencyObject) is not null)
                return;
            await request.Actions.InspectSelectedEndpointAsync();
        };
        loadedSessionsGrid.ToolTip = Loc.T("Overview.EndpointInspectionTooltip");
        PageSectionFactory.AddButtonColumn(
            loadedSessionsGrid,
            Loc.T("Common.ActionButton"),
            nameof(OverviewSessionRow.ActionLabel),
            nameof(OverviewSessionRow.CanUnload),
            request.Actions.UnloadLoadedSessionRowClick,
            .58,
            tooltipProvider: _ => Loc.T("Tooltip.Unload"),
            visualRole: VisualRole.Danger);
        modelSessionSection.Children.Add(PageSectionFactory.GridSection(Loc.T("Overview.LoadedSessionsTitle"), loadedSessionsGrid));
        Grid.SetRow(modelSessionSection, 0);
        root.Children.Add(modelSessionSection);

        var dashboardController = new OverviewDashboardController(
            request.DashboardLayout,
            new OverviewDashboardControllerActions(
                request.Actions.PersistDashboardLayoutAsync,
                request.Actions.RunEventAsync,
                request.Actions.DispatchDashboardMenuActionAsync));
        Grid.SetRow(dashboardController.Root, 1);
        root.Children.Add(dashboardController.Root);

        var runtimeLogBox = RuntimeLogBox();
        var runtimeLogSection = PageSectionFactory.FramedSection(Loc.T("Overview.LiveRuntimeLogTitle"), runtimeLogBox);
        Grid.SetRow(runtimeLogSection, 2);
        root.Children.Add(runtimeLogSection);
        var runtimeSectionsSplitter = PageSectionFactory.HorizontalGridSplitter(3);
        root.Children.Add(runtimeSectionsSplitter);

        var runtimeMetricsGrid = PageSectionFactory.GridFor(
            (Loc.T("Overview.MetricsCol.Metric"), nameof(RuntimeMetricRow.Name), 1.5),
            (Loc.T("Overview.MetricsCol.Labels"), nameof(RuntimeMetricRow.Labels), 2.2),
            (Loc.T("Overview.MetricsCol.Value"), nameof(RuntimeMetricRow.Value), .9),
            (Loc.T("Overview.MetricsCol.Type"), nameof(RuntimeMetricRow.Type), .7),
            (Loc.T("Overview.MetricsCol.Help"), nameof(RuntimeMetricRow.Help), 3));
        runtimeMetricsGrid.ItemsSource = request.ViewModel.RuntimeMetrics.Rows;
        runtimeMetricsGrid.VerticalAlignment = VerticalAlignment.Stretch;
        request.ConfigureRuntimeMetricsGrid(runtimeMetricsGrid);
        var metricsSection = PageSectionFactory.GridSection(Loc.T("Overview.RuntimeMetricsTitle"), runtimeMetricsGrid);
        Grid.SetRow(metricsSection, 4);
        root.Children.Add(metricsSection);

        return new OverviewPageControls(
            root,
            scroller,
            dashboardController.Root,
            modelCombo,
            launchProfileCombo,
            loadButton,
            loadedSessionsGrid,
            dashboardController,
            runtimeLogSection,
            runtimeSectionsSplitter,
            runtimeLogBox,
            metricsSection,
            runtimeMetricsGrid);
    }

    private static Grid ModelBar(
        OverviewPageRequest request,
        out WpfComboBox modelCombo,
        out WpfComboBox launchProfileCombo,
        out WpfButton loadButton)
    {
        var modelBar = new Grid { Margin = new Thickness(0, 0, 0, 10) };
        modelBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        modelBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modelBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
        modelBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        modelBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        modelBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        modelBar.ColumnDefinitions.Add(new ColumnDefinition());
        modelBar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var modelLabel = new TextBlock
        {
            Text = Loc.T("Overview.ModelLabel"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        modelBar.Children.Add(modelLabel);
        modelCombo = new SearchableComboBox
        {
            ItemsSource = request.ViewModel.Overview.ModelChoices,
            ItemTemplate = ModelNameTemplate(),
            SelectedValuePath = nameof(OverviewModelChoice.Id),
            SearchTextSelector = item => (item as OverviewModelChoice)?.DisplayName ?? "",
            FavoriteKeySelector = item => item is OverviewModelChoice { Kind: OverviewModelChoiceKind.Model } choice ? choice.Id : "",
            Width = 240,
            MinHeight = 30,
            Margin = new Thickness(0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            ToolTip = Loc.T("Tooltip.OverviewModelCombo")
        };
        TextSearch.SetTextPath(modelCombo, nameof(OverviewModelChoice.DisplayName));
        modelCombo.SelectionChanged += async (_, _) => await request.Actions.SelectModelSessionAsync();
        Grid.SetColumn(modelCombo, 1);
        modelBar.Children.Add(modelCombo);

        var profileLabel = new TextBlock
        {
            Text = Loc.T("ModelGroups.Column.LaunchProfile"),
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextSoft"],
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(profileLabel, 3);
        modelBar.Children.Add(profileLabel);

        launchProfileCombo = new SearchableComboBox
        {
            ItemsSource = request.ViewModel.Overview.LaunchProfileChoices,
            ItemTemplate = LaunchProfileNameTemplate(),
            SelectedValuePath = nameof(OverviewLaunchProfileChoice.Id),
            SearchTextSelector = item => (item as OverviewLaunchProfileChoice)?.Name ?? "",
            FavoriteKeySelector = item => (item as OverviewLaunchProfileChoice)?.Id ?? "",
            Width = 220,
            MinHeight = 30,
            Margin = new Thickness(0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            ToolTip = Loc.T("Overview.LaunchProfileTooltip")
        };
        TextSearch.SetTextPath(launchProfileCombo, nameof(OverviewLaunchProfileChoice.Name));
        launchProfileCombo.SelectionChanged += async (_, _) => await request.Actions.SelectLaunchProfileAsync();
        Grid.SetColumn(launchProfileCombo, 4);
        modelBar.Children.Add(launchProfileCombo);

        loadButton = Button(Loc.T("Overview.LoadButton"), request.Actions.LoadSelectedModelAsync, VisualRole.Primary);
        OverviewPageResponsiveCoordinator.ConfigureLoadButton(loadButton);
        Grid.SetColumn(loadButton, 6);
        modelBar.Children.Add(loadButton);

        OverviewPageResponsiveCoordinator.ConfigureModelBar(modelBar, modelLabel, modelCombo, profileLabel, launchProfileCombo, loadButton);

        return modelBar;
    }

    private static DataTemplate ModelNameTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(OverviewModelChoice.DisplayName)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataTemplate(typeof(OverviewModelChoice)) { VisualTree = text };
    }

    private static DataTemplate LaunchProfileNameTemplate()
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetBinding(TextBlock.TextProperty, new WpfBinding(nameof(OverviewLaunchProfileChoice.Name)));
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataTemplate(typeof(OverviewLaunchProfileChoice)) { VisualTree = text };
    }

    private static DataGridTemplateColumn EndpointColumn(RoutedEventHandler click)
    {
        var text = new FrameworkElementFactory(typeof(TextBlock));
        text.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        text.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        text.SetValue(FrameworkElement.MarginProperty, new Thickness(7, 2, 7, 2));
        text.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        var link = new FrameworkElementFactory(typeof(Hyperlink));
        link.SetBinding(Hyperlink.IsEnabledProperty, new WpfBinding(nameof(OverviewSessionRow.CanInspect)));
        link.SetBinding(FrameworkContentElement.TagProperty, new WpfBinding("."));
        link.SetResourceReference(TextElement.ForegroundProperty, "AccentBlue");
        link.SetValue(FrameworkContentElement.ToolTipProperty, "Inspect what this endpoint reports right now, including models, context, defaults, capabilities, and active slots when available.");
        link.AddHandler(Hyperlink.ClickEvent, click);
        var run = new FrameworkElementFactory(typeof(Run));
        run.SetBinding(Run.TextProperty, new WpfBinding(nameof(OverviewSessionRow.Endpoint)));
        link.AppendChild(run);
        text.AppendChild(link);
        return new DataGridTemplateColumn
        {
            Header = Loc.T("Overview.SessionsCol.ApiEndpoints"),
            Width = new DataGridLength(1.7, DataGridLengthUnitType.Star),
            MinWidth = 130,
            CanUserResize = true,
            CellTemplate = new DataTemplate { VisualTree = text }
        };
    }

    private static WpfTextBox RuntimeLogBox()
        => new()
        {
            IsReadOnly = true,
            IsUndoEnabled = false,
            Text = Loc.T("Overview.NoRuntimeLog"),
            BorderThickness = new Thickness(0),
            Margin = new Thickness(0),
            Height = 320,
            MaxLines = 24,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Segoe UI"),
            FontSize = 11,
            TextWrapping = TextWrapping.NoWrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

    private static WpfButton Button(string text, Func<Task> click, string visualRole = "")
    {
        var button = new WpfButton { Content = text };
        VisualRole.SetButtonRole(button, visualRole);
        button.ToolTip = string.Equals(text, Loc.T("Overview.LoadButton")) ? Loc.T("Tooltip.Load")
            : string.Equals(text, Loc.T("Overview.UnloadButton")) ? Loc.T("Tooltip.Unload")
            : Loc.T("Common.RunAction", text);
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += async (_, _) => await click();
        return button;
    }

    private static StackPanel Stack() => new();

    private static TextBlock Text(string text, int size = 13, bool bold = false, bool muted = false) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = muted ? (WpfBrush)WpfApplication.Current.Resources["TextMuted"] : (WpfBrush)WpfApplication.Current.Resources["TextMain"],
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, size >= 18 ? 10 : 0, 0, size >= 18 ? 10 : 8)
    };
}
