using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

internal static class LifetimePageResponsiveCoordinator
{
    private const double CompactThreshold = 870;

    public static void ConfigureToolbar(
        Grid panel,
        LifetimeRangeSelector range,
        WpfComboBox model,
        WpfComboBox profile,
        WpfComboBox runtime,
        WpfButton reset)
    {
        var rangeFilter = (FrameworkElement)range.Parent;
        var modelFilter = (FrameworkElement)model.Parent;
        var profileFilter = (FrameworkElement)profile.Parent;
        var runtimeFilter = (FrameworkElement)runtime.Parent;
        var compact = false;

        void Apply(double width)
        {
            var useCompactLayout = width < CompactThreshold;
            if (useCompactLayout == compact && width > 0) return;
            compact = useCompactLayout;

            if (!compact)
            {
                SetColumns(panel,
                    GridLength.Auto,
                    GridLength.Auto,
                    GridLength.Auto,
                    GridLength.Auto,
                    new GridLength(1, GridUnitType.Star),
                    GridLength.Auto);
                Place(rangeFilter, 0, 0);
                Place(modelFilter, 0, 1);
                Place(profileFilter, 0, 2);
                Place(runtimeFilter, 0, 3);
                Place(reset, 0, 5);
                reset.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
                return;
            }

            SetColumns(panel,
                GridLength.Auto,
                GridLength.Auto,
                new GridLength(1, GridUnitType.Star),
                new GridLength(0),
                new GridLength(0),
                new GridLength(0));
            Place(rangeFilter, 0, 0);
            Place(reset, 0, 2);
            Place(modelFilter, 1, 0);
            Place(profileFilter, 1, 1);
            Place(runtimeFilter, 1, 2);
            reset.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;
        }

        panel.Loaded += (_, _) => Apply(panel.ActualWidth);
        panel.SizeChanged += (_, args) => Apply(args.NewSize.Width);
        Apply(0);
    }

    private static void SetColumns(Grid panel, params GridLength[] widths)
    {
        for (var index = 0; index < widths.Length; index++)
            panel.ColumnDefinitions[index].Width = widths[index];
    }

    private static void Place(FrameworkElement item, int row, int column)
    {
        Grid.SetRow(item, row);
        Grid.SetColumn(item, column);
    }
}
