using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

public sealed record RuntimesPageActions(
    Func<Task> ChooseRuntimeFolderAsync,
    Func<Task> ChangeCudaPackagePreferenceAsync,
    Func<RuntimeRecord, Task> ToggleRuntimeFavoriteAsync,
    RoutedEventHandler VerifyRuntimeRowClick,
    RoutedEventHandler DeleteRuntimeRowClick,
    RoutedEventHandler RuntimeSourceRowClick,
    RoutedEventHandler InstallRuntimePackageRowClick,
    RoutedEventHandler CheckRuntimePackageUpdateRowClick,
    RoutedEventHandler DeleteRuntimePackageRowClick,
    Action<DataGrid> ConfigureRuntimeGridColumnSizing,
    Action<DataGrid> ConfigureRuntimeBuildGridColumnSizing);

public sealed record RuntimesPageRequest(
    MainWindowViewModel ViewModel,
    string RuntimeRoot,
    string CudaPackagePreference,
    RuntimesPageActions Actions);

public sealed record RuntimesPageControls(
    Grid Root,
    TextBlock RuntimesFolderText,
    DataGrid RuntimeGrid,
    DataGridSearchControls RuntimeSearch,
    DataGrid RuntimePackageGrid,
    WpfComboBox RuntimeCudaPreferenceCombo);

public static class RuntimesPageFactory
{
    public static RuntimesPageControls Create(RuntimesPageRequest request)
    {
        var root = RootGrid();
        var (header, runtimesFolderText, runtimeCudaPreferenceCombo) = Header(request);
        Grid.SetRow(header, 0);
        root.Children.Add(header);

        var runtimeGrid = InstalledRuntimesGrid(request);
        var runtimeSearch = DataGridSearch.Create(runtimeGrid,
            item => item is RuntimeCatalogRow row ? $"{row.Name} {row.Backend} {row.State} {row.Location} {row.Details}" : "",
            Loc.T("Runtimes.SearchInstalled"));
        var runtimeSection = FilteredSection(
            Loc.T("Runtimes.InstalledLocalBuildsTitle"),
            request.ViewModel.Runtimes.VendorFilters,
            request.ViewModel.Runtimes.SelectedVendorFilter,
            request.ViewModel.Runtimes.PlatformFilters,
            request.ViewModel.Runtimes.SelectedPlatformFilter,
            (vendor, platform) => request.ViewModel.Runtimes.ApplyFilters(vendor, platform),
            runtimeGrid,
            "InstalledRuntime",
            runtimeSearch.Root);
        Grid.SetRow(runtimeSection, 1);
        root.Children.Add(runtimeSection);
        root.Children.Add(PageSectionFactory.HorizontalGridSplitter(2));

        var runtimePackageGrid = RuntimePackageGrid(request);
        var packageSection = FilteredSection(
            Loc.T("Runtimes.RuntimeDownloadsTitle"),
            request.ViewModel.RuntimePackages.VendorFilters,
            request.ViewModel.RuntimePackages.SelectedVendorFilter,
            request.ViewModel.RuntimePackages.PlatformFilters,
            request.ViewModel.RuntimePackages.SelectedPlatformFilter,
            (vendor, platform) => request.ViewModel.RuntimePackages.ApplyFilters(vendor, platform),
            runtimePackageGrid,
            "RuntimeDownload");
        Grid.SetRow(packageSection, 3);
        root.Children.Add(packageSection);

        return new RuntimesPageControls(
            root,
            runtimesFolderText,
            runtimeGrid,
            runtimeSearch,
            runtimePackageGrid,
            runtimeCudaPreferenceCombo);
    }

    private static Grid RootGrid()
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.82, GridUnitType.Star), MinHeight = 100 });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.25, GridUnitType.Star), MinHeight = 150 });

        return root;
    }

    private static (Grid Header, TextBlock RuntimesFolderText, WpfComboBox CudaPreferenceCombo) Header(RuntimesPageRequest request)
    {
        var folderStrip = FolderStripActionsFirst(
            Loc.T("Runtimes.FolderLabel"),
            request.RuntimeRoot,
            out var runtimesFolderText,
            (Loc.T("Runtimes.ChooseFolderButton"), request.Actions.ChooseRuntimeFolderAsync));
        var header = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        folderStrip.Margin = new Thickness(0);
        header.Children.Add(folderStrip);
        var rightActions = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        rightActions.Children.Add(new TextBlock
        {
            Text = Loc.T("Runtimes.CudaDownloadsLabel"),
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 8, 6)
        });
        var runtimeCudaPreferenceCombo = LaunchCombo(AppPreferenceService.CudaPackagePreferenceOptions());
        runtimeCudaPreferenceCombo.Width = 132;
        runtimeCudaPreferenceCombo.SelectedItem = AppPreferenceService.CudaPackagePreferenceLabel(request.CudaPackagePreference);
        runtimeCudaPreferenceCombo.ToolTip = Loc.T("Tooltip.CudaPreferenceCombo");
        runtimeCudaPreferenceCombo.SelectionChanged += async (_, _) => await request.Actions.ChangeCudaPackagePreferenceAsync();
        rightActions.Children.Add(runtimeCudaPreferenceCombo);
        Grid.SetColumn(rightActions, 1);
        header.Children.Add(rightActions);
        return (header, runtimesFolderText, runtimeCudaPreferenceCombo);
    }

    private static DataGrid InstalledRuntimesGrid(RuntimesPageRequest request)
    {
        var grid = PageSectionFactory.GridFor(
            (Loc.T("Runtimes.Col.Name"), nameof(RuntimeCatalogRow.Name), 1.4),
            (Loc.T("Runtimes.Col.Backend"), nameof(RuntimeCatalogRow.Backend), .55),
            (Loc.T("Runtimes.Col.State"), nameof(RuntimeCatalogRow.State), .55),
            (Loc.T("Runtimes.Col.Location"), nameof(RuntimeCatalogRow.Location), 3));
        grid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
        grid.RowDetailsTemplate = PageSectionFactory.RowDetailsTemplate(nameof(RuntimeCatalogRow.Details));
        grid.Columns.Insert(0, RuntimeDetailsColumn());
        grid.Columns.Insert(1, SelectorFavoriteGridColumn.Create<RuntimeCatalogRow>(row =>
            request.Actions.ToggleRuntimeFavoriteAsync(row.Runtime!), nameof(RuntimeCatalogRow.Runtime)));
        grid.LoadingRow += (_, args) => args.Row.DetailsVisibility = args.Row.Item is RuntimeCatalogRow { IsDetailsExpanded: true }
            ? Visibility.Visible
            : Visibility.Collapsed;
        PageSectionFactory.AddButtonColumn(grid, Loc.T("Runtimes.Col.Trust"), nameof(RuntimeCatalogRow.VerifyAction), nameof(RuntimeCatalogRow.CanVerify), request.Actions.VerifyRuntimeRowClick, .62, tooltipBinding: nameof(RuntimeCatalogRow.VerifyToolTip));
        PageSectionFactory.AddButtonColumn(grid, Loc.T("Common.ActionButton"), nameof(RuntimeCatalogRow.DeleteAction), nameof(RuntimeCatalogRow.CanDelete), request.Actions.DeleteRuntimeRowClick, .65, tooltipBinding: nameof(RuntimeCatalogRow.DeleteToolTip), visualRole: VisualRole.Danger, compactContent: "×");
        PageSectionFactory.ApplyGridTextMargin(grid, new Thickness(6, 0, 6, 0));
        request.Actions.ConfigureRuntimeGridColumnSizing(grid);
        grid.ItemsSource = request.ViewModel.Runtimes.Rows;
        DataGridRowContextMenu.Attach(
            grid,
            SelectorFavoriteContextAction.Create<RuntimeCatalogRow>(
                row => row.IsFavorite,
                row => row.Runtime is not null,
                row => request.Actions.ToggleRuntimeFavoriteAsync(row.Runtime!)),
            new(row => ((RuntimeCatalogRow)row).VerifyAction,
                row => row is RuntimeCatalogRow { CanVerify: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.VerifyRuntimeRowClick, row),
                ToolTip: row => ((RuntimeCatalogRow)row).VerifyToolTip),
            new(row => ((RuntimeCatalogRow)row).DeleteAction,
                row => row is RuntimeCatalogRow { CanDelete: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.DeleteRuntimeRowClick, row),
                SeparatorBefore: true,
                ToolTip: row => ((RuntimeCatalogRow)row).DeleteToolTip));
        return grid;
    }

    private static DataGridTemplateColumn RuntimeDetailsColumn()
    {
        var button = new FrameworkElementFactory(typeof(WpfButton));
        button.SetBinding(ContentControl.ContentProperty, new System.Windows.Data.Binding(nameof(RuntimeCatalogRow.DetailsAction)));
        button.SetBinding(UIElement.IsEnabledProperty, new System.Windows.Data.Binding(nameof(RuntimeCatalogRow.CanExpandDetails)));
        button.SetBinding(FrameworkElement.TagProperty, new System.Windows.Data.Binding("."));
        button.SetValue(FrameworkElement.ToolTipProperty, Loc.T("Runtimes.ExpandDetails"));
        button.SetValue(AutomationProperties.NameProperty, Loc.T("Runtimes.ExpandDetails"));
        InlineGlyphButtonVisual.ConfigureForDataGrid(button, 15);
        button.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler(ToggleRuntimeDetails));
        return new DataGridTemplateColumn
        {
            Header = "",
            CellTemplate = new DataTemplate(typeof(RuntimeCatalogRow)) { VisualTree = button },
            CellStyle = InlineGlyphButtonVisual.CenteredDataGridCellStyle(),
            Width = new DataGridLength(28),
            MinWidth = 28,
            MaxWidth = 28
        };
    }

    private static void ToggleRuntimeDetails(object sender, RoutedEventArgs args)
    {
        if (sender is not WpfButton { Tag: RuntimeCatalogRow row } button
            || VisualTreeTraversal.FindAncestor<DataGridRow>(button) is not { } container)
            return;
        row.IsDetailsExpanded = !row.IsDetailsExpanded;
        container.DetailsVisibility = row.IsDetailsExpanded ? Visibility.Visible : Visibility.Collapsed;
        button.Content = row.DetailsAction;
        var label = Loc.T(row.IsDetailsExpanded ? "Runtimes.CollapseDetails" : "Runtimes.ExpandDetails");
        button.ToolTip = label;
        AutomationProperties.SetName(button, label);
        args.Handled = true;
    }

    private static DataGrid RuntimePackageGrid(RuntimesPageRequest request)
    {
        var grid = PageSectionFactory.GridFor(
            (Loc.T("Runtimes.Col.Runtime"), nameof(RuntimePackagePresetRow.Label), 1.45),
            (Loc.T("Runtimes.Col.Backend"), nameof(RuntimePackagePresetRow.Backend), .68),
            (Loc.T("Runtimes.Col.Local"), nameof(RuntimePackagePresetRow.LocalStatus), .78),
            (Loc.T("Runtimes.Col.LatestRelease"), nameof(RuntimePackagePresetRow.LatestRelease), 1.2),
            (Loc.T("Runtimes.Col.Assets"), nameof(RuntimePackagePresetRow.Assets), 2.35));
        PageSectionFactory.AddButtonColumn(grid, Loc.T("Runtimes.BuildFromSourceTitle"), nameof(RuntimePackagePresetRow.BuildSourceAction), nameof(RuntimePackagePresetRow.CanBuildSource), request.Actions.RuntimeSourceRowClick, .75, tooltipBinding: nameof(RuntimePackagePresetRow.BuildSourceToolTip));
        PageSectionFactory.AddButtonColumn(grid, Loc.T("Runtimes.ActionBtn.Install"), nameof(RuntimePackagePresetRow.InstallAction), nameof(RuntimePackagePresetRow.CanInstall), request.Actions.InstallRuntimePackageRowClick, .75, tooltipBinding: nameof(RuntimePackagePresetRow.InstallToolTip));
        PageSectionFactory.AddButtonColumn(grid, Loc.T("Runtimes.ActionBtn.Update"), nameof(RuntimePackagePresetRow.CheckAction), nameof(RuntimePackagePresetRow.CanCheck), request.Actions.CheckRuntimePackageUpdateRowClick, .75, tooltipBinding: nameof(RuntimePackagePresetRow.CheckToolTip));
        PageSectionFactory.AddButtonColumn(grid, Loc.T("Common.DeleteButton"), nameof(RuntimePackagePresetRow.DeleteAction), nameof(RuntimePackagePresetRow.CanDelete), request.Actions.DeleteRuntimePackageRowClick, .75, tooltipBinding: nameof(RuntimePackagePresetRow.DeleteToolTip), visualRole: VisualRole.Danger, compactContent: "×");
        PageSectionFactory.ApplyGridTextMargin(grid, new Thickness(6, 0, 6, 0));
        request.Actions.ConfigureRuntimeBuildGridColumnSizing(grid);
        grid.ItemsSource = request.ViewModel.RuntimePackages.Rows;
        DataGridRowContextMenu.Attach(
            grid,
            new(row => ((RuntimePackagePresetRow)row).BuildSourceAction,
                row => row is RuntimePackagePresetRow { CanBuildSource: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.RuntimeSourceRowClick, row),
                ToolTip: row => ((RuntimePackagePresetRow)row).BuildSourceToolTip),
            new(row => ((RuntimePackagePresetRow)row).InstallAction,
                row => row is RuntimePackagePresetRow { CanInstall: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.InstallRuntimePackageRowClick, row),
                ToolTip: row => ((RuntimePackagePresetRow)row).InstallToolTip),
            new(row => ((RuntimePackagePresetRow)row).CheckAction,
                row => row is RuntimePackagePresetRow { CanCheck: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.CheckRuntimePackageUpdateRowClick, row),
                ToolTip: row => ((RuntimePackagePresetRow)row).CheckToolTip),
            new(row => ((RuntimePackagePresetRow)row).DeleteAction,
                row => row is RuntimePackagePresetRow { CanDelete: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(request.Actions.DeleteRuntimePackageRowClick, row),
                SeparatorBefore: true,
                ToolTip: row => ((RuntimePackagePresetRow)row).DeleteToolTip));
        return grid;
    }

    private static Grid FilteredSection(
        string title,
        IReadOnlyList<string> vendorOptions,
        string selectedVendor,
        IReadOnlyList<string> platformOptions,
        string selectedPlatform,
        Action<string, string> applyFilters,
        DataGrid dataGrid,
        string namePrefix,
        FrameworkElement? headerAction = null)
    {
        var section = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition());

        var header = new Grid { Margin = new Thickness(1, 2, 0, 4) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMain"],
            VerticalAlignment = VerticalAlignment.Center
        });

        var bar = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (headerAction is not null)
            bar.Children.Add(headerAction);
        bar.Children.Add(FilterLabel("Type"));
        var vendor = FilterCombo(vendorOptions, selectedVendor, $"{namePrefix}TypeFilter");
        bar.Children.Add(vendor);
        bar.Children.Add(FilterLabel("Platform"));
        var platform = FilterCombo(platformOptions, selectedPlatform, $"{namePrefix}PlatformFilter");
        bar.Children.Add(platform);

        void ApplySelection()
            => applyFilters(vendor.SelectedItem?.ToString() ?? RuntimeInventoryFilterService.All,
                platform.SelectedItem?.ToString() ?? RuntimeInventoryFilterService.All);

        vendor.SelectionChanged += (_, _) => ApplySelection();
        platform.SelectionChanged += (_, _) => ApplySelection();
        Grid.SetColumn(bar, 1);
        header.Children.Add(bar);
        section.Children.Add(header);

        var frame = PageSectionFactory.GridFrame(dataGrid);
        Grid.SetRow(frame, 1);
        section.Children.Add(frame);
        return section;
    }

    private static TextBlock FilterLabel(string text) => new()
    {
        Text = text,
        Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center,
        Margin = new Thickness(10, 0, 6, 0)
    };

    private static WpfComboBox FilterCombo(IReadOnlyList<string> options, string selected, string name)
    {
        var combo = LaunchCombo(options);
        combo.Name = name;
        combo.Width = 104;
        combo.Margin = new Thickness(0);
        combo.SelectedItem = options.FirstOrDefault(option => string.Equals(option, selected, StringComparison.OrdinalIgnoreCase))
            ?? RuntimeInventoryFilterService.All;
        return combo;
    }

    private static Grid FolderStripActionsFirst(string label, string path, out TextBlock pathText, params (string Text, Func<Task> Click)[] actions)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        var column = 0;
        foreach (var _ in actions)
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());

        foreach (var action in actions)
        {
            var button = Button(action.Text, action.Click);
            Grid.SetColumn(button, column++);
            grid.Children.Add(button);
        }

        var labelBlock = new TextBlock
        {
            Text = label,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 10, 6)
        };
        Grid.SetColumn(labelBlock, column++);
        grid.Children.Add(labelBlock);

        pathText = new TextBlock
        {
            Text = path,
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 10, 6)
        };
        Grid.SetColumn(pathText, column);
        grid.Children.Add(pathText);
        return grid;
    }

    private static WpfComboBox LaunchCombo(IEnumerable<string> values) => new()
    {
        ItemsSource = values.ToArray(),
        SelectedIndex = 0,
        MinHeight = 27,
        MinWidth = 76,
        Margin = new Thickness(0, 0, 6, 4),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch
    };

    private static WpfButton Button(string text, Func<Task> click)
    {
        var button = new WpfButton { Content = text, ToolTip = TooltipText(ButtonToolTip(text)) };
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += async (_, _) => await click();
        return button;
    }

    private static string ButtonToolTip(string text)
        => (text ?? "").Trim() switch
        {
            var t when string.Equals(t, Loc.T("Runtimes.ChooseFolderButton")) => Loc.T("Tooltip.ChooseFolder"),
            var t when string.Equals(t, Loc.T("Runtimes.ShowAdvancedButton")) => Loc.T("Tooltip.RuntimesShowAdvanced"),
            var t when string.Equals(t, Loc.T("Runtimes.HideAdvancedButton")) => Loc.T("Tooltip.RuntimesHideAdvanced"),
            var label => string.IsNullOrWhiteSpace(label) ? "" : Loc.T("Common.RunAction", label)
        };

    private static string TooltipText(string text) => text;
}
