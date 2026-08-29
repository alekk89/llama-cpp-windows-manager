using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed record ModelsPageActions(
    Func<Task> ScanModelsFolderAsync,
    Func<Task> ImportModelFileAsync,
    Func<Task> ChooseModelsFolderAsync,
    Action OpenModelsFolder,
    Func<Task> ManageModelGroupsAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> AssignLaunchProfileGroupAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> RemoveLaunchProfileGroupAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> ToggleTrayProfileFavoriteAsync,
    Func<ModelRecord, NamedModelLaunchProfile, Task> LoadLaunchProfileAsync,
    Action BeginNewLaunchProfile,
    Action<DataGrid, DataGrid?> SelectModelGridRow,
    RoutedEventHandler OpenModelFolderRowClick,
    RoutedEventHandler DeleteModelRowClick,
    Func<Task> SearchHuggingFaceAsync,
    Func<Task> ShowDownloadHistoryAsync,
    Action<DataGrid> ConfigureModelGridColumnSizing);

public sealed record ModelsPageRequest(
    MainWindowViewModel ViewModel,
    string ModelsRoot,
    UIElement LaunchSettingsPanel,
    ModelsPageActions Actions);

public sealed record ModelsPageControls(
    Grid Root,
    TextBlock ModelsFolderText,
    DataGrid ModelsGrid,
    DataGrid ModelVariantsGrid,
    Grid HuggingFaceSection,
    GridSplitter HuggingFaceSplitter,
    WpfTextBox HuggingFaceQueryBox,
    DataGrid HuggingFaceGrid);

public static class ModelsPageFactory
{
    public static ModelsPageControls Create(ModelsPageRequest request)
    {
        var root = new Grid { Margin = new Thickness(16) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 260 });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(230), MinHeight = 120 });

        var folderStrip = FolderStripActionsFirst(
            Loc.T("Models.FolderLabel"),
            request.ModelsRoot,
            out var modelsFolderText,
            (Loc.T("Models.ScanButton"), request.Actions.ScanModelsFolderAsync),
            (Loc.T("Models.ImportFileButton"), request.Actions.ImportModelFileAsync),
            (Loc.T("Common.ChooseButton"), request.Actions.ChooseModelsFolderAsync),
            (Loc.T("Common.OpenButton"), () => OpenModelsFolder(request.Actions)),
            ("Groups…", request.Actions.ManageModelGroupsAsync)
        );
        var folderScroller = new ScrollViewer
        {
            Content = folderStrip,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(folderScroller, 0);
        root.Children.Add(folderScroller);

        var body = new Grid { MinWidth = 718 };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.05, GridUnitType.Star), MinWidth = 330 });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(.95, GridUnitType.Star), MinWidth = 380 });

        var (modelLists, modelsGrid, modelVariantsGrid) = ModelLists(request);
        body.Children.Add(modelLists);
        body.Children.Add(PageSectionFactory.VerticalGridSplitter(1));
        Grid.SetColumn(request.LaunchSettingsPanel, 2);
        body.Children.Add(request.LaunchSettingsPanel);
        var bodyScroller = new ScrollViewer
        {
            Content = body,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        body.SetBinding(FrameworkElement.WidthProperty, new WpfBinding(nameof(ScrollViewer.ViewportWidth))
        {
            Source = bodyScroller
        });
        Grid.SetRow(bodyScroller, 1);
        root.Children.Add(bodyScroller);
        var huggingFaceSplitter = PageSectionFactory.HorizontalGridSplitter(2);
        root.Children.Add(huggingFaceSplitter);

        var (huggingFaceSection, huggingFaceQueryBox, huggingFaceGrid) = HuggingFaceSearch(request);
        Grid.SetRow(huggingFaceSection, 3);
        root.Children.Add(huggingFaceSection);

        return new ModelsPageControls(root, modelsFolderText, modelsGrid, modelVariantsGrid, huggingFaceSection, huggingFaceSplitter, huggingFaceQueryBox, huggingFaceGrid);
    }

    private static (Grid Section, DataGrid ModelsGrid, DataGrid ModelVariantsGrid) ModelLists(ModelsPageRequest request)
    {
        var modelLists = new Grid();
        modelLists.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 130 });
        modelLists.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
        modelLists.RowDefinitions.Add(new RowDefinition { Height = new GridLength(.58, GridUnitType.Star), MinHeight = 96 });

        var modelsGrid = PageSectionFactory.GridFor(
            (Loc.T("Models.Col.Name"), nameof(ModelGridRow.Name), 2.35),
            (Loc.T("Models.Col.Quant"), nameof(ModelGridRow.Quant), .6),
            (Loc.T("Models.Col.Size"), nameof(ModelGridRow.Size), .65));
        PageSectionFactory.AddButtonColumn(modelsGrid, Loc.T("Models.ActionBtn.OpenFolder"), nameof(ModelGridRow.OpenFolderAction), nameof(ModelGridRow.CanOpenFolder), request.Actions.OpenModelFolderRowClick, .85, tooltipBinding: nameof(ModelGridRow.OpenFolderToolTip));
        PageSectionFactory.AddButtonColumn(modelsGrid, Loc.T("Models.ActionBtn.Delete"), nameof(ModelGridRow.DeleteAction), nameof(ModelGridRow.CanDelete), request.Actions.DeleteModelRowClick, .65, tooltipBinding: nameof(ModelGridRow.DeleteToolTip), visualRole: VisualRole.Danger);
        request.Actions.ConfigureModelGridColumnSizing(modelsGrid);
        modelsGrid.ItemsSource = request.ViewModel.Models.Rows;
        AttachModelContextMenu(modelsGrid, request.Actions);
        var modelVariantsGrid = PageSectionFactory.GridFor(
            (Loc.T("Models.Col.Name"), nameof(ModelGridRow.Name), 1.35),
            (Loc.T("Models.Col.BaseModel"), nameof(ModelGridRow.BaseModel), 1.35),
            (Loc.T("Models.Col.Port"), nameof(ModelGridRow.Port), .45));
        modelVariantsGrid.Columns.Add(LaunchProfileGroupColumn(
            request.Actions.AssignLaunchProfileGroupAsync,
            request.Actions.RemoveLaunchProfileGroupAsync));
        PageSectionFactory.AddButtonColumn(modelVariantsGrid, Loc.T("Models.ActionBtn.Remove"), nameof(ModelGridRow.DeleteAction), nameof(ModelGridRow.CanDelete), request.Actions.DeleteModelRowClick, .68, tooltipBinding: nameof(ModelGridRow.DeleteToolTip), visualRole: VisualRole.Danger);
        modelVariantsGrid.ItemsSource = request.ViewModel.Models.VariantRows;
        AttachLaunchProfileContextMenu(modelVariantsGrid, request.Actions);

        modelsGrid.SelectionChanged += (_, _) => request.Actions.SelectModelGridRow(modelsGrid, modelVariantsGrid);
        modelVariantsGrid.SelectionChanged += (_, _) => request.Actions.SelectModelGridRow(modelVariantsGrid, modelsGrid);

        modelLists.Children.Add(PageSectionFactory.GridSection(Loc.T("Models.ModelFilesTitle"), modelsGrid, Loc.T("Models.ModelFilesDescription")));
        modelLists.Children.Add(PageSectionFactory.HorizontalGridSplitter(1));
        var variantsSection = PageSectionFactory.GridSection(Loc.T("Models.SavedVariantsTitle"), modelVariantsGrid, Loc.T("Models.SavedVariantsDescription"));
        Grid.SetRow(variantsSection, 2);
        modelLists.Children.Add(variantsSection);
        return (modelLists, modelsGrid, modelVariantsGrid);
    }

    private static DataGridTemplateColumn LaunchProfileGroupColumn(
        Func<ModelRecord, NamedModelLaunchProfile, Task> assignLaunchProfileGroupAsync,
        Func<ModelRecord, NamedModelLaunchProfile, Task> removeLaunchProfileGroupAsync)
    {
        var panel = new FrameworkElementFactory(typeof(Grid));

        var addButton = new FrameworkElementFactory(typeof(WpfButton));
        addButton.Name = "AddGroupButton";
        addButton.SetValue(ContentControl.ContentProperty, "Add");
        addButton.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(ModelGridRow.GroupToolTip)));
        addButton.SetBinding(FrameworkElement.TagProperty, new WpfBinding());
        PageSectionFactory.ConfigureGridActionButton(addButton);
        addButton.SetValue(FrameworkElement.MinWidthProperty, 0d);
        RoutedEventHandler addClick = async (sender, _) =>
        {
            if ((sender as FrameworkElement)?.Tag is ModelGridRow { LaunchProfile: { } profile } row)
                await assignLaunchProfileGroupAsync(row.Model, profile);
        };
        addButton.AddHandler(WpfButton.ClickEvent, addClick);
        panel.AppendChild(addButton);

        var groupButton = new FrameworkElementFactory(typeof(WpfButton));
        groupButton.Name = "GroupNameButton";
        groupButton.SetBinding(ContentControl.ContentProperty, new WpfBinding(nameof(ModelGridRow.Group)));
        groupButton.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(nameof(ModelGridRow.GroupToolTip)));
        groupButton.SetBinding(FrameworkElement.TagProperty, new WpfBinding());
        groupButton.SetValue(VisualRole.ButtonRoleProperty, VisualRole.Quiet);
        PageSectionFactory.ConfigureGridActionButton(groupButton);
        groupButton.SetValue(FrameworkElement.MinWidthProperty, 0d);
        var groupLabel = new FrameworkElementFactory(typeof(TextBlock));
        groupLabel.SetBinding(TextBlock.TextProperty, new WpfBinding("."));
        groupLabel.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
        groupLabel.SetValue(TextBlock.TextWrappingProperty, TextWrapping.NoWrap);
        groupButton.SetValue(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = groupLabel });
        RoutedEventHandler groupClick = (sender, _) =>
        {
            if (sender is not WpfButton button
                || button.Tag is not ModelGridRow { LaunchProfile: { } profile } row)
                return;

            var menu = new ContextMenu();
            var change = new MenuItem { Header = Loc.T("ModelGroups.ChangeGroup") };
            change.Click += async (_, _) => await assignLaunchProfileGroupAsync(row.Model, profile);
            var remove = new MenuItem { Header = Loc.T("ModelGroups.RemoveFromGroup") };
            remove.Click += async (_, _) => await removeLaunchProfileGroupAsync(row.Model, profile);
            menu.Items.Add(change);
            menu.Items.Add(remove);
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            button.ContextMenu = menu;
            menu.IsOpen = true;
        };
        groupButton.AddHandler(WpfButton.ClickEvent, groupClick);
        panel.AppendChild(groupButton);

        var template = new DataTemplate(typeof(ModelGridRow)) { VisualTree = panel };
        var grouped = new DataTrigger
        {
            Binding = new WpfBinding(nameof(ModelGridRow.CanAssignGroup)),
            Value = false
        };
        grouped.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "AddGroupButton"));
        template.Triggers.Add(grouped);

        var ungrouped = new DataTrigger
        {
            Binding = new WpfBinding(nameof(ModelGridRow.CanAssignGroup)),
            Value = true
        };
        ungrouped.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed, "GroupNameButton"));
        template.Triggers.Add(ungrouped);

        return new DataGridTemplateColumn
        {
            Header = Loc.T("ModelGroups.Column.Group"),
            CellTemplate = template,
            Width = new DataGridLength(.75, DataGridLengthUnitType.Star),
            MinWidth = 84
        };
    }

    private static void AttachModelContextMenu(DataGrid grid, ModelsPageActions actions)
    {
        DataGridRowContextMenu.Attach(
            grid,
            new(_ => Loc.T("Models.ActionBtn.OpenFolder"),
                row => row is ModelGridRow { CanOpenFolder: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(actions.OpenModelFolderRowClick, row)),
            new(_ => Loc.T("Launch.SaveAsNewButton"),
                row => row is ModelGridRow { IsMissing: false },
                _ => BeginNewLaunchProfileAsync(actions)),
            new(_ => Loc.T("Models.ActionBtn.Delete"),
                row => row is ModelGridRow { CanDelete: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(actions.DeleteModelRowClick, row),
                SeparatorBefore: true));
    }

    private static void AttachLaunchProfileContextMenu(DataGrid grid, ModelsPageActions actions)
    {
        DataGridRowContextMenu.Attach(
            grid,
            new(_ => Loc.T("Overview.LoadButton"),
                row => row is ModelGridRow { LaunchProfile: not null, CanLoad: true },
                row => LoadProfileAsync(actions, (ModelGridRow)row),
                ToolTip: row => ((ModelGridRow)row).IsMissing
                    ? Loc.T("Overview.MissingModelLoadTooltip")
                    : Loc.T("Tooltip.Load")),
            new(row => ((ModelGridRow)row).IsTrayFavorite
                    ? Loc.T("Tray.RemoveFavorite")
                    : Loc.T("Tray.AddFavorite"),
                row => row is ModelGridRow { LaunchProfile: not null },
                row => ToggleTrayFavoriteAsync(actions, (ModelGridRow)row)),
            new(row => ((ModelGridRow)row).CanAssignGroup
                    ? Loc.T("ModelGroups.AssignAction")
                    : Loc.T("ModelGroups.ChangeGroup"),
                row => row is ModelGridRow { LaunchProfile: not null },
                row => AssignGroupAsync(actions, (ModelGridRow)row)),
            new(_ => Loc.T("ModelGroups.RemoveFromGroup"),
                row => row is ModelGridRow { LaunchProfile: not null, CanAssignGroup: false },
                row => RemoveGroupAsync(actions, (ModelGridRow)row),
                IsVisible: row => row is ModelGridRow { CanAssignGroup: false }),
            new(_ => Loc.T("Models.ActionBtn.Remove"),
                row => row is ModelGridRow { CanDelete: true },
                row => DataGridRowContextMenu.RaiseRowActionAsync(actions.DeleteModelRowClick, row),
                SeparatorBefore: true));
    }

    private static Task BeginNewLaunchProfileAsync(ModelsPageActions actions)
    {
        actions.BeginNewLaunchProfile();
        return Task.CompletedTask;
    }

    private static Task LoadProfileAsync(ModelsPageActions actions, ModelGridRow row)
        => actions.LoadLaunchProfileAsync(row.Model, row.LaunchProfile!);

    private static Task AssignGroupAsync(ModelsPageActions actions, ModelGridRow row)
        => actions.AssignLaunchProfileGroupAsync(row.Model, row.LaunchProfile!);

    private static Task ToggleTrayFavoriteAsync(ModelsPageActions actions, ModelGridRow row)
        => actions.ToggleTrayProfileFavoriteAsync(row.Model, row.LaunchProfile!);

    private static Task RemoveGroupAsync(ModelsPageActions actions, ModelGridRow row)
        => actions.RemoveLaunchProfileGroupAsync(row.Model, row.LaunchProfile!);

    private static Task OpenModelsFolder(ModelsPageActions actions)
    {
        actions.OpenModelsFolder();
        return Task.CompletedTask;
    }

    private static (Grid Section, WpfTextBox QueryBox, DataGrid SearchGrid) HuggingFaceSearch(ModelsPageRequest request)
    {
        var hf = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        hf.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hf.RowDefinitions.Add(new RowDefinition());
        var hfBar = Bar();
        var hfQueryBox = new WpfTextBox { Width = 280, ToolTip = Loc.T("Tooltip.HfSearchBox") };
        hfBar.Children.Add(hfQueryBox);
        var searchButton = Button(Loc.T("Models.ActionBtn.SearchHf"), request.Actions.SearchHuggingFaceAsync);
        VisualRole.SetButtonRole(searchButton, VisualRole.Primary);
        hfBar.Children.Add(searchButton);
        hfBar.Children.Add(Button(Loc.T("Models.ActionBtn.History"), request.Actions.ShowDownloadHistoryAsync));
        hf.Children.Add(hfBar);
        var hfGrid = new DataGrid();
        PageSectionFactory.PolishGrid(hfGrid);
        var hfGridFrame = PageSectionFactory.GridFrame(hfGrid);
        Grid.SetRow(hfGridFrame, 1);
        hf.Children.Add(hfGridFrame);
        return (hf, hfQueryBox, hfGrid);
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

    private static WrapPanel Bar()
        => new() { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };

    private static WpfButton Button(string text, Func<Task> click)
    {
        var button = new WpfButton { Content = text, ToolTip = TooltipText(ButtonToolTip(text)) };
        ToolTipService.SetShowOnDisabled(button, true);
        button.Click += async (_, _) => await click();
        return button;
    }

    private static string ButtonToolTip(string text)
    {
        var t = (text ?? "").Trim();
        if (string.Equals(t, Loc.T("Models.ScanButton"))) return Loc.T("Tooltip.ScanModelsFolder");
        if (string.Equals(t, Loc.T("Models.ImportFileButton"))) return Loc.T("Tooltip.ChooseGgufFile");
        if (string.Equals(t, Loc.T("Common.ChooseButton"))) return Loc.T("Tooltip.ChooseFolder");
        if (string.Equals(t, Loc.T("Common.OpenButton"))) return Loc.T("Tooltip.OpenFolder");
        if (string.Equals(t, "Groups…")) return "Create launch-profile retention groups, assign profiles, and set eviction priorities.";
        if (string.Equals(t, Loc.T("Models.ActionBtn.SearchHf"))) return Loc.T("Tooltip.SearchHf");
        if (string.Equals(t, Loc.T("Models.ActionBtn.History"))) return Loc.T("Tooltip.DownloadHistory");
        return string.IsNullOrWhiteSpace(t) ? "" : Loc.T("Common.RunAction", t);
    }

    private static string TooltipText(string text) => text;
}
