using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public abstract partial class WpfUiTestBase
{
    protected static readonly string TestWorkspace = CreateTestWorkspace();
    private static readonly Lazy<Dispatcher> TestDispatcher = new(CreateTestDispatcher);
    protected static void AssertDashboardPolish(LocalLlmConsole.OverviewPageControls overview)
    {
        Assert.Same(overview.Root, overview.Scroller.Content);
        Assert.Equal(ScrollBarVisibility.Hidden, overview.Scroller.VerticalScrollBarVisibility);
        Assert.Equal(ScrollBarVisibility.Disabled, overview.Scroller.HorizontalScrollBarVisibility);

        var dashboard = overview.DashboardController;
        var rightmost = dashboard.Cards.Max(card => Canvas.GetLeft(card.Root) + card.Root.Width);
        Assert.InRange(rightmost, 1, dashboard.DashboardGrid.ActualWidth + 1);

        var card = dashboard.Cards.First(item => item.MetricIds.Count > 2);
        var metricRows = VisualDescendants<Grid>(card.Root)
            .Where(grid => grid.Tag is OverviewDashboardMetricRowView)
            .ToArray();
        Assert.NotEmpty(metricRows);
        Assert.All(metricRows, row => Assert.Contains("Technical metric:", row.ToolTip?.ToString(), StringComparison.Ordinal));
        var tokenCard = dashboard.Cards.Single(item =>
            item.MetricIds.Contains(OverviewDashboardMetricIds.AverageGenerationRate));
        var tokenRow = VisualDescendants<Grid>(tokenCard.Root)
            .Single(grid => grid.Tag is OverviewDashboardMetricRowView row
                            && row.MetricId == OverviewDashboardMetricIds.AverageGenerationRate);
        Assert.True(tokenRow.ColumnDefinitions[0].Width.IsStar);
        Assert.True(tokenRow.ColumnDefinitions[1].Width.IsAuto);
        var tokenLabel = VisualDescendants<TextBlock>(tokenRow)
            .Single(text => text.Text == "Average generation rate");
        Assert.InRange(tokenLabel.ActualHeight, 1, 18);
        var original = card.CurrentMetricOrder.ToArray();
        var reorder = card.Root.ContextMenu!.Items.OfType<MenuItem>()
            .Single(item => Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.Reorder")));
        Assert.True(reorder.IsEnabled);
        reorder.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        var firstRow = metricRows.Single(row =>
            ((OverviewDashboardMetricRowView)row.Tag).MetricId == original[0]);
        Assert.Equal(Cursors.SizeNS, firstRow.Cursor);
        Assert.Contains(VisualDescendants<TextBlock>(card.Root), text => text.Text == "⠿" && text.Visibility == Visibility.Visible);
        var rowStack = Assert.IsType<StackPanel>(firstRow.Parent);
        rowStack.Children.Remove(firstRow);
        rowStack.Children.Insert(1, firstRow);
        Assert.Equal(original[0], card.CurrentMetricOrder[1]);

        var outsideRow = VisualDescendants<TextBlock>((DependencyObject)dashboard.Root)
            .First(text => text.Text == LocalLlmConsole.Localization.Loc.T("Overview.ModelStatusLabel"));
        outsideRow.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = Mouse.PreviewMouseDownEvent,
            Source = outsideRow
        });
        var persisted = dashboard.Layout.Cards.Single(item => item.Id == card.Layout.Id).MetricIds;
        Assert.Equal(card.CurrentMetricOrder, persisted);
        Assert.DoesNotContain(
            VisualDescendants<TextBlock>(dashboard.Cards.Single(item => item.Layout.Id == card.Layout.Id).Root),
            text => text.Text == "⠿" && text.Visibility == Visibility.Visible);
        AssertDashboardSizeLock(overview);
        AssertDashboardOptionalCardTitle(dashboard);
    }

    protected static void AssertHiddenDashboardOverflow(LocalLlmConsole.OverviewPageControls overview)
    {
        var dashboard = overview.DashboardController;
        var regularLayout = dashboard.Layout;
        var overflowCard = regularLayout.Cards[0];
        dashboard.ApplyLayout(OverviewDashboardLayoutPolicy.SetCardBounds(
            regularLayout, overflowCard.Id, overflowCard.Bounds! with { Y = 1000 }));
        overview.Scroller.Measure(new Size(900, 360));
        overview.Scroller.Arrange(new Rect(0, 0, 900, 360));
        overview.Scroller.UpdateLayout();
        Assert.True(overview.Scroller.ExtentHeight > overview.Scroller.ViewportHeight);
        overview.Scroller.ScrollToVerticalOffset(120);
        overview.Scroller.UpdateLayout();
        Assert.True(overview.Scroller.VerticalOffset > 0);
        dashboard.ApplyLayout(regularLayout);
    }

    protected static ContextMenu OpenContextMenu(FrameworkElement target)
    {
        var menu = target.ContextMenu!;
        menu.PlacementTarget = target;
        menu.IsOpen = true;
        return menu;
    }

    protected static void AssertHardwareChartHistoryAndOptionalSensors(
        OverviewDashboardController dashboard)
    {
        var hardwareCardId = dashboard.Layout.Cards.Single(card =>
            card.MetricIds.Contains(OverviewDashboardMetricIds.Cpu)).Id;
        var hardwareChartLayout = OverviewDashboardLayoutPolicy.SetChartVisibility(
            OverviewDashboardLayoutPolicy.AddMetrics(
                dashboard.Layout, hardwareCardId, [OverviewDashboardMetricIds.CpuTemperature]),
            hardwareCardId,
            OverviewDashboardMetricIds.Cpu,
            true);
        dashboard.ApplyLayout(hardwareChartLayout);
        var cpuCard = dashboard.Cards.Single(card =>
            card.MetricIds.Contains(OverviewDashboardMetricIds.Cpu));
        var initialSampleCount = cpuCard.Graphs[OverviewDashboardMetricIds.Cpu].SampleCount;
        Assert.True(initialSampleCount >= 1);

        dashboard.ApplyMetricSummary(RuntimeMetricSummaryPresentation.NoRuntime);
        dashboard.ApplyLayout(dashboard.Layout);
        cpuCard = dashboard.Cards.Single(card =>
            card.MetricIds.Contains(OverviewDashboardMetricIds.Cpu));
        Assert.Equal(initialSampleCount, cpuCard.Graphs[OverviewDashboardMetricIds.Cpu].SampleCount);

        dashboard.ApplyHardwareSummary(
            "CPU: AMD Ryzen 9 7950X\nTelemetry: 24.0% load | 16C/32T\n" +
            "RAM: 12.5/32.0 GiB | 39.1%\n" +
            "GPU 0: AMD Radeon RX 7900 XTX | 61.0% | 8.0/24.0 GiB");

        Assert.Equal(initialSampleCount + 1, cpuCard.Graphs[OverviewDashboardMetricIds.Cpu].SampleCount);
        var unavailableTemperatureRow = VisualDescendants<Grid>(cpuCard.Root)
            .Single(grid => grid.Tag is OverviewDashboardMetricRowView row
                            && row.MetricId == OverviewDashboardMetricIds.CpuTemperature);
        Assert.Equal(Visibility.Collapsed, unavailableTemperatureRow.Visibility);
        AssertDashboardDefersGraphPushesToOnePerFrame();
    }

    protected static void AssertDashboardDefersGraphPushesToOnePerFrame()
    {
        var layout = OverviewDashboardLayoutPolicy.SetChartVisibility(
            new OverviewDashboardLayout(
                OverviewDashboardLayoutPolicy.CurrentVersion,
                [new OverviewDashboardCardLayout(
                    "cpu",
                    [OverviewDashboardMetricIds.Cpu],
                    Bounds: new OverviewDashboardCardBounds(0, 0, 4, 112))]),
            "cpu",
            OverviewDashboardMetricIds.Cpu,
            true);
        var dashboard = new OverviewDashboardController(layout,
            new OverviewDashboardControllerActions(_ => Task.CompletedTask, action => action()));

        using (dashboard.DeferUpdates())
        {
            dashboard.ApplyHardwareSummary("CPU: First\nTelemetry: 10% load");
            dashboard.ApplyHardwareSummary("CPU: Second\nTelemetry: 20% load");
        }

        var graph = Assert.Single(dashboard.Cards).Graphs[OverviewDashboardMetricIds.Cpu];
        Assert.Equal(1, graph.SampleCount);
    }

    protected static void AssertDashboardGraphsAreNamed(
        IEnumerable<OverviewDashboardCardView> graphCards)
    {
        var registry = new OverviewDashboardMetricRegistry();
        Assert.All(graphCards, card =>
        {
            Assert.All(card.Graphs, graphEntry =>
            {
                var metricRow = Assert.IsType<Grid>(graphEntry.Value.Parent);
                var chartName = registry.Definition(graphEntry.Key).DisplayName;
                Assert.Contains(VisualDescendants<TextBlock>(metricRow), text => text.Text == chartName);
            });
        });
    }

    protected static void AssertHiddenDashboardCardsDoNotReserveSpace()
    {
        var layout = OverviewDashboardLayoutPolicy.Normalize(new OverviewDashboardLayout(6,
        [
            new("cpu", [OverviewDashboardMetricIds.Cpu],
                Bounds: new OverviewDashboardCardBounds(0, 0, 4, 112)),
            new("gpu-0", [OverviewDashboardMetricIds.Gpu(0)],
                Bounds: new OverviewDashboardCardBounds(4, 0, 4, 112)),
            new("unavailable-gpu", [OverviewDashboardMetricIds.Gpu(2)],
                Bounds: new OverviewDashboardCardBounds(8, 0, 4, 112)),
            new("status", [OverviewDashboardMetricIds.ModelStatus],
                Bounds: new OverviewDashboardCardBounds(8, 0, 4, 112))
        ]));
        var dashboard = new OverviewDashboardController(layout,
            new OverviewDashboardControllerActions(_ => Task.CompletedTask, action => action()));
        dashboard.SetMetricValue(OverviewDashboardMetricIds.ModelStatus, "Ready");
        dashboard.ApplyHardwareSummary(
            "CPU: AMD Ryzen 9 7950X\nTelemetry: 24.0% load | 16C/32T\n" +
            "GPU 0: AMD Radeon RX 7900 XTX | 61.0% | 8.0/24.0 GiB");
        dashboard.DashboardGrid.Measure(new Size(900, 1000));
        dashboard.DashboardGrid.Arrange(new Rect(0, 0, 900, 1000));
        dashboard.DashboardGrid.UpdateLayout();

        var hidden = dashboard.Cards.Single(card => card.Layout.Id == "unavailable-gpu");
        Assert.Equal(Visibility.Collapsed, hidden.Root.Visibility);
        var visible = dashboard.Cards.Where(card => card.Root.Visibility == Visibility.Visible).ToArray();
        Assert.Equal([0, 300, 600], visible.Select(card => Canvas.GetLeft(card.Root)));
        Assert.All(visible, card => Assert.Equal(0, Canvas.GetTop(card.Root)));
    }

    protected static LoadedModelSessionSnapshot RunningSession(AppSettings settings)
        => new(
            "session-1",
            "model-1",
            "Qwen",
            "runtime-1",
            "Official CPU",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            "runtime.log",
            DateTimeOffset.UtcNow,
            "",
            123,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true,
            LaunchProfileId: "default:model-1",
            LaunchProfileName: "Default");

    protected static ModelRecord RunningModel()
        => new(
            "model-1",
            "Qwen",
            Path.Combine(Path.GetTempPath(), "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);

    protected static IEnumerable<T> VisualDescendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T match) yield return match;
            foreach (var descendant in VisualDescendants<T>(child)) yield return descendant;
        }
    }

    protected static Border MetricCard(Grid metric)
        => Assert.IsType<Border>(Assert.IsType<StackPanel>(metric.Parent).Parent);

    protected static void AssertContextMenu(DataGrid grid, object row, params string[] expectedHeaders)
    {
        grid.SelectedItem = row;
        grid.ContextMenu!.IsOpen = true;
        Assert.Equal(expectedHeaders, grid.ContextMenu.Items.OfType<MenuItem>().Select(item => item.Header).ToArray());
        grid.ContextMenu.IsOpen = false;
    }

    protected static void AssertGridActionButtonMatches(Button actual, DataGrid grid, string expectedPeerContent)
    {
        var expected = VisualDescendants<Button>(grid)
            .Single(button => Equals(button.Content, expectedPeerContent)
                              && ReferenceEquals(button.DataContext, actual.DataContext));
        Assert.True(double.IsNaN(actual.Height));
        Assert.Equal(expected.ActualHeight, actual.ActualHeight, precision: 1);
        Assert.Equal(expected.Padding, actual.Padding);
        Assert.Equal(expected.Margin, actual.Margin);
    }

    protected static void AssertDashboardCardsSeparated(LocalLlmConsole.OverviewDashboardController dashboard)
    {
        var cards = dashboard.Cards.ToArray();
        var gap = LocalLlmConsole.Services.OverviewDashboardLayoutPolicy.CardGap;
        for (var firstIndex = 0; firstIndex < cards.Length; firstIndex++)
        {
            var first = cards[firstIndex].Root;
            var firstRect = new Rect(Canvas.GetLeft(first), Canvas.GetTop(first), first.ActualWidth, first.ActualHeight);
            for (var secondIndex = firstIndex + 1; secondIndex < cards.Length; secondIndex++)
            {
                var second = cards[secondIndex].Root;
                var secondRect = new Rect(Canvas.GetLeft(second), Canvas.GetTop(second), second.ActualWidth, second.ActualHeight);
                var separated = firstRect.Right + gap <= secondRect.Left + .1
                                || secondRect.Right + gap <= firstRect.Left + .1
                                || firstRect.Bottom + gap <= secondRect.Top + .1
                                || secondRect.Bottom + gap <= firstRect.Top + .1;
                Assert.True(separated,
                    $"Dashboard cards {cards[firstIndex].Layout.Id} and {cards[secondIndex].Layout.Id} overlap: {firstRect} / {secondRect}.");
            }
        }
    }

    protected static void AssertDashboardSubmenuTemplate(MenuItem menu)
    {
        menu.ApplyTemplate();
        Assert.Equal(new Thickness(8, 4, 8, 4), menu.Padding);
        Assert.Equal(30, menu.MinHeight);
        var checkColumn = Assert.IsType<ColumnDefinition>(menu.Template.FindName("CheckColumn", menu));
        var submenuColumn = Assert.IsType<ColumnDefinition>(menu.Template.FindName("SubmenuColumn", menu));
        Assert.Equal(new GridLength(0), checkColumn.Width);
        Assert.Equal(new GridLength(16), submenuColumn.Width);
        var popup = Assert.IsType<Popup>(menu.Template.FindName("PART_Popup", menu));
        Assert.Equal(PlacementMode.Right, popup.Placement);
        var popupChrome = Assert.IsType<Border>(popup.Child);
        Assert.Equal(new Thickness(4), popupChrome.Padding);
        Assert.Equal(new CornerRadius(8), popupChrome.CornerRadius);
    }

    protected static void AssertDashboardCheckItemTemplate(MenuItem menu)
    {
        menu.ApplyTemplate();
        var checkColumn = Assert.IsType<ColumnDefinition>(menu.Template.FindName("CheckColumn", menu));
        var submenuColumn = Assert.IsType<ColumnDefinition>(menu.Template.FindName("SubmenuColumn", menu));
        Assert.Equal(new GridLength(20), checkColumn.Width);
        Assert.Equal(new GridLength(0), submenuColumn.Width);
    }

    protected static void AssertUnavailableMetricPolish()
    {
        Assert.Equal("N/A", LocalLlmConsole.Localization.Loc.T("Dashboard.ValueUnavailable"));
        var definition = OverviewDashboardMetricRegistry.BuiltInDefinitions()
            .Single(definition => definition.Id == OverviewDashboardMetricIds.AverageGenerationRate);
        var row = new OverviewDashboardMetricRowView(
            definition,
            null,
            false);
        var label = VisualDescendants<TextBlock>(row.Root)
            .Single(block => block.Text == definition.DisplayName);
        Assert.Equal(10.25, label.FontSize);

        row.Apply(new OverviewDashboardMetricReading(definition.Id, "42.5", Unit: "tok/s"));
        var measuredValue = VisualDescendants<TextBlock>(row.Root)
            .Single(block => block.Text == "42.5");
        var unit = VisualDescendants<TextBlock>(row.Root)
            .Single(block => block.Text == "tok/s");
        Assert.Equal(14.5, measuredValue.FontSize);
        Assert.Equal(9, unit.FontSize);
        var valueLine = Assert.IsType<Grid>(unit.Parent);
        Assert.True(valueLine.ColumnDefinitions[1].Width.IsAuto);
        Assert.Equal(36, valueLine.ColumnDefinitions[1].MaxWidth);

        row.Apply(null);
        var value = VisualDescendants<TextBlock>(row.Root)
            .Single(block => block.Text == LocalLlmConsole.Localization.Loc.T("Dashboard.ValueUnavailable"));
        Assert.Equal(11.75, value.FontSize);
        Assert.Equal(FontWeights.Medium, value.FontWeight);
    }

    protected static void AssertDashboardSizeLock(OverviewPageControls overview)
    {
        var dashboard = overview.DashboardController;
        var originalLayout = dashboard.Layout;
        var originalWidth = overview.Root.ActualWidth;
        var originalHeight = overview.Root.ActualHeight;
        var lockButton = VisualDescendants<Button>(overview.Root)
            .Single(button => Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Dashboard.Lock"))
                              || Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Dashboard.Unlock")));
        if (dashboard.Layout.CardSizesLocked)
        {
            lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(dashboard.Layout.CardSizesLocked);
        }
        var cardId = dashboard.Cards[0].Layout.Id;
        var widthBeforeLock = dashboard.Cards[0].Root.Width;

        lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(dashboard.Layout.CardSizesLocked);
        Assert.Equal(LocalLlmConsole.Localization.Loc.T("Dashboard.Unlock"), lockButton.Content);
        Assert.All(dashboard.Cards, card => Assert.Equal(
            (OverviewDashboardResizeEdge)0,
            card.ResizeEdgeAt(new Point(1, card.Root.Height / 2))));

        var narrowerWidth = Math.Max(700, originalWidth - 180);
        overview.Root.Measure(new Size(narrowerWidth, originalHeight));
        overview.Root.Arrange(new Rect(0, 0, narrowerWidth, originalHeight));
        overview.Root.UpdateLayout();
        var lockedCard = dashboard.Cards.Single(card => card.Layout.Id == cardId);
        Assert.Equal(widthBeforeLock, lockedCard.Root.Width, precision: 1);

        lockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(dashboard.Layout.CardSizesLocked);
        Assert.Equal(LocalLlmConsole.Localization.Loc.T("Dashboard.Lock"), lockButton.Content);
        Assert.NotEqual((OverviewDashboardResizeEdge)0,
            dashboard.Cards[0].ResizeEdgeAt(new Point(1, dashboard.Cards[0].Root.Height / 2)));

        dashboard.ApplyLayout(originalLayout);
        overview.Root.Measure(new Size(originalWidth, originalHeight));
        overview.Root.Arrange(new Rect(0, 0, originalWidth, originalHeight));
        overview.Root.UpdateLayout();
    }

    protected static void AssertDashboardOptionalCardTitle(OverviewDashboardController dashboard)
    {
        var original = dashboard.Layout;
        var cardId = original.Cards[0].Id;
        var titled = OverviewDashboardLayoutPolicy.SetCardTitle(original, cardId, "Primary telemetry");
        dashboard.ApplyLayout(titled);
        var card = dashboard.Cards.Single(item => item.Layout.Id == cardId);
        var title = VisualDescendants<TextBlock>(card.Root)
            .Single(text => text.Text == "Primary telemetry");
        Assert.Equal(FontWeights.SemiBold, title.FontWeight);
        Assert.Contains(card.Root.ContextMenu!.Items.OfType<MenuItem>(), item =>
            Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.CardTitle")));

        dashboard.ApplyLayout(OverviewDashboardLayoutPolicy.SetCardTitle(titled, cardId, ""));
        Assert.DoesNotContain(
            VisualDescendants<TextBlock>(dashboard.Cards.Single(item => item.Layout.Id == cardId).Root),
            text => text.Text == "Primary telemetry");
        dashboard.ApplyLayout(original);
    }

    protected static void EnsureDashboardCardSizesUnlocked(
        OverviewPageControls overview,
        OverviewDashboardController dashboard)
    {
        if (dashboard.Layout.CardSizesLocked)
        {
            var unlockButton = VisualDescendants<Button>(overview.Root)
                .Single(button => Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Dashboard.Unlock")));
            unlockButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        Assert.False(dashboard.Layout.CardSizesLocked);
        Assert.Contains(VisualDescendants<Button>(overview.Root),
            button => Equals(button.Content, LocalLlmConsole.Localization.Loc.T("Dashboard.Lock")));
    }

    protected static (AppSettings Settings, LocalLlmConsole.OverviewPageControls Overview) CreateOverviewSurface()
    {
        var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-overview-smoke"));
        var viewModel = new LocalLlmConsole.ViewModels.MainWindowViewModel();
        viewModel.Overview.ReplaceSessions([RunningSession(settings)]);
        viewModel.Overview.ReplaceLaunchProfiles(
        [
            new NamedModelLaunchProfile(
                "default:model-1",
                "model-1",
                "Default",
                ModelLaunchSettings.FromAppSettings(settings),
                DateTimeOffset.UtcNow,
                true)
        ]);
        var overview = LocalLlmConsole.OverviewPageFactory.Create(new LocalLlmConsole.OverviewPageRequest(
            viewModel,
            new LocalLlmConsole.OverviewPageActions(
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                () => Task.CompletedTask,
                (_, _) => { },
                (_, _) => { },
                _ => Task.CompletedTask,
                action => action(),
                action => action()),
            _ => { },
            OverviewDashboardLayoutPolicy.Default));
        overview.LaunchProfileCombo.SelectedIndex = 0;
        overview.Root.Measure(new Size(900, 680));
        overview.Root.Arrange(new Rect(0, 0, 900, 680));
        overview.Root.UpdateLayout();
        return (settings, overview);
    }

    protected static async Task RunStaAsync(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ = TestDispatcher.Value.BeginInvoke(new Action(() =>
        {
            try
            {
                LocalLlmConsole.Localization.Loc.LoadLanguage("en");
                action();
                DeleteTestWorkspace();
                completion.SetResult();
            }
            catch (Exception ex)
            {
                completion.SetException(ex);
            }
        }));
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    private static Dispatcher CreateTestDispatcher()
    {
        _ = TestWorkspace;
        Dispatcher? dispatcher = null;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            if (System.Windows.Application.Current is null)
            {
                var app = new System.Windows.Application
                {
                    ShutdownMode = ShutdownMode.OnExplicitShutdown
                };
                foreach (var resource in new[]
                         {
                             "Palette.xaml",
                             "Foundation.xaml",
                             "Inputs.xaml",
                             "DataAndSurfaces.xaml"
                         })
                {
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/LlamaCppWindowsManager;component/Themes/{resource}", UriKind.Relative)
                    });
                }
            }
            dispatcher = Dispatcher.CurrentDispatcher;
            ready.Set();
            Dispatcher.Run();
        })
        {
            IsBackground = true,
            Name = "LLWM WPF test dispatcher"
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        ready.Wait(TimeSpan.FromSeconds(10));
        return dispatcher ?? throw new InvalidOperationException("The WPF test dispatcher did not start.");
    }

    private static string CreateTestWorkspace()
    {
        var root = Path.Combine(Path.GetTempPath(), $"llwm-ui-tests-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Environment.SetEnvironmentVariable(WorkspaceRootResolver.EnvironmentVariable, root);
        return root;
    }

    private static void DeleteTestWorkspace()
    {
        if (Directory.Exists(TestWorkspace))
            Directory.Delete(TestWorkspace, recursive: true);
    }

    protected static void DetachLoadedStartup(LocalLlmConsole.MainWindow window)
    {
        var method = typeof(LocalLlmConsole.MainWindow).GetMethod(
            "Window_Loaded",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?? throw new MissingMethodException(typeof(LocalLlmConsole.MainWindow).FullName, "Window_Loaded");
        var handler = (RoutedEventHandler)method.CreateDelegate(typeof(RoutedEventHandler), window);
        window.Loaded -= handler;
    }
}
