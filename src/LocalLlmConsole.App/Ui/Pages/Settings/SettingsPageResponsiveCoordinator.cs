using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

internal static class SettingsPageResponsiveCoordinator
{
    internal const double SingleColumnThreshold = 820;

    public static void Configure(
        Grid columns,
        StackPanel left,
        StackPanel right,
        IReadOnlyList<FrameworkElement> sections)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(sections);

        bool? singleColumn = null;

        void Apply(double availableWidth)
        {
            if (availableWidth <= 0) return;
            var useSingleColumn = availableWidth < SingleColumnThreshold;
            if (singleColumn == useSingleColumn) return;
            singleColumn = useSingleColumn;
            ApplyLayout(columns, left, right, sections, useSingleColumn);
        }

        columns.Loaded += (_, _) => Apply(columns.ActualWidth);
        columns.SizeChanged += (_, args) => Apply(args.NewSize.Width);
    }

    internal static void ApplyLayout(
        Grid columns,
        StackPanel left,
        StackPanel right,
        IReadOnlyList<FrameworkElement> sections,
        bool useSingleColumn)
    {
        left.Children.Clear();
        right.Children.Clear();
        if (useSingleColumn)
        {
            columns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
            columns.ColumnDefinitions[1].Width = new GridLength(0);
            left.Margin = new Thickness(0);
            right.Margin = new Thickness(0);
            right.Visibility = Visibility.Collapsed;
            foreach (var section in sections)
                left.Children.Add(section);
            return;
        }

        columns.ColumnDefinitions[0].Width = new GridLength(1, GridUnitType.Star);
        columns.ColumnDefinitions[1].Width = new GridLength(1, GridUnitType.Star);
        left.Margin = new Thickness(0, 0, 6, 0);
        right.Margin = new Thickness(6, 0, 0, 0);
        right.Visibility = Visibility.Visible;
        for (var index = 0; index < sections.Count; index++)
            (index % 2 == 0 ? left : right).Children.Add(sections[index]);
    }
}
