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
    private sealed record ModelDisplayRow(string Id, string Name, string Context, string Size, string Parameters, string Details)
    {
        public bool IsDetailsExpanded { get; set; }
        public string DetailsAction => IsDetailsExpanded ? "▾" : "▸";
        public bool HasDetails => NameVisibility == Visibility.Visible || !string.IsNullOrWhiteSpace(Details);
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
            var details = new List<string>();
            void Add(string key, string value) => details.Add($"{Loc.T(key)}: {value}");
            if (!string.IsNullOrWhiteSpace(model.Profile)) Add("EndpointInspection.Profile", model.Profile);
            var context = report.Kind == EndpointInspectionKind.Gateway
                ? model.ConfiguredContext
                : report.Defaults?.ContextSize ?? model.ConfiguredContext;
            // A model's training limit is not the context allocated by the running server.
            if (report.Kind == EndpointInspectionKind.DirectModel && model.TrainingContext.HasValue)
                Add("EndpointInspection.TrainingContext", Tokens(model.TrainingContext.Value));
            return new ModelDisplayRow(model.Id, model.Name,
                context.HasValue ? Tokens(context.Value) : "—",
                model.SizeBytes.HasValue ? DisplayFormatService.Bytes(model.SizeBytes.Value) : "—",
                model.ParameterCount.HasValue ? CompactCount(model.ParameterCount.Value) : "—",
                string.Join(Environment.NewLine, details));
        });
        var grid = Table(rows,
            (Loc.T("EndpointInspection.ModelId"), nameof(ModelDisplayRow.Id), 1.8),
            (Loc.T("EndpointInspection.Context"), nameof(ModelDisplayRow.Context), 1),
            (Loc.T("Models.Col.Size"), nameof(ModelDisplayRow.Size), .65),
            (Loc.T("EndpointInspection.Parameters"), nameof(ModelDisplayRow.Parameters), .65));
        grid.Columns[2].Visibility = report.Models.Any(model => model.SizeBytes.HasValue) ? Visibility.Visible : Visibility.Collapsed;
        grid.Columns[3].Visibility = report.Models.Any(model => model.ParameterCount.HasValue) ? Visibility.Visible : Visibility.Collapsed;
        foreach (var column in grid.Columns.Skip(1))
        {
            var label = new TextBlock { Text = column.Header?.ToString(), FontSize = 11.5, FontWeight = FontWeights.SemiBold };
            label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            column.MinWidth = label.DesiredSize.Width + 28;
        }
        grid.MinRowHeight = 33;
        grid.LoadingRow += (_, args) => args.Row.DetailsVisibility = args.Row.Item is ModelDisplayRow { IsDetailsExpanded: true }
            ? Visibility.Visible : Visibility.Collapsed;
        grid.RowDetailsVisibilityMode = DataGridRowDetailsVisibilityMode.Collapsed;
        grid.AreRowDetailsFrozen = true;
        grid.RowDetailsTemplate = ModelDetailsTemplate();
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

        var status = new TextBlock { Foreground = ResourceBrush("TextSoft"), FontSize = 11.5, Visibility = Visibility.Collapsed };
        AutomationProperties.SetAutomationId(status, "EndpointModelCopyStatus");
        AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
        var button = new FrameworkElementFactory(typeof(WpfButton));
        button.SetValue(ContentControl.ContentProperty, "⧉");
        button.SetValue(FrameworkElement.ToolTipProperty, Loc.T("EndpointInspection.CopyModelId"));
        button.SetValue(AutomationProperties.AutomationIdProperty, "EndpointModelCopyIdButton");
        button.SetValue(AutomationProperties.NameProperty, Loc.T("EndpointInspection.CopyModelId"));
        InlineGlyphButtonVisual.ConfigureForDataGrid(button);
        button.SetBinding(UIElement.IsEnabledProperty, new Binding(nameof(ModelDisplayRow.HasId)));
        button.AddHandler(WpfButton.ClickEvent, new RoutedEventHandler((sender, _) =>
        {
            if (sender is not FrameworkElement { DataContext: ModelDisplayRow { HasId: true } row }) return;
            try
            {
                copyToClipboard(row.Id);
                status.Text = Loc.T("EndpointInspection.Copied");
                status.Visibility = Visibility.Visible;
            }
            catch
            {
                status.Text = Loc.T("EndpointInspection.CopyFailed");
                status.Visibility = Visibility.Visible;
            }
        }));
        grid.Columns.Add(new DataGridTemplateColumn
        {
            Width = new DataGridLength(40),
            MinWidth = 40,
            CellStyle = InlineGlyphButtonVisual.CenteredDataGridCellStyle(),
            CanUserSort = false,
            CellTemplate = new DataTemplate { VisualTree = button }
        });
        grid.Columns.Add(ModelDetailsColumn());
        var content = new StackPanel();
        content.Children.Add(PageSectionFactory.GridFrame(grid));
        content.Children.Add(status);
        return Card(report.Kind == EndpointInspectionKind.Gateway
            ? Loc.T("EndpointInspection.AdvertisedModelsCount", report.Models.Count)
            : Loc.T("EndpointInspection.EndpointModel"), content);
    }

    private static DataTemplate ModelIdentityTemplate()
    {
        var id = ModelText(nameof(ModelDisplayRow.Id), "EndpointModelIdText");
        id.SetValue(FrameworkElement.FlowDirectionProperty, FlowDirection.LeftToRight);
        id.SetValue(WpfTextBox.TextWrappingProperty, TextWrapping.NoWrap);
        id.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ModelDisplayRow.Id)));
        id.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
        return new DataTemplate { VisualTree = id };
    }

    private static DataTemplate ModelDetailsTemplate()
    {
        var panel = new FrameworkElementFactory(typeof(StackPanel));
        panel.SetValue(FrameworkElement.MarginProperty, new Thickness(12, 4, 12, 8));
        var name = ModelText(nameof(ModelDisplayRow.DisplayName), "EndpointModelNameText");
        name.SetBinding(FrameworkElement.ToolTipProperty, new Binding(nameof(ModelDisplayRow.Name)));
        name.SetBinding(UIElement.VisibilityProperty, new Binding(nameof(ModelDisplayRow.NameVisibility)));
        name.SetResourceReference(Control.ForegroundProperty, "TextSoft");
        panel.AppendChild(name);
        panel.AppendChild(ModelText(nameof(ModelDisplayRow.Details), "EndpointModelDetailsText"));
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
