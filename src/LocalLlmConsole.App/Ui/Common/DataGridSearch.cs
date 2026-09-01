using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public sealed record DataGridSearchControls(Grid Root, WpfTextBox Input, WpfButton Toggle);

public static class DataGridSearch
{
    public static DataGridSearchControls Create(
        DataGrid dataGrid,
        Func<object, string> searchText,
        string automationName)
    {
        ArgumentNullException.ThrowIfNull(dataGrid);
        ArgumentNullException.ThrowIfNull(searchText);
        ArgumentException.ThrowIfNullOrWhiteSpace(automationName);

        var root = new Grid
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var input = new WpfTextBox
        {
            Width = 168,
            Height = 28,
            MinHeight = 28,
            Margin = new Thickness(0),
            Padding = new Thickness(8, 2, 8, 2),
            VerticalContentAlignment = VerticalAlignment.Center,
            Visibility = Visibility.Collapsed,
            ToolTip = automationName
        };
        AutomationProperties.SetName(input, automationName);
        root.Children.Add(input);
        var toggle = new WpfButton
        {
            Content = "\uE721",
            FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"),
            FontSize = 13,
            Width = 28,
            Height = 28,
            MinWidth = 28,
            MinHeight = 28,
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(0),
            ToolTip = automationName
        };
        VisualRole.SetButtonRole(toggle, VisualRole.Quiet);
        AutomationProperties.SetName(toggle, automationName);
        Grid.SetColumn(toggle, 1);
        root.Children.Add(toggle);

        var view = CollectionViewSource.GetDefaultView(dataGrid.ItemsSource);
        var previousFilter = view.Filter;
        void ApplyFilter()
        {
            var terms = input.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            view.Filter = item => (previousFilter?.Invoke(item) ?? true)
                                  && terms.All(term => searchText(item).Contains(term, StringComparison.OrdinalIgnoreCase));
            view.Refresh();
        }

        input.TextChanged += (_, _) => ApplyFilter();
        input.PreviewKeyDown += (_, args) =>
        {
            if (args.Key != Key.Escape) return;
            input.Text = "";
            input.Visibility = Visibility.Collapsed;
            toggle.Focus();
            args.Handled = true;
        };
        toggle.Click += (_, _) =>
        {
            if (input.Visibility != Visibility.Visible)
            {
                input.Visibility = Visibility.Visible;
                input.Focus();
                return;
            }
            input.Text = "";
            input.Visibility = Visibility.Collapsed;
        };
        root.Unloaded += (_, _) =>
        {
            view.Filter = previousFilter;
            view.Refresh();
        };
        return new DataGridSearchControls(root, input, toggle);
    }
}
