using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using WpfBinding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;
using WpfApplication = System.Windows.Application;
using WpfControl = System.Windows.Controls.Control;

namespace LocalLlmConsole;

public static class SelectorFavoriteGridColumn
{
    public static DataGridTemplateColumn Create<T>(
        Func<T, Task> toggleFavoriteAsync,
        string? availabilityBinding = null)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(toggleFavoriteAsync);

        var button = new FrameworkElementFactory(typeof(WpfButton));
        InlineGlyphButtonVisual.ConfigureForDataGrid(button);
        button.SetBinding(ContentControl.ContentProperty, FavoriteBinding(FavoriteGlyphConverter.Instance));
        button.SetBinding(WpfControl.ForegroundProperty, FavoriteBinding(FavoriteForegroundConverter.Instance));
        button.SetBinding(FrameworkElement.ToolTipProperty, FavoriteBinding(FavoriteTooltipConverter.Instance));
        button.SetBinding(AutomationProperties.NameProperty, FavoriteBinding(FavoriteTooltipConverter.Instance));
        button.SetBinding(FrameworkElement.TagProperty, new WpfBinding("."));
        if (!string.IsNullOrWhiteSpace(availabilityBinding))
            button.SetBinding(UIElement.VisibilityProperty, new WpfBinding(availabilityBinding)
            {
                Converter = AvailableVisibilityConverter.Instance
            });
        RoutedEventHandler click = async (sender, args) =>
        {
            if (sender is not WpfButton { Tag: T row } favoriteButton) return;
            args.Handled = true;
            favoriteButton.IsEnabled = false;
            try
            {
                await toggleFavoriteAsync(row);
            }
            finally
            {
                favoriteButton.IsEnabled = true;
            }
        };
        button.AddHandler(WpfButton.ClickEvent, click);

        return new DataGridTemplateColumn
        {
            Header = "",
            CellTemplate = new DataTemplate(typeof(T)) { VisualTree = button },
            CellStyle = InlineGlyphButtonVisual.CenteredDataGridCellStyle(),
            Width = new DataGridLength(28),
            MinWidth = 28,
            MaxWidth = 28,
            CanUserResize = false,
            CanUserSort = false
        };
    }

    private static WpfBinding FavoriteBinding(IValueConverter converter)
        => new(nameof(ModelGridRow.IsFavorite)) { Converter = converter };

    private sealed class FavoriteGlyphConverter : IValueConverter
    {
        public static FavoriteGlyphConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is true ? "★" : "☆";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class FavoriteTooltipConverter : IValueConverter
    {
        public static FavoriteTooltipConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => Loc.T(value is true ? "Selector.RemoveFavoriteTooltip" : "Selector.AddFavoriteTooltip");

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class FavoriteForegroundConverter : IValueConverter
    {
        public static FavoriteForegroundConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => WpfApplication.Current?.TryFindResource(value is true ? "Accent" : "TextSoft")
               ?? DependencyProperty.UnsetValue;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    private sealed class AvailableVisibilityConverter : IValueConverter
    {
        public static AvailableVisibilityConverter Instance { get; } = new();

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => value is null ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
