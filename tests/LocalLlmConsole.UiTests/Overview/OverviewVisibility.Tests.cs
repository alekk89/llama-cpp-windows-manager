using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewVisibilityTests : WpfUiTestBase
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task SavedSplitterCannotOverrideOverviewVisibility(bool dashboard, bool log, bool metrics)
    {
        await RunStaAsync(async () =>
        {
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"overview-visibility-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();
            var (settings, first) = CreateOverviewSurface();
            var firstHost = new ContentControl { Content = first.Scroller };
            var firstWindow = new Window { Content = firstHost, Width = 1100, Height = 850, ShowInTaskbar = false };
            try
            {
                firstWindow.Show();
                var persistence = new UiLayoutPersistenceService(store);
                await persistence.AttachShellAsync(firstWindow, firstHost, () => "Overview");
                // Reproduce a layout saved when the log/metrics splitter was resizable.
                first.Root.RowDefinitions[2].Height = new GridLength(350);
                first.Root.RowDefinitions[4].Height = new GridLength(260);
                await persistence.SaveShellAsync();
            }
            finally { firstWindow.Close(); }

            var (_, page) = CreateOverviewSurface();
            var state = new OverviewPageState();
            state.Apply(page);
            var preferences = settings with
            {
                ShowOverviewModelSection = dashboard,
                ShowOverviewLiveRuntimeLog = log,
                ShowOverviewAllMetrics = metrics
            };
            state.ApplyUiPreferences(preferences);
            var expectedRows = page.Root.RowDefinitions.Select(row => row.Height).ToArray();
            var savedCards = page.DashboardController.Layout;
            var host = new ContentControl { Content = page.Scroller };
            var window = new Window { Content = host, Width = 1100, Height = 850, ShowInTaskbar = false };
            try
            {
                window.Show();
                var persistence = new UiLayoutPersistenceService(store);
                await persistence.AttachShellAsync(window, host, () => "Overview");
                window.UpdateLayout();
                Assert.Equal(expectedRows, page.Root.RowDefinitions.Select(row => row.Height).ToArray());
                AssertSections(page, dashboard, log, metrics);

                for (var toggle = 0; toggle < 2; toggle++)
                {
                    state.ApplyUiPreferences(preferences with
                    {
                        ShowOverviewModelSection = true,
                        ShowOverviewLiveRuntimeLog = true,
                        ShowOverviewAllMetrics = true
                    });
                    window.UpdateLayout();
                    AssertSections(page, true, true, true);
                    state.ApplyUiPreferences(preferences with
                    {
                        ShowOverviewModelSection = false,
                        ShowOverviewLiveRuntimeLog = false,
                        ShowOverviewAllMetrics = false
                    });
                    window.UpdateLayout();
                    AssertSections(page, false, false, false);
                    Assert.Same(savedCards, page.DashboardController.Layout);
                }
            }
            finally { window.Close(); }
        });
    }

    [Theory]
    [InlineData("showOverviewModelStatus")]
    [InlineData("showOverviewHardware")]
    [InlineData("showOverviewSlots")]
    [InlineData("showOverviewTokens")]
    [InlineData("showOverviewMtpTokens")]
    [InlineData("showOverviewKvCache")]
    public async Task LegacyMetricSwitchesRemoveTheirContentWithoutEmptyCards(string key)
    {
        await RunStaAsync(() =>
        {
            var (settings, page) = CreateOverviewSurface();
            var state = new OverviewPageState();
            state.Apply(page);
            var updates = new AppSettingsUpdateService();
            for (var toggle = 0; toggle < 2; toggle++)
            {
                var shown = updates.Build(new AppSettingsUpdateRequest(settings, settings.WorkspaceRoot,
                    settings.ThemeMode, new Dictionary<string, string> { [key] = "Show" }, new HashSet<int>()));
                Assert.True(shown.Success, shown.StatusMessage);
                state.ApplyUiPreferences(shown.Settings);
                var shownIds = page.DashboardController.Cards.SelectMany(card => card.MetricIds).ToHashSet();

                var hidden = updates.Build(new AppSettingsUpdateRequest(shown.Settings, settings.WorkspaceRoot,
                    settings.ThemeMode, new Dictionary<string, string> { [key] = "Hide" }, new HashSet<int>()));
                Assert.True(hidden.Success, hidden.StatusMessage);
                state.ApplyUiPreferences(hidden.Settings);
                page.Root.UpdateLayout();
                var hiddenIds = page.DashboardController.Cards.SelectMany(card => card.MetricIds).ToHashSet();
                Assert.NotEmpty(shownIds.Except(hiddenIds));
                Assert.All(page.DashboardController.Cards, card => Assert.NotEmpty(card.MetricIds));
                Assert.Equal(page.DashboardController.Cards.Count, page.DashboardController.DashboardCanvas.Children.Count);
                settings = hidden.Settings;
            }
        });
    }

    private static void AssertSections(OverviewPageControls page, bool dashboard, bool log, bool metrics)
    {
        AssertSection(page.ModelStatusSection, 1, dashboard);
        AssertSection(page.RuntimeLogSection, 2, log);
        AssertSection(page.MetricsSection, 4, metrics);
        AssertCollapsedRow(3);
        Assert.Equal(Visibility.Collapsed, page.RuntimeSectionsSplitter.Visibility);
        Assert.True(page.Root.RowDefinitions[0].ActualHeight > 0);

        void AssertSection(FrameworkElement section, int row, bool visible)
        {
            Assert.Equal(visible ? Visibility.Visible : Visibility.Collapsed, section.Visibility);
            if (visible) Assert.True(page.Root.RowDefinitions[row].ActualHeight > 0);
            else AssertCollapsedRow(row);
        }

        void AssertCollapsedRow(int row)
        {
            Assert.Equal(new GridLength(0), page.Root.RowDefinitions[row].Height);
            // Grid distributes at most one physical pixel of layout rounding to an empty row.
            var pixel = 1 / System.Windows.Media.VisualTreeHelper.GetDpi(page.Root).DpiScaleY;
            Assert.InRange(page.Root.RowDefinitions[row].ActualHeight, 0, pixel + .001);
        }
    }
}
