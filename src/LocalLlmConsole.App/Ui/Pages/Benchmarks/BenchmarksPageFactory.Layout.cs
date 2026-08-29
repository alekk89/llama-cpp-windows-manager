using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using TextBox = System.Windows.Controls.TextBox;
using Button = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public static partial class BenchmarksPageFactory
{
    private static Button Button(string text, RoutedEventHandler handler)
    {
        var button = new Button { Content = text, MinWidth = 92, Margin = new Thickness(0, 0, 8, 6), Padding = new Thickness(12, 5, 12, 5) };
        button.Click += handler;
        AutomationProperties.SetName(button, text);
        return button;
    }

    private static TextBlock Heading(string title, string subtitle)
    {
        var block = new TextBlock { Text = $"{title}\n{subtitle}", FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return block;
    }

    private static TextBlock Muted(string text)
    {
        var block = new TextBlock { Text = text, Margin = new Thickness(8, 5, 8, 5), VerticalAlignment = VerticalAlignment.Center };
        block.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        return block;
    }

    private static ComboBox Combo(string name)
    {
        var combo = new ComboBox
        {
            Height = 28,
            MinHeight = 28,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 4, 1),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        AutomationProperties.SetName(combo, name);
        return combo;
    }

    private static TextBox Text(string value, double minWidth = 120)
        => new()
        {
            Text = value,
            Height = 28,
            MinHeight = 28,
            MinWidth = Math.Max(72, minWidth),
            Margin = new Thickness(0, 0, 4, 1),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };

    private static CheckBox Check(string text, bool value = false, string? tooltip = null)
    {
        var checkBox = new CheckBox
        {
            Content = text,
            IsChecked = value,
            Height = 28,
            MinHeight = 28,
            Margin = new Thickness(4, 0, 10, 1),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = tooltip
        };
        AutomationProperties.SetName(checkBox, text);
        if (!string.IsNullOrWhiteSpace(tooltip))
        {
            AutomationProperties.SetHelpText(checkBox, tooltip);
            ToolTipService.SetShowDuration(checkBox, 30000);
        }
        return checkBox;
    }

    private static ComboBox Choice(string name, params string[] values)
    {
        var combo = Combo(name);
        combo.ItemsSource = values;
        combo.SelectedIndex = 0;
        return combo;
    }

    private static ComboBox MatrixChoice(string name, params string[] values)
    {
        var combo = Choice(name, ["", .. values]);
        combo.IsEditable = true;
        combo.StaysOpenOnEdit = true;
        return combo;
    }

    private static ComboBox BooleanChoice(string name, string enabledName, string disabledName, bool value = true)
    {
        var combo = Combo(name);
        var items = new[]
        {
            new BenchmarkBooleanItem(true, enabledName),
            new BenchmarkBooleanItem(false, disabledName)
        };
        combo.ItemsSource = items;
        combo.SelectedItem = items.First(item => item.Value == value);
        return combo;
    }

    private static Grid GuidedVariablesGrid()
    {
        var grid = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        return grid;
    }

    private static void AddGuidedVariable(
        Grid grid,
        int row,
        string name,
        string inheritedDescription,
        CheckBox enabled,
        FrameworkElement values,
        string valueHelp)
    {
        var gridRow = row / 2;
        var gridColumn = row % 2 == 0 ? 0 : 2;
        while (grid.RowDefinitions.Count <= gridRow)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var item = new Grid { Margin = new Thickness(0, 3, 0, 6) };
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var label = new TextBlock { Text = name, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var help = $"{inheritedDescription} {valueHelp}";
        label.Margin = new Thickness(0, 0, 10, 0);
        label.ToolTip = help;
        values.Margin = new Thickness(0, 0, 4, 0);
        values.ToolTip = help;
        item.ToolTip = help;
        ToolTipService.SetShowDuration(item, 30000);
        AutomationProperties.SetName(values, $"{name} values to compare");
        AutomationProperties.SetHelpText(values, help);
        AutomationProperties.SetName(enabled, $"Compare {name}");
        Grid.SetColumn(values, 1);
        item.Children.Add(label);
        item.Children.Add(values);
        Grid.SetRow(item, gridRow);
        Grid.SetColumn(item, gridColumn);
        grid.Children.Add(item);
    }

    private static void AddGuidedField(
        Grid grid,
        int index,
        string name,
        FrameworkElement field,
        string? help = null)
    {
        help = BenchmarkFieldDescriptions.Get(name, help);
        var gridRow = index / 2;
        var gridColumn = index % 2 == 0 ? 0 : 2;
        while (grid.RowDefinitions.Count <= gridRow)
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var item = new Grid { Margin = new Thickness(0, 3, 0, 6), ToolTip = help };
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        ToolTipService.SetShowDuration(item, 30000);

        var label = new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            ToolTip = help
        };
        AutomationProperties.SetHelpText(label, help);

        field.Height = 28;
        field.MinHeight = 28;
        field.MinWidth = Math.Max(field.MinWidth, 72);
        field.Margin = new Thickness(0, 0, 4, 0);
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        field.VerticalAlignment = VerticalAlignment.Center;
        field.ToolTip = help;
        ToolTipService.SetShowDuration(field, 30000);
        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(field)))
            AutomationProperties.SetName(field, name);
        AutomationProperties.SetHelpText(field, help);

        Grid.SetColumn(field, 1);
        item.Children.Add(label);
        item.Children.Add(field);
        Grid.SetRow(item, gridRow);
        Grid.SetColumn(item, gridColumn);
        grid.Children.Add(item);
    }

    private static void AddGuidedWideField(
        System.Windows.Controls.Panel panel,
        string name,
        FrameworkElement field,
        string? help = null)
    {
        help = BenchmarkFieldDescriptions.Get(name, help);
        var item = new StackPanel { Margin = new Thickness(0, 3, 0, 6), ToolTip = help };
        ToolTipService.SetShowDuration(item, 30000);
        var label = new TextBlock
        {
            Text = name,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 4),
            TextWrapping = TextWrapping.Wrap,
            ToolTip = help
        };
        AutomationProperties.SetHelpText(label, help);
        field.Margin = new Thickness(0, 0, 4, 0);
        field.HorizontalAlignment = HorizontalAlignment.Stretch;
        field.ToolTip = help;
        ToolTipService.SetShowDuration(field, 30000);
        if (string.IsNullOrWhiteSpace(AutomationProperties.GetName(field)))
            AutomationProperties.SetName(field, name);
        AutomationProperties.SetHelpText(field, help);
        item.Children.Add(label);
        item.Children.Add(field);
        panel.Children.Add(item);
    }

    internal static string VariableCombinationSummary(
        bool compareContexts,
        string? contexts,
        bool compareBatches,
        string? batches)
        => VariableCombinationSummary(
            (compareContexts, contexts, "context length", "context lengths"),
            (compareBatches, batches, "batch size", "batch sizes"));

    private static string VariableCombinationSummary(
        params (bool Enabled, string? Values, string Singular, string Plural)[] dimensions)
    {
        var enabled = dimensions.Where(dimension => dimension.Enabled).ToArray();
        if (enabled.Any(dimension => CountValues(dimension.Values) == 0))
            return "Enter at least one value in every enabled row. Separate multiple values with commas.";
        if (enabled.Length == 0)
            return "1 launch configuration per profile · all launch settings inherited.";
        long total = 1;
        var factors = new List<string>(enabled.Length);
        foreach (var dimension in enabled)
        {
            var count = CountValues(dimension.Values);
            total *= count;
            factors.Add($"{count} {(count == 1 ? dimension.Singular : dimension.Plural)}");
        }
        return $"{string.Join(" × ", factors)} = {total} temporary launch configuration{(total == 1 ? "" : "s")} per profile.";
    }

    private static int CountValues(string? value)
        => (value ?? "")
            .Split((value ?? "").Contains(';') ? [';'] : [','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

}
