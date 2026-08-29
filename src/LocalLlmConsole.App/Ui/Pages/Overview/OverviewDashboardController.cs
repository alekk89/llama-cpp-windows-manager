using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WpfApplication = System.Windows.Application;
using WpfBrush = System.Windows.Media.Brush;
using WpfButton = System.Windows.Controls.Button;

namespace LocalLlmConsole;

public sealed record OverviewDashboardControllerActions(
    Func<OverviewDashboardLayout, Task> PersistLayoutAsync,
    Func<Func<Task>, Task> RunEventAsync,
    Func<Func<Task>, Task>? DispatchMenuActionAsync = null);

public sealed partial class OverviewDashboardController
{
    private const string HardwareRuntimeKey = "host-hardware";
    private readonly OverviewDashboardControllerActions _actions;
    private readonly OverviewDashboardMetricRegistry _registry = new();
    private readonly Dictionary<string, OverviewDashboardMetricReading> _readings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DashboardMetricHistory> _histories = new(StringComparer.Ordinal);
    private readonly Dictionary<string, OverviewDashboardCardView> _cardViews = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _mutationGate = new(1, 1);
    private readonly Grid _dashboard = new();
    private readonly Canvas _surface = new();
    private readonly StackPanel _root = new();
    private readonly StackPanel _dashboardActions;
    private readonly TextBlock _emptyState;
    private WpfButton _sizeLockButton = null!;
    private OverviewDashboardLayout _layout;
    private OverviewDashboardCardView? _dragView;
    private System.Windows.Point _dragOrigin;
    private OverviewDashboardCardBounds? _dragStartBounds;
    private OverviewDashboardCardBounds? _interactionBounds;
    private OverviewDashboardCardView? _resizeView;
    private OverviewDashboardResizeEdge _resizeEdge;
    private System.Windows.Point _resizeOrigin;
    private OverviewDashboardCardBounds? _resizeStartBounds;
    private int _deferredUpdateDepth;
    private bool _readingsApplyPending;
    private bool _pendingGraphPush;
    private bool _placementDirty = true;
    private double _lastPlacementWidth = double.NaN;

    public OverviewDashboardController(
        OverviewDashboardLayout? layout,
        OverviewDashboardControllerActions actions)
    {
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _layout = OverviewDashboardLayoutPolicy.Normalize(layout);
        var header = DashboardHeader(out _dashboardActions);
        _root.Children.Add(header);
        _dashboard.Margin = new Thickness(0, 2, 0, 8);
        _surface.Background = System.Windows.Media.Brushes.Transparent;
        _dashboard.Children.Add(_surface);
        _dashboard.SizeChanged += (_, args) => ApplyPlacement(args.NewSize.Width);
        _dashboard.ContextMenu = DashboardContextMenu();
        _root.Children.Add(_dashboard);
        _emptyState = new TextBlock
        {
            Text = Loc.T("Dashboard.EmptyState"),
            Foreground = (WpfBrush)WpfApplication.Current.Resources["TextMuted"],
            Margin = new Thickness(0, 4, 0, 12),
            Visibility = Visibility.Collapsed
        };
        _root.Children.Add(_emptyState);
        ConfigureCommands();
        ConfigureReorderPersistence();
        RebuildDashboard();
    }

    public FrameworkElement Root => _root;
    public OverviewDashboardLayout Layout => _layout;
    public Grid DashboardGrid => _dashboard;
    public Canvas DashboardCanvas => _surface;
    public bool IsEditing => true;
    public IReadOnlyList<OverviewDashboardCardView> Cards
        => _layout.Cards.Select(card => _cardViews[card.Id]).ToArray();

    public IDisposable DeferUpdates()
    {
        _deferredUpdateDepth++;
        return new DeferredUpdateScope(this);
    }

    public void ApplyLayout(OverviewDashboardLayout? layout)
    {
        _layout = OverviewDashboardLayoutPolicy.Normalize(layout);
        RebuildDashboard();
    }

    public void SetMetricValue(string metricId, string value)
    {
        _readings[metricId] = new OverviewDashboardMetricReading(metricId, value);
        RequestApplyReadings(pushGraphs: false);
    }

    public void ApplyHardwareSummary(string summary)
        => ApplyHardwareSummaryCore(HostHardwareSnapshotParser.Parse(summary));

    public async Task ApplyHardwareSummaryAsync(HostHardwareSnapshot snapshot)
    {
        if (ApplyHardwareSummaryCore(snapshot))
            await _actions.PersistLayoutAsync(_layout);
    }

    private bool ApplyHardwareSummaryCore(HostHardwareSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        foreach (var metricId in _readings.Keys.Where(IsHardwareMetric).ToArray())
            _readings.Remove(metricId);
        var observed = _registry.ObserveHardware(snapshot);
        foreach (var reading in observed)
            SetReading(reading with { RuntimeKey = HardwareRuntimeKey });
        if (observed.All(reading => !string.Equals(reading.MetricId, OverviewDashboardMetricIds.Cpu, StringComparison.Ordinal)))
            SetReading(new(OverviewDashboardMetricIds.Cpu, Loc.T("Dashboard.ValueUnavailable"), HardwareRuntimeKey));
        if (observed.All(reading => !string.Equals(reading.MetricId, OverviewDashboardMetricIds.Ram, StringComparison.Ordinal)))
            SetReading(new(OverviewDashboardMetricIds.Ram, Loc.T("Dashboard.ValueUnavailable"), HardwareRuntimeKey));
        var detectedLayout = OverviewDashboardLayoutPolicy.WithDetectedGpuCards(
            _layout,
            OverviewDashboardLayoutPolicy.DefaultGpuCardIndices(snapshot.Gpus));
        var layoutChanged = !LayoutsMatch(_layout, detectedLayout);
        if (layoutChanged)
        {
            _layout = detectedLayout;
            RebuildDashboard();
        }
        // Rebuilding replays the just-recorded hardware history into new card
        // graphs, so pushing again would duplicate the latest sample.
        RequestApplyReadings(pushGraphs: !layoutChanged);
        return layoutChanged;
    }

    public void ApplyObservedGpuEnergy(ObservedGpuEnergySnapshot? snapshot)
    {
        foreach (var metricId in _readings.Keys
                     .Where(OverviewDashboardMetricIds.IsObservedGpuMetric)
                     .ToArray())
            _readings.Remove(metricId);

        if (snapshot is not null)
        {
            foreach (var reading in _registry.ObserveGpuEnergy(snapshot))
                SetReading(reading);
        }
        RequestApplyReadings(pushGraphs: false);
    }

    public void ApplyGatewayPerformance(GatewayPerformanceSnapshot snapshot)
    {
        const string runtimeKey = "gateway";
        SetGateway(OverviewDashboardMetricIds.GatewayTimeToFirstData,
            snapshot.LastTimeToFirstDataMilliseconds, "0", "ms");
        SetGateway(OverviewDashboardMetricIds.GatewayRequestDuration,
            snapshot.LastRequestDurationMilliseconds, "0", "ms");
        SetGateway(OverviewDashboardMetricIds.GatewayResponseThroughput,
            snapshot.LastResponseTokensPerSecond, "0.0", "t/s");
        SetGateway(OverviewDashboardMetricIds.GatewayRequests, snapshot.RequestCount, "N0", "requests");
        SetGateway(OverviewDashboardMetricIds.GatewayFailures, snapshot.FailureCount, "N0", "requests");
        SetGateway(OverviewDashboardMetricIds.GatewayFailureRate,
            snapshot.FailureRatePercent, "0.#", "%");
        RequestApplyReadings(pushGraphs: true);

        void SetGateway(string id, double? value, string format, string unit)
        {
            if (snapshot.CapturedAt is null || value is not { } finite || !double.IsFinite(finite)) return;
            SetReading(new(id, finite.ToString(format, CultureInfo.CurrentCulture), runtimeKey,
                finite, Unit: unit));
        }
    }

    public void ApplyMetricSummary(RuntimeMetricSummaryPresentation summary)
    {
        ArgumentNullException.ThrowIfNull(summary);
        var graph = summary.GraphSample;
        if (string.IsNullOrWhiteSpace(graph.RuntimeKey))
        {
            foreach (var metricId in _histories.Keys.Where(id => !IsHardwareMetric(id)).ToArray())
                _histories.Remove(metricId);
        }
        var staleAt = summary.LastKnownCapturedAt;
        var atomic = summary.Atomic ?? RuntimeMetricAtomicSnapshot.Empty with
        {
            AverageGenerationRate = graph.GenerationRate,
            AveragePromptRate = graph.PromptRate,
            MtpGeneratedRate = graph.SpeculativeGeneratedRate,
            MtpAcceptedRate = graph.SpeculativeAcceptedRate,
            KvCacheUsagePercent = graph.KvCacheUsagePercent
        };
        var draftAcceptancePercent = atomic.DraftAcceptancePercent
            ?? (atomic.MtpAcceptedTokens is { } accepted && atomic.MtpGeneratedTokens is > 0
                ? Math.Clamp(100 * accepted / atomic.MtpGeneratedTokens.Value, 0, 100)
                : (double?)null);
        SetAtomicReading(OverviewDashboardMetricIds.AverageGenerationRate, Number(atomic.AverageGenerationRate, "0.0"), atomic.AverageGenerationRate, "t/s");
        SetAtomicReading(OverviewDashboardMetricIds.AveragePromptRate, Number(atomic.AveragePromptRate, "0.0"), atomic.AveragePromptRate, "t/s");
        SetAtomicReading(OverviewDashboardMetricIds.RecentGenerationRate, Number(atomic.RecentGenerationRate, "0.0"), atomic.RecentGenerationRate, "t/s");
        SetAtomicReading(OverviewDashboardMetricIds.RecentPromptRate, Number(atomic.RecentPromptRate, "0.0"), atomic.RecentPromptRate, "t/s");
        SetAtomicReading(OverviewDashboardMetricIds.PromptCacheReuse, Number(atomic.PromptCacheReusePercent, "0.#"),
            atomic.PromptCacheReusePercent, "%", atomic.PromptCachedTokens is { } cached ? $"{cached:N0} cached tokens" : "");
        SetAtomicReading(OverviewDashboardMetricIds.DraftAcceptance, Number(draftAcceptancePercent, "0.#"),
            draftAcceptancePercent, "%");
        SetAtomicReading(OverviewDashboardMetricIds.PeakContextUsed, Number(atomic.PeakContextTokens, "N0"),
            atomic.PeakContextTokens, "tokens");
        SetAtomicReading(OverviewDashboardMetricIds.ContextShifts, Number(atomic.ContextShiftCount, "N0"),
            atomic.ContextShiftCount);
        SetAtomicReading(OverviewDashboardMetricIds.GeneratedTokens, Number(atomic.GeneratedTokens, "N0"), atomic.GeneratedTokens, "tokens");
        SetAtomicReading(OverviewDashboardMetricIds.PromptTokens, Number(atomic.PromptTokens, "N0"), atomic.PromptTokens, "tokens");
        SetAtomicReading(OverviewDashboardMetricIds.AverageMtpGeneratedRate, Number(atomic.AverageMtpGeneratedRate, "0.0"), atomic.AverageMtpGeneratedRate, "t/s");
        SetAtomicReading(OverviewDashboardMetricIds.AverageMtpAcceptedRate, Number(atomic.AverageMtpAcceptedRate, "0.0"), atomic.AverageMtpAcceptedRate, "t/s");
        SetAtomicReading(OverviewDashboardMetricIds.MtpGeneratedTokens, Number(atomic.MtpGeneratedTokens, "N0"), atomic.MtpGeneratedTokens, "tokens");
        SetAtomicReading(OverviewDashboardMetricIds.MtpAcceptedTokens, Number(atomic.MtpAcceptedTokens, "N0"), atomic.MtpAcceptedTokens, "tokens");
        SetAtomicReading(OverviewDashboardMetricIds.ActiveSlots,
            $"{atomic.ActiveSlots:N0}", atomic.ActiveSlots, $"of {atomic.SlotCapacity:N0}");
        SetAtomicReading(OverviewDashboardMetricIds.QueuedRequests, $"{atomic.QueuedRequests:N0}", atomic.QueuedRequests);
        SetAtomicReading(OverviewDashboardMetricIds.BusyDecodeSlots, $"{atomic.BusyDecodeSlots:0.0}", atomic.BusyDecodeSlots);
        SetAtomicReading(OverviewDashboardMetricIds.KvCacheUsed, Number(atomic.KvCacheUsedTokens, "N0"), atomic.KvCacheUsedTokens, "tokens");
        SetAtomicReading(OverviewDashboardMetricIds.KvCacheCapacity, Number(atomic.KvCacheCapacityTokens, "N0"), atomic.KvCacheCapacityTokens, "tokens");
        var kvDetail = atomic.KvCacheUsedTokens is { } used && atomic.KvCacheCapacityTokens is { } capacity
            ? $"{used:N0} of {capacity:N0} tokens"
            : "";
        SetAtomicReading(OverviewDashboardMetricIds.KvCacheUsage, Number(atomic.KvCacheUsagePercent, "0.#"),
            atomic.KvCacheUsagePercent, "%", kvDetail);
        SetAtomicReading(OverviewDashboardMetricIds.KvCacheAllocation, atomic.KvCacheAllocation, null);

        foreach (var rawId in _readings.Keys.Where(id => id.StartsWith(OverviewDashboardMetricIds.PrometheusPrefix, StringComparison.Ordinal)).ToArray())
            _readings.Remove(rawId);
        foreach (var reading in _registry.Observe(summary.Samples, graph.RuntimeKey))
            SetReading(staleAt is null ? reading : reading with { LastKnownCapturedAt = staleAt });
        RequestApplyReadings(pushGraphs: true);

        void SetAtomicReading(string metricId, string value, double? primary, string unit = "", string detail = "")
            => SetReading(new(metricId, value, graph.RuntimeKey, primary, LastKnownCapturedAt: staleAt,
                Unit: string.Equals(value, Loc.T("Dashboard.ValueUnavailable"), StringComparison.Ordinal) ? "" : unit,
                Detail: detail));
    }

    private static string Number(double? value, string format)
        => value is { } number && double.IsFinite(number)
            ? number.ToString(format, CultureInfo.CurrentCulture)
            : Loc.T("Dashboard.ValueUnavailable");

    private static bool IsHardwareMetric(string metricId)
        => string.Equals(metricId, OverviewDashboardMetricIds.Cpu, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.CpuTemperature, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.CpuCoreClock, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.Ram, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.RamUsed, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.RamClock, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.ServerProcessCpu, StringComparison.Ordinal)
           || string.Equals(metricId, OverviewDashboardMetricIds.ServerProcessMemory, StringComparison.Ordinal)
           || OverviewDashboardMetricIds.IsObservedGpuMetric(metricId)
           || OverviewDashboardMetricIds.IsGpuMetric(metricId);

    private void SetReading(OverviewDashboardMetricReading reading)
    {
        _readings[reading.MetricId] = reading;
        if (string.IsNullOrWhiteSpace(reading.RuntimeKey)) return;
        if (!_histories.TryGetValue(reading.MetricId, out var history))
        {
            history = new DashboardMetricHistory();
            _histories[reading.MetricId] = history;
        }
        if (!string.Equals(history.RuntimeKey, reading.RuntimeKey, StringComparison.Ordinal))
        {
            history.RuntimeKey = reading.RuntimeKey;
            history.Readings.Clear();
        }
        if (reading.Primary is null && reading.Secondary is null) return;
        history.Readings.Add(reading);
        if (history.Readings.Count > 60)
            history.Readings.RemoveAt(0);
    }

    private void RebuildDashboard()
    {
        _surface.Children.Clear();
        _cardViews.Clear();
        var configuredIds = _layout.Cards.SelectMany(card => card.MetricIds).ToArray();
        var definitions = _registry.Definitions(configuredIds);
        foreach (var card in _layout.Cards)
        {
            var view = new OverviewDashboardCardView(card, definitions);
            view.SetResizeEnabled(!_layout.CardSizesLocked);
            view.Root.ContextMenu = CardContextMenu(card);
            view.DragSurface.PreviewMouseLeftButtonDown += (_, args) => BeginPointerInteraction(view, args);
            view.DragSurface.PreviewMouseMove += (_, args) => TrackPointerInteraction(view, args);
            view.DragSurface.PreviewMouseLeftButtonUp += async (_, args) => await EndPointerInteractionAsync(view, args);
            view.DragSurface.MouseLeave += (_, _) => ResetPointerWhenIdle(view);
            ConfigureKeyboardInteraction(view);
            _cardViews[card.Id] = view;
            _surface.Children.Add(view.Root);
            ReplayHistory(view);
        }
        _emptyState.Visibility = _layout.Cards.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSizeLockButton();
        _placementDirty = true;
        RequestApplyReadings(pushGraphs: false);
    }

    private void RequestApplyReadings(bool pushGraphs)
    {
        _readingsApplyPending = true;
        _pendingGraphPush |= pushGraphs;
        if (_deferredUpdateDepth == 0)
            FlushDeferredUpdates();
    }

    private void FlushDeferredUpdates()
    {
        if (!_readingsApplyPending) return;
        var pushGraphs = _pendingGraphPush;
        _readingsApplyPending = false;
        _pendingGraphPush = false;
        ApplyReadings(pushGraphs);
    }

    private void ApplyReadings(bool pushGraphs)
    {
        var geometryChanged = false;
        foreach (var view in _cardViews.Values)
            geometryChanged |= view.Apply(_readings, pushGraphs);
        _placementDirty |= geometryChanged;
        var width = _dashboard.ActualWidth;
        if (_dragView is null && _resizeView is null
            && (_placementDirty || !SameLength(width, _lastPlacementWidth)))
            ApplyPlacement(width);
    }

    private void CompleteDeferredUpdate()
    {
        if (_deferredUpdateDepth <= 0) return;
        _deferredUpdateDepth--;
        if (_deferredUpdateDepth == 0)
            FlushDeferredUpdates();
    }

    private static bool SameLength(double first, double second)
        => double.IsFinite(first) && double.IsFinite(second) && Math.Abs(first - second) < .1;

    private static bool LayoutsMatch(OverviewDashboardLayout first, OverviewDashboardLayout second)
        => first.Version == second.Version
           && first.CardSizesLocked == second.CardSizesLocked
           && first.LockedSurfaceWidth.Equals(second.LockedSurfaceWidth)
           && first.Cards.Count == second.Cards.Count
           && first.Cards.Zip(second.Cards).All(pair =>
               string.Equals(pair.First.Id, pair.Second.Id, StringComparison.OrdinalIgnoreCase)
               && pair.First.Bounds == pair.Second.Bounds
               && pair.First.MetricIds.SequenceEqual(pair.Second.MetricIds, StringComparer.Ordinal)
               && (pair.First.ChartMetricIds ?? []).SequenceEqual(pair.Second.ChartMetricIds ?? [], StringComparer.Ordinal));

    private void ReplayHistory(OverviewDashboardCardView view)
    {
        foreach (var (metricId, graph) in view.Graphs)
        {
            if (!_histories.TryGetValue(metricId, out var history)) continue;
            foreach (var reading in history.Readings)
                graph.Push(reading.RuntimeKey, reading.Primary, reading.Secondary);
        }
    }

    private IReadOnlyList<OverviewDashboardMetricDefinition> AvailableDefinitions(IEnumerable<string> configuredMetricIds)
        => _registry.Definitions(configuredMetricIds)
            .Where(IsDefinitionAvailable)
            .ToArray();

    private bool IsDefinitionAvailable(OverviewDashboardMetricDefinition definition)
        => definition.PickerVisible
           && (!definition.RequiresObservedValue
           || (_readings.TryGetValue(definition.Id, out var reading)
               && reading.Primary is { } primary
               && double.IsFinite(primary)));

    private static Grid DashboardHeader(out StackPanel dashboardActions)
    {
        var header = new Grid { Margin = new Thickness(0, 8, 0, 8) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(new TextBlock
        {
            Text = Loc.T("Overview.ModelStatusLabel"),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        dashboardActions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        Grid.SetColumn(dashboardActions, 1);
        header.Children.Add(dashboardActions);
        return header;
    }

    private static WpfButton QuietButton(string text)
    {
        var button = new WpfButton { Content = text, MinHeight = 28, Margin = new Thickness(8, 0, 0, 0) };
        VisualRole.SetButtonRole(button, VisualRole.Quiet);
        return button;
    }

    private sealed class DashboardMetricHistory
    {
        public string RuntimeKey { get; set; } = "";
        public List<OverviewDashboardMetricReading> Readings { get; } = [];
    }

    private sealed class DeferredUpdateScope : IDisposable
    {
        private OverviewDashboardController? _owner;

        public DeferredUpdateScope(OverviewDashboardController owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            owner?.CompleteDeferredUpdate();
        }
    }
}
