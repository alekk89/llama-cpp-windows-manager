using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public static partial class EndpointInspectionDialogFactory
{
    private sealed record DisplayRow(string C1, string C2, string C3, string C4, string C5 = "", string C6 = "");

    public static void Show(WpfWindow owner, EndpointInspectionReport report, string apiKey, Action<string> copyToClipboard)
        => Create(owner, report, apiKey, copyToClipboard).ShowDialog();

    public static WpfWindow Create(
        WpfWindow owner,
        EndpointInspectionReport report,
        string apiKey = "",
        Action<string>? copyToClipboard = null)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(report);

        var dialog = new WpfWindow
        {
            Title = Loc.T("EndpointInspection.DialogTitle", report.Title),
            Width = 760,
            Height = 480,
            MinWidth = 620,
            MinHeight = 320,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = System.Windows.Media.Brushes.Transparent,
            AllowsTransparency = true,
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.CanResize,
            ShowInTaskbar = false,
            FlowDirection = owner.FlowDirection
        };
        if (owner.IsVisible)
            dialog.Owner = owner;
        dialog.KeyDown += (_, args) =>
        {
            if (args.Key == Key.Escape) dialog.Close();
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());

        var header = Header(dialog, report);
        layout.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var copy = copyToClipboard ?? System.Windows.Clipboard.SetText;
        body.Children.Add(EndpointInspectionCopyBarFactory.Create(
            report,
            apiKey,
            copy));
        body.Children.Add(ModelsCard(report, copy));
        var settings = SettingsFields(report);
        if (settings.Count > 0)
            body.Children.Add(Card(Loc.T(report.Kind == EndpointInspectionKind.Gateway
                ? "EndpointInspection.ManagerRouting" : "EndpointInspection.ServerDefaults"), FieldsGrid(settings.ToArray())));
        if (report.Kind == EndpointInspectionKind.Gateway && report.RunningModels.Count > 0)
            body.Children.Add(RunningModelsCard(report.RunningModels));
        if (report.UnavailableSources.Count > 0)
        {
            body.Children.Add(new Expander
            {
                Header = Loc.T("EndpointInspection.PartialDetails"),
                Foreground = ResourceBrush("Warning"),
                Content = Muted(string.Join(Environment.NewLine, report.UnavailableSources)),
                Margin = new Thickness(9, 4, 9, 4)
            });
        }

        var scroll = new ScrollViewer
        {
            Content = body,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        Grid.SetRow(scroll, 1);
        layout.Children.Add(scroll);

        dialog.Content = new WpfBorder
        {
            Background = ResourceBrush("PanelBack"),
            BorderBrush = ResourceBrush("PanelBorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Child = layout
        };
        return dialog;
    }

    private static Grid Header(WpfWindow dialog, EndpointInspectionReport report)
    {
        var header = new Grid { Margin = new Thickness(1, 0, 0, 1) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = report.Kind == EndpointInspectionKind.Gateway
                ? Loc.T("EndpointInspection.GatewayReport")
                : Loc.T("EndpointInspection.DirectReport"),
            FontSize = 16,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain")
        });
        header.Children.Add(copy);
        var close = Button(Loc.T("Common.Close"));
        close.MinWidth = 64;
        close.ToolTip = Loc.T("EndpointInspection.CloseTooltip");
        close.Click += (_, _) => dialog.Close();
        Grid.SetColumn(close, 1);
        header.Children.Add(close);
        header.MouseLeftButtonDown += (_, args) =>
        {
            if (args.ChangedButton == MouseButton.Left && args.ButtonState == MouseButtonState.Pressed)
                dialog.DragMove();
        };
        return header;
    }

    private static WpfBorder RunningModelsCard(IReadOnlyList<EndpointInspectionRunningModel> models)
    {
        if (models.Count == 0)
            return Card(Loc.T("EndpointInspection.LoadedThroughManager"), Muted(Loc.T("EndpointInspection.NoLoadedRuntime")));
        var rows = models.Select(model => new DisplayRow(
            Empty(model.Name, model.Id),
            string.Join(" · ", new[] { model.Status, model.Runtime }.Where(value => !string.IsNullOrWhiteSpace(value)))
                + Environment.NewLine + model.Endpoint, "", ""));
        return Card(Loc.T("EndpointInspection.LoadedThroughManagerCount", models.Count), Table(
            rows,
            (Loc.T("Overview.SessionsCol.Model"), nameof(DisplayRow.C1), 1),
            (Loc.T("EndpointInspection.RunningDetails"), nameof(DisplayRow.C2), 1.6)));
    }

    private static WpfBorder FieldsGrid(params (string Label, string Value)[] fields)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(112) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var index = 0; index < fields.Length; index++)
        {
            var row = index / 2;
            if (grid.RowDefinitions.Count <= row)
            {
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto, MinHeight = 33 });
                var stripe = new WpfBorder
                {
                    Background = ResourceBrush(row % 2 == 0 ? "GridRowBack" : "GridRowAlt"),
                    BorderBrush = ResourceBrush("PanelBorder"),
                    BorderThickness = new Thickness(0, 0, 0, 1)
                };
                Grid.SetRow(stripe, row);
                Grid.SetColumnSpan(stripe, 5);
                grid.Children.Add(stripe);
            }
            var column = index % 2 == 0 ? 0 : 3;
            var label = new TextBlock
            {
                Text = fields[index].Label,
                TextWrapping = TextWrapping.Wrap,
                Foreground = ResourceBrush("TextMuted"),
                Margin = new Thickness(10, 6, 8, 6),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            grid.Children.Add(label);
            var value = SelectableText(fields[index].Value, "TextMain");
            value.Margin = new Thickness(0, 6, 10, 6);
            value.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetRow(value, row);
            Grid.SetColumn(value, column + 1);
            grid.Children.Add(value);
        }
        return new WpfBorder
        {
            Background = ResourceBrush("SurfaceRaised"),
            BorderBrush = ResourceBrush("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(7),
            Margin = new Thickness(0, 7, 0, 8),
            Child = grid
        };
    }

    private static DataGrid Table<T>(
        IEnumerable<T> rows,
        params (string Header, string Binding, double Width)[] columns)
    {
        var grid = new DataGrid();
        PageSectionFactory.PolishGrid(grid);
        var textStyle = new Style(typeof(TextBlock), (Style)WpfApplication.Current.Resources["GridCellText"]);
        textStyle.Setters.Add(new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap));
        textStyle.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.None));
        textStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6, 5, 6, 5)));
        foreach (var column in columns)
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = column.Header,
                Binding = new System.Windows.Data.Binding(column.Binding),
                Width = new DataGridLength(column.Width, DataGridLengthUnitType.Star),
                MinWidth = 100,
                ElementStyle = textStyle
            });
        grid.RowHeight = double.NaN;
        grid.ColumnHeaderHeight = 36;
        grid.ItemsSource = new ObservableCollection<T>(rows);
        grid.IsReadOnly = true;
        grid.CanUserAddRows = false;
        grid.CanUserDeleteRows = false;
        grid.SelectionMode = DataGridSelectionMode.Extended;
        grid.SelectionUnit = DataGridSelectionUnit.CellOrRowHeader;
        grid.ClipboardCopyMode = DataGridClipboardCopyMode.IncludeHeader;
        grid.MaxHeight = 180;
        grid.MinHeight = 36;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        return grid;
    }

    private static WpfBorder Card(string title, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain"),
            Margin = new Thickness(1, 2, 0, 4)
        });
        panel.Children.Add(content is DataGrid grid ? PageSectionFactory.GridFrame(grid) : content);
        return new WpfBorder
        {
            Background = ResourceBrush("PanelBack"),
            BorderBrush = ResourceBrush("PanelBorder"),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0, 4, 0, 4),
            Margin = new Thickness(0),
            Child = panel
        };
    }

    private static WpfTextBox Muted(string text)
        => SelectableText(text, "TextSoft");

    private static WpfTextBox SelectableText(string text, string foregroundKey) => new()
    {
        Text = text,
        IsReadOnly = true,
        Style = (Style)WpfApplication.Current.Resources["EndpointReportSelectableText"],
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 1, 0, 2),
        Foreground = ResourceBrush(foregroundKey),
        VerticalContentAlignment = VerticalAlignment.Top
    };

    private static WpfButton Button(string text) => new()
    {
        Content = text,
        MinWidth = 74,
        Height = 27,
        MinHeight = 27,
        Padding = new Thickness(9, 1, 9, 1),
        Margin = new Thickness(6, 0, 0, 0)
    };

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];

}
