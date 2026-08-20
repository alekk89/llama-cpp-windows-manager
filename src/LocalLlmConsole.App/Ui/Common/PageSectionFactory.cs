using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBinding = System.Windows.Data.Binding;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfControl = System.Windows.Controls.Control;

namespace LocalLlmConsole;

public static class PageSectionFactory
{
    public static DataGrid GridFor(params (string Header, string Binding, double Weight)[] columns)
    {
        var grid = new DataGrid();
        PolishGrid(grid);
        ConfigureGridColumns(grid, columns);
        return grid;
    }

    public static void PolishGrid(DataGrid grid)
    {
        grid.BorderThickness = new Thickness(0);
        grid.Margin = new Thickness(0);
        grid.FontSize = 12.5;
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        ScrollViewer.SetVerticalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
    }

    public static DataTemplate RowDetailsTemplate(string binding)
    {
        var factory = new FrameworkElementFactory(typeof(TextBlock));
        factory.SetBinding(TextBlock.TextProperty, new WpfBinding(binding));
        factory.SetValue(TextBlock.TextWrappingProperty, TextWrapping.Wrap);
        factory.SetValue(TextBlock.ForegroundProperty, (WpfBrush)WpfApplication.Current.Resources["TextMuted"]);
        factory.SetValue(TextBlock.MarginProperty, new Thickness(14, 2, 14, 8));
        return new DataTemplate { VisualTree = factory };
    }

    public static Border GridFrame(DataGrid grid)
    {
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 7, 0, 8),
            Child = grid
        };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        return frame;
    }

    public static Grid GridSection(string title, DataGrid grid, string description = "")
    {
        var section = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition());
        var header = SectionHeader(title, description);

        section.Children.Add(header);
        var frame = GridFrame(grid);
        Grid.SetRow(frame, 1);
        section.Children.Add(frame);
        return section;
    }

    public static Grid ContentSection(string title, UIElement content, string description = "")
    {
        var section = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition());
        section.Children.Add(SectionHeader(title, description));
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 7, 0, 8),
            Padding = new Thickness(6, 5, 6, 6),
            Child = content
        };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        Grid.SetRow(frame, 1);
        section.Children.Add(frame);
        return section;
    }

    private static Grid SectionHeader(string title, string description = "")
    {
        var header = new Grid { Margin = new Thickness(1, 2, 0, 4) };
        var copy = new StackPanel();
        var titleBlock = new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, string.IsNullOrWhiteSpace(description) ? 0 : 3)
        };
        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
        AutomationProperties.SetHeadingLevel(titleBlock, AutomationHeadingLevel.Level2);
        copy.Children.Add(titleBlock);
        if (!string.IsNullOrWhiteSpace(description))
        {
            var descriptionBlock = new TextBlock
            {
                Text = description,
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            };
            descriptionBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            copy.Children.Add(descriptionBlock);
        }
        header.Children.Add(copy);
        return header;
    }

    public static Grid FramedSection(string title, UIElement child)
    {
        var section = new Grid { Margin = new Thickness(0, 0, 0, 6) };
        section.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        section.RowDefinitions.Add(new RowDefinition());
        section.Children.Add(SectionHeader(title));
        var frame = new Border
        {
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Padding = new Thickness(2),
            Margin = new Thickness(0, 7, 0, 8),
            Child = child
        };
        frame.SetResourceReference(Border.BackgroundProperty, "SurfaceRaised");
        frame.SetResourceReference(Border.BorderBrushProperty, "PanelBorder");
        Grid.SetRow(frame, 1);
        section.Children.Add(frame);
        return section;
    }

    public static GridSplitter HorizontalGridSplitter(int row)
    {
        var splitter = new GridSplitter
        {
            Height = 7,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Rows,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = true,
            Background = (WpfBrush)WpfApplication.Current.Resources["PanelBorderStrong"],
            Margin = new Thickness(0, 3, 0, 3)
        };
        Grid.SetRow(splitter, row);
        return splitter;
    }

    public static GridSplitter VerticalGridSplitter(int column)
    {
        var splitter = new GridSplitter
        {
            Width = 7,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = true,
            Background = (WpfBrush)WpfApplication.Current.Resources["PanelBorderStrong"],
            Margin = new Thickness(2, 6, 2, 6)
        };
        Grid.SetColumn(splitter, column);
        return splitter;
    }

    public static void ConfigureGridColumns(DataGrid grid, params (string Header, string Binding, double Weight)[] columns)
    {
        grid.Columns.Clear();
        var textStyle = (Style)WpfApplication.Current.Resources["GridCellText"];
        foreach (var col in columns)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = col.Header,
                Binding = new WpfBinding(col.Binding),
                Width = new DataGridLength(col.Weight, DataGridLengthUnitType.Star),
                MinWidth = 56,
                CanUserResize = true,
                ElementStyle = textStyle
            });
    }

    public static void ApplyGridTextMargin(DataGrid grid, Thickness margin)
    {
        var textStyle = new Style(typeof(TextBlock), (Style)WpfApplication.Current.Resources["GridCellText"]);
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, margin));
        foreach (var column in grid.Columns.OfType<DataGridTextColumn>())
            column.ElementStyle = textStyle;
    }

    public static void AddButtonColumn(
        DataGrid grid,
        string header,
        string contentBinding,
        string enabledBinding,
        RoutedEventHandler click,
        double weight,
        string tooltipBinding = "",
        Func<string, string>? tooltipProvider = null,
        string visualRole = "")
    {
        var factory = new FrameworkElementFactory(typeof(WpfButton));
        factory.SetBinding(ContentControl.ContentProperty, new WpfBinding(contentBinding));
        factory.SetBinding(AutomationProperties.NameProperty, new WpfBinding(contentBinding));
        factory.SetBinding(UIElement.IsEnabledProperty, new WpfBinding(enabledBinding));
        factory.SetBinding(FrameworkElement.TagProperty, new WpfBinding("."));
        if (!string.IsNullOrWhiteSpace(visualRole))
            factory.SetValue(VisualRole.ButtonRoleProperty, visualRole);
        if (!string.IsNullOrWhiteSpace(tooltipBinding))
        {
            factory.SetBinding(FrameworkElement.ToolTipProperty, new WpfBinding(tooltipBinding));
            factory.SetBinding(AutomationProperties.HelpTextProperty, new WpfBinding(tooltipBinding));
        }
        else
        {
            var toolTip = tooltipProvider?.Invoke(header) ?? "";
            if (!string.IsNullOrWhiteSpace(toolTip))
            {
                factory.SetValue(FrameworkElement.ToolTipProperty, toolTip);
                factory.SetValue(AutomationProperties.HelpTextProperty, toolTip);
            }
        }
        ConfigureGridActionButton(factory);
        var labelFactory = new FrameworkElementFactory(typeof(TextBlock));
        labelFactory.SetBinding(TextBlock.TextProperty, new WpfBinding("."));
        labelFactory.SetBinding(TextBlock.ForegroundProperty, new WpfBinding(nameof(WpfControl.Foreground))
        {
            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(WpfButton), 1)
        });
        factory.SetValue(ContentControl.ContentTemplateProperty, new DataTemplate { VisualTree = labelFactory });
        var style = new Style(typeof(WpfButton), (Style)WpfApplication.Current.Resources[typeof(WpfButton)]);
        var emptyTrigger = new Trigger { Property = ContentControl.ContentProperty, Value = "" };
        emptyTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Collapsed));
        style.Triggers.Add(emptyTrigger);
        factory.SetValue(FrameworkElement.StyleProperty, style);
        factory.AddHandler(WpfButton.ClickEvent, click);

        grid.Columns.Add(new DataGridTemplateColumn
        {
            Header = header,
            Width = new DataGridLength(weight, DataGridLengthUnitType.Star),
            MinWidth = 72,
            CanUserResize = true,
            CellTemplate = new DataTemplate { VisualTree = factory }
        });
    }

    public static void ConfigureGridActionButton(FrameworkElementFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        factory.SetValue(ToolTipService.ShowOnDisabledProperty, true);
        factory.SetValue(FrameworkElement.MinHeightProperty, 22.0);
        factory.SetValue(WpfControl.PaddingProperty, new Thickness(7, 1, 7, 2));
        factory.SetValue(FrameworkElement.MarginProperty, new Thickness(2, 1, 2, 1));
        factory.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Stretch);
    }

}
