using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfControl = System.Windows.Controls.Control;
using WpfCursors = System.Windows.Input.Cursors;
using WpfDataGridCell = System.Windows.Controls.DataGridCell;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfApplication = System.Windows.Application;

namespace LocalLlmConsole;

public static class InlineGlyphButtonVisual
{
    public const double Size = 20;

    public static void Configure(WpfButton button, double fontSize = 13)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.Template = Template();
        button.Background = WpfBrushes.Transparent;
        button.BorderThickness = new Thickness(0);
        button.Cursor = WpfCursors.Hand;
        button.Focusable = true;
        button.FontFamily = new WpfFontFamily("Segoe UI Symbol");
        button.FontSize = fontSize;
        button.Height = Size;
        button.MinHeight = 0;
        button.MinWidth = 0;
        button.Margin = new Thickness(0);
        button.Padding = new Thickness(0);
        button.Width = Size;
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Center;
        button.VerticalAlignment = VerticalAlignment.Center;
        button.HorizontalContentAlignment = System.Windows.HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
        button.SnapsToDevicePixels = true;
        button.UseLayoutRounding = true;
        ApplicationFontScaleService.SetIsExcluded(button, true);
        button.SetResourceReference(WpfControl.ForegroundProperty, "TextSoft");
    }

    public static void Configure(FrameworkElementFactory button, double fontSize = 13)
    {
        ArgumentNullException.ThrowIfNull(button);
        button.SetValue(WpfControl.TemplateProperty, Template());
        button.SetValue(WpfControl.BackgroundProperty, WpfBrushes.Transparent);
        button.SetValue(WpfControl.BorderThicknessProperty, new Thickness(0));
        button.SetValue(FrameworkElement.CursorProperty, WpfCursors.Hand);
        button.SetValue(UIElement.FocusableProperty, true);
        button.SetValue(WpfControl.FontFamilyProperty, new WpfFontFamily("Segoe UI Symbol"));
        button.SetValue(WpfControl.FontSizeProperty, fontSize);
        button.SetValue(FrameworkElement.HeightProperty, Size);
        button.SetValue(FrameworkElement.MinHeightProperty, 0d);
        button.SetValue(FrameworkElement.MinWidthProperty, 0d);
        button.SetValue(FrameworkElement.MarginProperty, new Thickness(0));
        button.SetValue(WpfControl.PaddingProperty, new Thickness(0));
        button.SetValue(FrameworkElement.WidthProperty, Size);
        button.SetValue(FrameworkElement.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        button.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        button.SetValue(WpfControl.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        button.SetValue(WpfControl.VerticalContentAlignmentProperty, VerticalAlignment.Center);
        button.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        button.SetValue(FrameworkElement.UseLayoutRoundingProperty, true);
        button.SetValue(ApplicationFontScaleService.IsExcludedProperty, true);
        button.SetResourceReference(WpfControl.ForegroundProperty, "TextSoft");
    }

    public static void ConfigureForDataGrid(FrameworkElementFactory button, double fontSize = 13)
        => Configure(button, fontSize);

    public static Style CenteredDataGridCellStyle()
    {
        var baseStyle = WpfApplication.Current?.TryFindResource(typeof(WpfDataGridCell)) as Style;
        var style = new Style(typeof(WpfDataGridCell), baseStyle);
        style.Setters.Add(new Setter(WpfControl.BackgroundProperty, WpfBrushes.Transparent));
        style.Setters.Add(new Setter(WpfControl.HorizontalContentAlignmentProperty, System.Windows.HorizontalAlignment.Center));
        style.Setters.Add(new Setter(WpfControl.VerticalContentAlignmentProperty, VerticalAlignment.Center));
        style.Setters.Add(new Setter(WpfControl.PaddingProperty, new Thickness(0)));
        style.Setters.Add(new Setter(UIElement.SnapsToDevicePixelsProperty, true));
        style.Setters.Add(new Setter(FrameworkElement.UseLayoutRoundingProperty, true));
        var selected = new Trigger { Property = WpfDataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(WpfControl.BackgroundProperty, WpfBrushes.Transparent));
        style.Triggers.Add(selected);
        return style;
    }

    private static ControlTemplate Template()
    {
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, System.Windows.HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
        presenter.SetValue(FrameworkElement.UseLayoutRoundingProperty, true);
        return new ControlTemplate(typeof(WpfButton)) { VisualTree = presenter };
    }
}
