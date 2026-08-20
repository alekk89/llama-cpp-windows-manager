using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace LocalLlmConsole;

internal static class OverviewPageResponsiveCoordinator
{
    public static void ConfigureModelBar(
        Grid modelBar,
        TextBlock modelLabel,
        WpfComboBox modelCombo,
        TextBlock profileLabel,
        WpfComboBox launchProfileCombo,
        WpfButton loadButton)
    {
        modelBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        modelBar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var narrow = false;

        void Apply(double availableWidth)
        {
            var shouldUseNarrowLayout = availableWidth < 760;
            if (shouldUseNarrowLayout == narrow && availableWidth > 0) return;
            narrow = shouldUseNarrowLayout;
            if (!narrow)
            {
                SetColumns(modelBar, GridLength.Auto, new GridLength(240), new GridLength(16), GridLength.Auto, new GridLength(220), new GridLength(1, GridUnitType.Star), GridLength.Auto);
                Place(modelLabel, 0, 0);
                Place(modelCombo, 0, 1);
                Place(profileLabel, 0, 3);
                Place(launchProfileCombo, 0, 4);
                Place(loadButton, 0, 6);
                Grid.SetRowSpan(loadButton, 1);
                modelCombo.Width = 240;
                launchProfileCombo.Width = 220;
                profileLabel.Margin = new Thickness(0, 0, 8, 0);
                launchProfileCombo.Margin = new Thickness(0);
                return;
            }

            SetColumns(modelBar, GridLength.Auto, new GridLength(1, GridUnitType.Star), new GridLength(12), GridLength.Auto, new GridLength(0), new GridLength(0), new GridLength(0));
            Place(modelLabel, 0, 0);
            Place(modelCombo, 0, 1);
            Place(profileLabel, 1, 0);
            Place(launchProfileCombo, 1, 1);
            Place(loadButton, 0, 3);
            Grid.SetRowSpan(loadButton, 2);
            modelCombo.Width = double.NaN;
            launchProfileCombo.Width = double.NaN;
            profileLabel.Margin = new Thickness(0, 8, 8, 0);
            launchProfileCombo.Margin = new Thickness(0, 8, 0, 0);
        }

        modelBar.Loaded += (_, _) => Apply(modelBar.ActualWidth);
        modelBar.SizeChanged += (_, args) => Apply(args.NewSize.Width);
        Apply(0);
    }

    private static void SetColumns(Grid grid, params GridLength[] widths)
    {
        for (var index = 0; index < widths.Length; index++)
            grid.ColumnDefinitions[index].Width = widths[index];
    }

    private static void Place(FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
    }

    public static void ConfigureLoadButton(WpfButton button)
    {
        button.MinWidth = 94;
        button.MinHeight = 30;
        button.Margin = new Thickness(0);
        button.HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch;
        button.VerticalAlignment = VerticalAlignment.Center;
    }

    public static void ConfigureMetricLayout(Grid dashboard)
    {
        var cards = dashboard.Children.OfType<Border>().ToArray();
        var appliedColumns = 0;
        var appliedVisibleCards = -1;

        void Apply(double availableWidth)
        {
            var visibleCards = cards.Where(card => card.Visibility == Visibility.Visible).ToArray();
            var columns = OverviewResponsiveLayout.MetricColumnCount(availableWidth, visibleCards.Length);
            if (columns == appliedColumns && visibleCards.Length == appliedVisibleCards) return;
            appliedColumns = columns;
            appliedVisibleCards = visibleCards.Length;

            dashboard.ColumnDefinitions.Clear();
            dashboard.RowDefinitions.Clear();
            for (var column = 0; column < columns; column++)
                dashboard.ColumnDefinitions.Add(new ColumnDefinition());
            var rows = (int)Math.Ceiling(visibleCards.Length / (double)columns);
            for (var row = 0; row < rows; row++)
                dashboard.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            for (var index = 0; index < visibleCards.Length; index++)
            {
                var column = index % columns;
                Grid.SetRow(visibleCards[index], index / columns);
                Grid.SetColumn(visibleCards[index], column);
                visibleCards[index].Margin = new Thickness(column == 0 ? 0 : 5, 0, column == columns - 1 ? 0 : 5, 8);
            }
        }

        foreach (var card in cards)
            card.IsVisibleChanged += (_, _) => Apply(dashboard.ActualWidth);
        dashboard.Loaded += (_, _) => Apply(dashboard.ActualWidth);
        dashboard.SizeChanged += (_, args) => Apply(args.NewSize.Width);
        Apply(0);
    }

}
