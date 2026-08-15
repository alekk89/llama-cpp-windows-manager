using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfApplication = System.Windows.Application;
using WpfBorder = System.Windows.Controls.Border;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfWindow = System.Windows.Window;

namespace LocalLlmConsole;

public static class EndpointInspectionDialogFactory
{
    private sealed record DisplayRow(string C1, string C2, string C3, string C4, string C5 = "", string C6 = "");

    public static void Show(WpfWindow owner, EndpointInspectionReport report)
        => Create(owner, report).ShowDialog();

    public static WpfWindow Create(WpfWindow owner, EndpointInspectionReport report)
    {
        ArgumentNullException.ThrowIfNull(owner);
        ArgumentNullException.ThrowIfNull(report);

        var dialog = new WpfWindow
        {
            Title = Loc.T("EndpointInspection.DialogTitle", report.Title),
            Width = 790,
            Height = 620,
            MinWidth = 640,
            MinHeight = 430,
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

        var body = new StackPanel { Margin = new Thickness(0, 8, 0, 8) };
        body.Children.Add(ConnectionCard(report));
        body.Children.Add(ModelsCard(report));
        if (report.Defaults is not null)
            body.Children.Add(DefaultsCard(report.Defaults));
        if (report.Kind == EndpointInspectionKind.DirectModel)
            body.Children.Add(SlotsCard(report.Slots));
        else
        {
            body.Children.Add(GatewayCard(report));
            body.Children.Add(RunningModelsCard(report.RunningModels));
        }
        if (report.UnavailableSources.Count > 0)
            body.Children.Add(UnavailableCard(report.UnavailableSources));

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
            Padding = new Thickness(14),
            Child = layout
        };
        return dialog;
    }

    private static Grid Header(WpfWindow dialog, EndpointInspectionReport report)
    {
        var header = new Grid { Margin = new Thickness(1, 0, 0, 2) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var copy = new StackPanel();
        copy.Children.Add(new TextBlock
        {
            Text = report.Kind == EndpointInspectionKind.Gateway
                ? Loc.T("EndpointInspection.GatewayReport")
                : Loc.T("EndpointInspection.DirectReport"),
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain")
        });
        copy.Children.Add(new TextBlock
        {
            Text = Loc.T("EndpointInspection.Description"),
            FontSize = 12.5,
            Foreground = ResourceBrush("TextSoft"),
            Margin = new Thickness(0, 3, 0, 0)
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

    private static WpfBorder ConnectionCard(EndpointInspectionReport report)
    {
        var fields = FieldsGrid(
            (Loc.T("EndpointInspection.Endpoint"), report.Endpoint),
            (Loc.T("EndpointInspection.Health"), report.Health),
            (Loc.T("EndpointInspection.Inspected"), report.InspectedAt.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture)),
            (Loc.T("EndpointInspection.Sources"), report.Kind == EndpointInspectionKind.DirectModel
                ? "/health · /v1/models · /props · /slots"
                : "/health · /v1/models · /running"));
        return Card(Loc.T("EndpointInspection.Connection"), fields);
    }

    private static WpfBorder ModelsCard(EndpointInspectionReport report)
    {
        if (report.Models.Count == 0)
            return Card(
                report.Kind == EndpointInspectionKind.Gateway
                    ? Loc.T("EndpointInspection.AdvertisedModels")
                    : Loc.T("EndpointInspection.EndpointModel"),
                Muted(Loc.T("EndpointInspection.NoModels")));

        var rows = report.Models.Select(model => new DisplayRow(
            model.NameOrId(),
            Empty(model.Profile),
            Empty(model.Owner),
            model.TrainingContext.HasValue ? Tokens(model.TrainingContext.Value) : "—",
            model.ParameterCount.HasValue ? CompactCount(model.ParameterCount.Value) : "—",
            model.SizeBytes.HasValue ? DisplayFormatService.Bytes(model.SizeBytes.Value) : "—"));
        var grid = Table(
            rows,
            (Loc.T("EndpointInspection.ModelIdName"), nameof(DisplayRow.C1), 1.85),
            (Loc.T("EndpointInspection.Profile"), nameof(DisplayRow.C2), .9),
            (Loc.T("EndpointInspection.Owner"), nameof(DisplayRow.C3), .8),
            (Loc.T("EndpointInspection.TrainingContext"), nameof(DisplayRow.C4), .65),
            (Loc.T("EndpointInspection.Parameters"), nameof(DisplayRow.C5), .65),
            (Loc.T("Models.Col.Size"), nameof(DisplayRow.C6), .6));
        return Card(
            report.Kind == EndpointInspectionKind.Gateway
                ? Loc.T("EndpointInspection.AdvertisedModelsCount", report.Models.Count)
                : Loc.T("EndpointInspection.EndpointModel"),
            grid);
    }

    private static WpfBorder DefaultsCard(EndpointInspectionDefaults defaults)
    {
        var capabilities = defaults.ChatCapabilities.Count == 0
            ? Loc.T("EndpointInspection.NotReported")
            : string.Join(", ", defaults.ChatCapabilities
                .Where(pair => pair.Value)
                .Select(pair => FriendlyCapability(pair.Key))
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(capabilities)) capabilities = Loc.T("EndpointInspection.NoCapabilitiesEnabled");
        var fields = FieldsGrid(
            (Loc.T("EndpointInspection.ModelFile"), Empty(defaults.ModelFile)),
            (Loc.T("EndpointInspection.ContextSize"), defaults.ContextSize.HasValue ? Tokens(defaults.ContextSize.Value) : Loc.T("EndpointInspection.NotReported")),
            (Loc.T("EndpointInspection.ParallelSlots"), Number(defaults.ParallelSlots)),
            (Loc.T("EndpointInspection.DefaultMaxOutput"), OutputLimit(defaults.MaximumOutputTokens)),
            (Loc.T("EndpointInspection.Reasoning"), Empty(defaults.Reasoning, Loc.T("EndpointInspection.NotReportedRequestControlled"))),
            (Loc.T("EndpointInspection.ReasoningFormat"), Empty(defaults.ReasoningFormat, Loc.T("EndpointInspection.NotReportedRequestControlled"))),
            (Loc.T("EndpointInspection.Vision"), defaults.Vision),
            (Loc.T("EndpointInspection.Speculative"), Boolean(defaults.Speculative)),
            (Loc.T("EndpointInspection.Temperature"), Number(defaults.Temperature)),
            (Loc.T("EndpointInspection.TopK"), Number(defaults.TopK)),
            (Loc.T("EndpointInspection.TopP"), Number(defaults.TopP)),
            (Loc.T("EndpointInspection.MinP"), Number(defaults.MinP)),
            (Loc.T("EndpointInspection.Sleeping"), Boolean(defaults.Sleeping)),
            (Loc.T("EndpointInspection.Build"), Empty(defaults.Build)),
            (Loc.T("EndpointInspection.ChatCapabilities"), capabilities));
        return Card(Loc.T("EndpointInspection.ServerDefaults"), fields);
    }

    private static WpfBorder SlotsCard(IReadOnlyList<EndpointInspectionSlot> slots)
    {
        if (slots.Count == 0)
            return Card(Loc.T("EndpointInspection.CurrentSlots"), Muted(Loc.T("EndpointInspection.NoSlotState")));
        var rows = slots.Select(slot => new DisplayRow(
            slot.Id?.ToString(CultureInfo.InvariantCulture) ?? "—",
            SlotState(slot),
            slot.ContextSize.HasValue ? Tokens(slot.ContextSize.Value) : "—",
            SlotOutput(slot),
            Boolean(slot.Speculative),
            Sampling(slot)));
        return Card(Loc.T("EndpointInspection.CurrentSlotsCount", slots.Count(slot => slot.IsProcessing), slots.Count), Table(
            rows,
            (Loc.T("EndpointInspection.Slot"), nameof(DisplayRow.C1), .42),
            (Loc.T("Overview.SessionsCol.State"), nameof(DisplayRow.C2), .75),
            (Loc.T("EndpointInspection.Context"), nameof(DisplayRow.C3), .8),
            (Loc.T("EndpointInspection.MaxOutput"), nameof(DisplayRow.C4), .9),
            (Loc.T("EndpointInspection.Speculative"), nameof(DisplayRow.C5), .7),
            (Loc.T("EndpointInspection.Sampling"), nameof(DisplayRow.C6), 1.7)));
    }

    private static WpfBorder GatewayCard(EndpointInspectionReport report)
        => Card(Loc.T("EndpointInspection.ManagerRouting"), FieldsGrid(
            (Loc.T("EndpointInspection.Policy"), Empty(report.GatewayPolicy)),
            (Loc.T("EndpointInspection.Exposure"), Empty(report.GatewayExposure)),
            (Loc.T("EndpointInspection.ModelSelection"), Loc.T("EndpointInspection.ModelSelectionValue")),
            (Loc.T("EndpointInspection.ModelDefaults"), Loc.T("EndpointInspection.ModelDefaultsValue"))));

    private static WpfBorder RunningModelsCard(IReadOnlyList<EndpointInspectionRunningModel> models)
    {
        if (models.Count == 0)
            return Card(Loc.T("EndpointInspection.LoadedThroughManager"), Muted(Loc.T("EndpointInspection.NoLoadedRuntime")));
        var rows = models.Select(model => new DisplayRow(
            Empty(model.Name, model.Id),
            Empty(model.Status),
            Empty(model.Runtime),
            Empty(model.Endpoint),
            model.StartedAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "—"));
        return Card(Loc.T("EndpointInspection.LoadedThroughManagerCount", models.Count), Table(
            rows,
            (Loc.T("Overview.SessionsCol.Model"), nameof(DisplayRow.C1), 1.15),
            (Loc.T("Overview.SessionsCol.State"), nameof(DisplayRow.C2), .65),
            (Loc.T("Overview.SessionsCol.Runtime"), nameof(DisplayRow.C3), 1.1),
            (Loc.T("EndpointInspection.DirectEndpoint"), nameof(DisplayRow.C4), 1.45),
            (Loc.T("EndpointInspection.Started"), nameof(DisplayRow.C5), .9)));
    }

    private static WpfBorder UnavailableCard(IReadOnlyList<string> unavailable)
        => Card(Loc.T("EndpointInspection.UnavailableDetails"), Muted(string.Join(Environment.NewLine, unavailable)));

    private static Grid FieldsGrid(params (string Label, string Value)[] fields)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        for (var index = 0; index < fields.Length; index++)
        {
            var row = index / 2;
            if (grid.RowDefinitions.Count <= row)
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var column = index % 2 == 0 ? 0 : 3;
            var label = new TextBlock
            {
                Text = fields[index].Label,
                Foreground = ResourceBrush("TextMuted"),
                Margin = new Thickness(0, 3, 10, 4),
                VerticalAlignment = VerticalAlignment.Top
            };
            Grid.SetRow(label, row);
            Grid.SetColumn(label, column);
            grid.Children.Add(label);
            var value = new TextBlock
            {
                Text = fields[index].Value,
                Foreground = ResourceBrush("TextMain"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 4)
            };
            Grid.SetRow(value, row);
            Grid.SetColumn(value, column + 1);
            grid.Children.Add(value);
        }
        return grid;
    }

    private static DataGrid Table(
        IEnumerable<DisplayRow> rows,
        params (string Header, string Binding, double Width)[] columns)
    {
        var grid = PageSectionFactory.GridFor(columns);
        grid.ItemsSource = new ObservableCollection<DisplayRow>(rows);
        grid.IsReadOnly = true;
        grid.CanUserAddRows = false;
        grid.CanUserDeleteRows = false;
        grid.SelectionMode = DataGridSelectionMode.Single;
        grid.MaxHeight = 215;
        grid.MinHeight = 42;
        grid.HeadersVisibility = DataGridHeadersVisibility.Column;
        return grid;
    }

    private static WpfBorder Card(string title, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock
        {
            Text = title,
            FontSize = 13.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = ResourceBrush("TextMain"),
            Margin = new Thickness(0, 0, 0, 7)
        });
        panel.Children.Add(content);
        return new WpfBorder
        {
            Background = ResourceBrush("SurfaceRaised"),
            BorderBrush = ResourceBrush("PanelBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(11, 9, 11, 9),
            Margin = new Thickness(0, 0, 0, 8),
            Child = panel
        };
    }

    private static TextBlock Muted(string text) => new()
    {
        Text = text,
        Foreground = ResourceBrush("TextSoft"),
        TextWrapping = TextWrapping.Wrap
    };

    private static WpfButton Button(string text) => new()
    {
        Content = text,
        MinWidth = 74,
        Height = 29,
        MinHeight = 29,
        Padding = new Thickness(10, 2, 10, 2),
        Margin = new Thickness(6, 0, 0, 0)
    };

    private static WpfBrush ResourceBrush(string key)
        => (WpfBrush)WpfApplication.Current.Resources[key];

    private static string Empty(string value, string? fallback = null)
        => string.IsNullOrWhiteSpace(value) ? fallback ?? "—" : value;

    private static string Number(int? value)
        => value?.ToString("N0", CultureInfo.InvariantCulture) ?? Loc.T("EndpointInspection.NotReported");

    private static string Number(double? value)
        => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? Loc.T("EndpointInspection.NotReported");

    private static string Boolean(bool? value)
        => value.HasValue
            ? value.Value ? Loc.T("Pref.Yes") : Loc.T("Pref.No")
            : Loc.T("EndpointInspection.NotReported");

    private static string OutputLimit(int? value)
        => value switch
        {
            null => Loc.T("EndpointInspection.NotReportedRequestControlled"),
            < 0 => Loc.T("EndpointInspection.UnlimitedRequestControlled"),
            _ => Tokens(value.Value)
        };

    private static string Tokens(long value)
        => Loc.T("EndpointInspection.Tokens", value.ToString("N0", CultureInfo.InvariantCulture));

    private static string CompactCount(long value)
        => value >= 1_000_000_000
            ? $"{value / 1_000_000_000d:0.##}B"
            : value >= 1_000_000
                ? $"{value / 1_000_000d:0.##}M"
                : value.ToString("N0", CultureInfo.InvariantCulture);

    private static string Sampling(EndpointInspectionSlot slot)
        => $"T {Number(slot.Temperature)} · K {Number(slot.TopK)} · P {Number(slot.TopP)} · Min P {Number(slot.MinP)}";

    private static string SlotState(EndpointInspectionSlot slot)
    {
        var state = slot.IsProcessing ? Loc.T("EndpointInspection.Processing") : Loc.T("EndpointInspection.Idle");
        return string.IsNullOrWhiteSpace(slot.ReasoningFormat)
            ? state
            : Loc.T("EndpointInspection.StateReasoning", state, slot.ReasoningFormat);
    }

    private static string SlotOutput(EndpointInspectionSlot slot)
    {
        var parts = new List<string> { OutputLimit(slot.MaximumOutputTokens) };
        if (slot.DecodedTokens is { } decoded)
            parts.Add(Loc.T("EndpointInspection.Decoded", decoded.ToString("N0", CultureInfo.InvariantCulture)));
        if (slot.RemainingTokens is >= 0 and { } remaining)
            parts.Add(Loc.T("EndpointInspection.Remaining", remaining.ToString("N0", CultureInfo.InvariantCulture)));
        return string.Join(" · ", parts);
    }

    private static string FriendlyCapability(string key)
        => key.Replace("supports_", "", StringComparison.OrdinalIgnoreCase)
            .Replace('_', ' ');

    private static string NameOrId(this EndpointInspectionModel model)
        => !string.IsNullOrWhiteSpace(model.Name)
            ? $"{model.Name} ({model.Id})"
            : Empty(model.Id);
}
