using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfButton = System.Windows.Controls.Button;
using WpfFontFamily = System.Windows.Media.FontFamily;

namespace LocalLlmConsole;

/// <summary>
/// The loaded-models card body: one keyboard-focusable row per running session.
/// Rows are updated in place while the session set is unchanged so polling never
/// steals keyboard focus.
/// </summary>
public sealed class OverviewDashboardLoadedModelsView
{
    private static readonly WpfFontFamily MeasurementFont = new("Cascadia Mono, Consolas, Segoe UI");
    private readonly Action<string>? _selectSession;
    private readonly StackPanel _rows = new();
    private readonly TextBlock _count;
    private readonly TextBlock _empty;
    private readonly List<SessionRow> _sessionRows = [];
    private IReadOnlyList<LoadedSessionDashboardRow> _current = [];

    public OverviewDashboardLoadedModelsView(OverviewDashboardMetricDefinition definition, Action<string>? selectSession)
    {
        _selectSession = selectSession;
        var content = new StackPanel();
        var header = new DockPanel { LastChildFill = true, Margin = new Thickness(0, 1, 0, 3) };
        _count = new TextBlock
        {
            FontSize = 14.5,
            FontWeight = FontWeights.SemiBold,
            FontFamily = MeasurementFont,
            Margin = new Thickness(8, 0, 0, 0)
        };
        _count.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
        DockPanel.SetDock(_count, Dock.Right);
        header.Children.Add(_count);
        var label = new TextBlock
        {
            Text = definition.DisplayName,
            FontSize = 10.25,
            FontWeight = FontWeights.Medium,
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "TextSoft");
        header.Children.Add(label);
        content.Children.Add(header);
        _empty = new TextBlock
        {
            Text = Loc.T("Dashboard.LoadedModels.Empty"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 2)
        };
        _empty.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
        content.Children.Add(_empty);
        content.Children.Add(_rows);
        Root = new Border { Child = content, Tag = this, Background = WpfBrushes.Transparent };
        AutomationProperties.SetName(Root, definition.DisplayName);
        Apply([]);
    }

    public Border Root { get; }
    public IReadOnlyList<WpfButton> SessionButtons => _sessionRows.Select(row => row.Button).ToArray();
    public IReadOnlyList<LoadedSessionDashboardRow> Sessions => _current;

    public bool Apply(IReadOnlyList<LoadedSessionDashboardRow> sessions)
    {
        sessions ??= [];
        if (sessions.SequenceEqual(_current) && _sessionRows.Count == sessions.Count && _count.Text.Length > 0)
            return false;
        var sameSet = sessions.Select(row => row.SessionId)
            .SequenceEqual(_sessionRows.Select(row => row.SessionId), StringComparer.Ordinal);
        if (!sameSet)
        {
            _rows.Children.Clear();
            _sessionRows.Clear();
            foreach (var session in sessions)
            {
                var row = new SessionRow(session.SessionId, _selectSession);
                _sessionRows.Add(row);
                _rows.Children.Add(row.Button);
            }
        }

        for (var index = 0; index < sessions.Count; index++)
            _sessionRows[index].Apply(sessions[index], index < sessions.Count - 1);
        _current = sessions.ToArray();
        _count.Text = sessions.Count.ToString(CultureInfo.CurrentCulture);
        _empty.Visibility = sessions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        return true;
    }

    public static string StatusLabel(LoadedModelSessionStatus status)
        => Loc.T(status switch
        {
            LoadedModelSessionStatus.Running or LoadedModelSessionStatus.Warm => "Dashboard.LoadedModels.Status.Loaded",
            LoadedModelSessionStatus.Loading => "Dashboard.LoadedModels.Status.Loading",
            LoadedModelSessionStatus.Unreachable => "Dashboard.LoadedModels.Status.Unreachable",
            LoadedModelSessionStatus.Stopping => "Dashboard.LoadedModels.Status.Stopping",
            LoadedModelSessionStatus.Failed => "Dashboard.LoadedModels.Status.Failed",
            _ => "Dashboard.LoadedModels.Status.Stopped"
        });

    public static string MetricsLine(LoadedSessionDashboardRow session)
    {
        var unavailable = Loc.T("Dashboard.ValueUnavailable");
        var rate = session.AverageGenerationRate is { } generation && double.IsFinite(generation)
            ? $"{generation.ToString("0.0", CultureInfo.CurrentCulture)} t/s"
            : unavailable;
        var slots = session.ActiveSlots is { } active && session.SlotCapacity is { } capacity
            ? Loc.T("Dashboard.LoadedModels.Slots",
                active.ToString("0", CultureInfo.CurrentCulture),
                capacity.ToString(CultureInfo.CurrentCulture))
            : Loc.T("Dashboard.LoadedModels.Slots", unavailable, unavailable);
        var kv = Loc.T("Dashboard.LoadedModels.KvCache", session.KvCacheUsagePercent is { } percent && double.IsFinite(percent)
            ? $"{percent.ToString("0", CultureInfo.CurrentCulture)}%"
            : unavailable);
        var port = Loc.T("Dashboard.LoadedModels.Port", session.Port.ToString(CultureInfo.InvariantCulture));
        return string.Join(" · ", rate, slots, kv, port);
    }

    public static string DisplayName(LoadedSessionDashboardRow session)
        => string.IsNullOrWhiteSpace(session.ProfileName)
            ? session.ModelName
            : $"{session.ModelName} · {session.ProfileName}";

    private sealed class SessionRow
    {
        private readonly Border _surface;
        private readonly TextBlock _name;
        private readonly TextBlock _status;
        private readonly TextBlock _metrics;

        public SessionRow(string sessionId, Action<string>? select)
        {
            SessionId = sessionId;
            _name = new TextBlock
            {
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            _name.SetResourceReference(TextBlock.ForegroundProperty, "TextMain");
            _status = new TextBlock { FontSize = 10, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _metrics = new TextBlock
            {
                FontSize = 10,
                FontFamily = MeasurementFont,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 1, 0, 0)
            };
            _metrics.SetResourceReference(TextBlock.ForegroundProperty, "TextMuted");
            Typography.SetNumeralAlignment(_metrics, FontNumeralAlignment.Tabular);
            var firstLine = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(_status, Dock.Right);
            firstLine.Children.Add(_status);
            firstLine.Children.Add(_name);
            var lines = new StackPanel();
            lines.Children.Add(firstLine);
            lines.Children.Add(_metrics);
            _surface = new Border
            {
                Child = lines,
                Padding = new Thickness(6, 4, 6, 4),
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(3),
                BorderBrush = WpfBrushes.Transparent,
                Background = WpfBrushes.Transparent
            };
            Button = new WpfButton
            {
                Content = _surface,
                // The app button style renders string content; rows host rich content.
                ContentTemplate = null,
                Tag = sessionId,
                HorizontalContentAlignment = System.Windows.HorizontalAlignment.Stretch,
                Padding = new Thickness(0),
                Margin = new Thickness(-4, 1, -4, 1),
                MinHeight = 0,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand
            };
            VisualRole.SetButtonRole(Button, VisualRole.Quiet);
            AutomationProperties.SetHelpText(Button, Loc.T("Dashboard.LoadedModels.SelectHelp"));
            ToolTipService.SetShowDuration(Button, 20000);
            Button.Click += (_, args) =>
            {
                args.Handled = true;
                select?.Invoke(SessionId);
            };
        }

        public string SessionId { get; }
        public WpfButton Button { get; }

        public void Apply(LoadedSessionDashboardRow session, bool hasNext)
        {
            var name = DisplayName(session);
            var status = StatusLabel(session.Status);
            var metrics = MetricsLine(session);
            _name.Text = name;
            _status.Text = status;
            _status.SetResourceReference(TextBlock.ForegroundProperty, session.Status switch
            {
                LoadedModelSessionStatus.Running or LoadedModelSessionStatus.Warm => "Success",
                LoadedModelSessionStatus.Unreachable or LoadedModelSessionStatus.Failed => "Danger",
                _ => "Warning"
            });
            _metrics.Text = metrics;
            if (session.IsSelected)
            {
                _surface.SetResourceReference(Border.BorderBrushProperty, "AccentBlue");
                _surface.SetResourceReference(Border.BackgroundProperty, "InfoSoft");
            }
            else
            {
                _surface.BorderBrush = WpfBrushes.Transparent;
                _surface.Background = WpfBrushes.Transparent;
            }
            Button.Margin = new Thickness(-4, 1, -4, hasNext ? 2 : 0);
            var accessibleName = session.IsSelected
                ? $"{name}, {status}, {Loc.T("Dashboard.LoadedModels.Selected")}"
                : $"{name}, {status}";
            AutomationProperties.SetName(Button, accessibleName);
            AutomationProperties.SetItemStatus(Button, metrics);
            Button.ToolTip = $"{name}\n{status}\n{metrics}\n{Loc.T("Dashboard.LoadedModels.SelectHelp")}";
        }
    }
}
