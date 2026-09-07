using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Binding = System.Windows.Data.Binding;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public static partial class EndpointInspectionDialogFactory
{
    private static DataGridTemplateColumn ModelDetailsColumn()
    {
        var button = new FrameworkElementFactory(typeof(WpfButton));
        button.SetBinding(ContentControl.ContentProperty, new Binding(nameof(ModelDisplayRow.DetailsAction)));
        button.SetValue(FrameworkElement.ToolTipProperty, Loc.T("EndpointInspection.ExpandDetails"));
        button.SetValue(AutomationProperties.NameProperty, Loc.T("EndpointInspection.ExpandDetails"));
        button.SetValue(AutomationProperties.AutomationIdProperty, "EndpointModelDetailsButton");
        button.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(ModelDisplayRow.HasDetails)));
        InlineGlyphButtonVisual.ConfigureForDataGrid(button, 15);
        button.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler((sender, args) =>
        {
            if (sender is not WpfButton action
                || VisualTreeTraversal.FindAncestor<DataGridRow>(action) is not { } row) return;
            if (row.Item is not ModelDisplayRow model) return;
            var expanded = model.IsDetailsExpanded = !model.IsDetailsExpanded;
            row.DetailsVisibility = expanded ? Visibility.Visible : Visibility.Collapsed;
            action.Content = expanded ? "▾" : "▸";
            var label = Loc.T(expanded ? "EndpointInspection.CollapseDetails" : "EndpointInspection.ExpandDetails");
            action.ToolTip = label;
            AutomationProperties.SetName(action, label);
            args.Handled = true;
        }));
        return new DataGridTemplateColumn
        {
            Width = new DataGridLength(28),
            MinWidth = 28,
            MaxWidth = 28,
            CanUserSort = false,
            CellStyle = InlineGlyphButtonVisual.CenteredDataGridCellStyle(),
            CellTemplate = new DataTemplate { VisualTree = button }
        };
    }
}
