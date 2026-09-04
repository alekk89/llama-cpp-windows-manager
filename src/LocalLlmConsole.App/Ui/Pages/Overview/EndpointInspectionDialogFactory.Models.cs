using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using Binding = System.Windows.Data.Binding;
using Control = System.Windows.Controls.Control;
using FlowDirection = System.Windows.FlowDirection;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace LocalLlmConsole;

public static partial class EndpointInspectionDialogFactory
{
    private sealed record ModelDisplayRow(string Id, string Name, string Profile, string Owner, string Context, string Parameters, string Size)
    {
        public bool HasId => !string.IsNullOrWhiteSpace(Id);
        public string DisplayName => Name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase) && (Name.Contains('\\') || Name.StartsWith('/'))
            ? RuntimeDirectAliasService.ShortModelId(Name) : Name;
        public Visibility NameVisibility => string.IsNullOrWhiteSpace(DisplayName) || DisplayName == Id ? Visibility.Collapsed : Visibility.Visible;
    }

    private static WpfBorder ModelsCard(EndpointInspectionReport report, Action<string> copyToClipboard)
    {
        if (report.Models.Count == 0)
            return Card(
                report.Kind == EndpointInspectionKind.Gateway
                    ? Loc.T("EndpointInspection.AdvertisedModels")
                    : Loc.T("EndpointInspection.EndpointModel"),
                Muted(Loc.T("EndpointInspection.NoModels")));

        var rows = report.Models.Select(model =>
        {
            var context = report.Kind == EndpointInspectionKind.Gateway ? model.ConfiguredContext : model.TrainingContext;
            return new ModelDisplayRow(model.Id, model.Name, Empty(model.Profile), Empty(model.Owner),
                context.HasValue ? Tokens(context.Value) : "—",
                model.ParameterCount.HasValue ? CompactCount(model.ParameterCount.Value) : "—",
                model.SizeBytes.HasValue ? DisplayFormatService.Bytes(model.SizeBytes.Value) : "—");
        });
        var grid = Table(rows,
            (Loc.T("EndpointInspection.ModelIdName"), nameof(ModelDisplayRow.Id), 1.85),
            (Loc.T("EndpointInspection.Profile"), nameof(ModelDisplayRow.Profile), .9),
            (Loc.T("EndpointInspection.Owner"), nameof(ModelDisplayRow.Owner), .8),
            (report.Kind == EndpointInspectionKind.Gateway
                ? Loc.T("EndpointInspection.ContextSize")
                : Loc.T("EndpointInspection.TrainingContext"), nameof(ModelDisplayRow.Context), .65),
            (Loc.T("EndpointInspection.Parameters"), nameof(ModelDisplayRow.Parameters), .65),
            (Loc.T("Models.Col.Size"), nameof(ModelDisplayRow.Size), .6));
        // Size metadata to its headers so the copy action cannot compress labels.
        foreach (var column in grid.Columns.Skip(1))
        {
            var header = new TextBlock { Text = column.Header?.ToString() ?? "", FontSize = grid.FontSize, FontWeight = FontWeights.SemiBold };
            header.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            column.Width = new DataGridLength(header.DesiredSize.Width + 32);
        }
        var identityHeader = new TextBlock { Text = grid.Columns[0].Header?.ToString() ?? "", FontSize = grid.FontSize };
        identityHeader.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        ScrollViewer.SetHorizontalScrollBarVisibility(grid, ScrollBarVisibility.Auto);
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.ExcludeHeader;
        AutomationProperties.SetAutomationId(grid, "EndpointModelsTable");
        grid.Columns[0] = new DataGridTemplateColumn
        {
            Header = grid.Columns[0].Header,
            Width = grid.Columns[0].Width,
            MinWidth = identityHeader.DesiredSize.Width + 28,
            SortMemberPath = nameof(ModelDisplayRow.Id),
            ClipboardContentBinding = new Binding(nameof(ModelDisplayRow.Id)),
            CellTemplate = ModelIdentityTemplate()
        };

        var status = new TextBlock { Foreground = ResourceBrush("TextSoft"), FontSize = 11.5 };
        AutomationProperties.SetAutomationId(status, "EndpointModelCopyStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var button = new FrameworkElementFactory(typeof(WpfButton));
        button.SetValue(ContentControl.ContentProperty, "⧉");
        button.SetValue(FrameworkElement.ToolTipProperty, Loc.T("EndpointInspection.CopyModelId"));
        button.SetValue(AutomationProperties.AutomationIdProperty, "EndpointModelCopyIdButton");
        button.SetValue(AutomationProperties.NameProperty, Loc.T("EndpointInspection.CopyModelId"));
        button.SetValue(Control.PaddingProperty, new Thickness(7, 1, 7, 1));
        button.SetValue(FrameworkElement.MinHeightProperty, 27d);
        button.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(ModelDisplayRow.HasId)));
        button.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is not FrameworkElement { DataContext: ModelDisplayRow { HasId: true } row }) return;
            try
            {
                copyToClipboard(row.Id);
                status.Text = Loc.T("EndpointInspection.Copied");
            }
            catch
            {
                status.Text = Loc.T("EndpointInspection.CopyFailed");
            }
        }));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Width = DataGridLength.Auto,
            CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = button }
        });
        var content = new StackPanel();
        content.Children.Add(grid);
        content.Children.Add(status);
        return Card(report.Kind == EndpointInspectionKind.Gateway
            ? Loc.T("EndpointInspection.AdvertisedModelsCount", report.Models.Count)
            : Loc.T("EndpointInspection.EndpointModel"), content);
    }

    private static DataTemplate ModelIdentityTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        var id = ModelText(nameof(ModelDisplayRow.Id), "EndpointModelIdText");
        id.SetValue(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight);
        panel.AppendChild(id);
        var name = ModelText(nameof(ModelDisplayRow.DisplayName), "EndpointModelNameText");
        name.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ModelDisplayRow.Name)));
        name.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(ModelDisplayRow.NameVisibility)));
        name.SetResourceReference(Control.ForegroundProperty, "TextSoft");
        panel.AppendChild(name);
        return new DataTemplate { VisualTree = panel };
    }

    private static FrameworkElementFactory ModelText(string property, string automationId)
    {
        var text = new FrameworkElementFactory(typeof(WpfTextBox));
        text.SetValue(FrameworkElement.StyleProperty, (Style)WpfApplication.Current.Resources["EndpointReportSelectableText"]);
        text.SetValue(WpfTextBox.IsReadOnlyProperty, true);
        text.SetValue(WpfTextBox.TextWrappingProperty, TextWrapping.Wrap);
        text.SetValue(AutomationProperties.AutomationIdProperty, automationId);
        text.SetBinding(WpfTextBox.TextProperty, new Binding(property) { Mode = BindingMode.OneWay });
        return text;
    }
}
