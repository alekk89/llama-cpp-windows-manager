using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewLoadedModelsTests : WpfUiTestBase
{
    [Fact]
    public async Task LoadedModelsCardListsSessionsAndSelectsThroughTheSessionGrid()
    {
        await RunStaAsync(() =>
        {
            var settings = AppSettings.CreateDefault(Path.Combine(Path.GetTempPath(), "wpf-loaded-models"));
            var first = RunningSession(settings);
            var second = first with
            {
                SessionId = "session-2",
                ModelId = "model-2",
                ModelName = "Gemma",
                LaunchProfileName = "Long context",
                IsSelected = false,
                LaunchSettings = settings with { Port = 8090 }
            };
            var viewModel = new MainWindowViewModel();
            viewModel.Overview.ReplaceSessions([first, second]);
            var selections = 0;
            var layout = OverviewDashboardLayoutPolicy.AddCard(
                OverviewDashboardLayoutPolicy.Default, [OverviewDashboardMetricIds.LoadedModels]);
            var overview = OverviewPageFactory.Create(new OverviewPageRequest(
                viewModel,
                new OverviewPageActions(
                    () => Task.CompletedTask,
                    () => Task.CompletedTask,
                    () => Task.CompletedTask,
                    () =>
                    {
                        selections++;
                        return Task.CompletedTask;
                    },
                    () => Task.CompletedTask,
                    (_, _) => { },
                    (_, _) => { },
                    _ => Task.CompletedTask,
                    action => action(),
                    action => action()),
                _ => { },
                layout));
            overview.Root.Measure(new Size(1100, 900));
            overview.Root.Arrange(new Rect(0, 0, 1100, 900));
            overview.Root.UpdateLayout();
            var dashboard = overview.DashboardController;

            var card = Assert.Single(dashboard.Cards, item => item.LoadedModels is not null);
            Assert.Equal(Visibility.Visible, card.Root.Visibility);
            Assert.Contains(LocalLlmConsole.Localization.Loc.T("Dashboard.LoadedModels.Empty"),
                VisualDescendants<TextBlock>(card.Root).Where(text => text.Visibility == Visibility.Visible).Select(text => text.Text));
            Assert.Empty(card.LoadedModels!.SessionButtons);

            dashboard.ApplyLoadedSessions(
            [
                new LoadedSessionDashboardRow("session-2", "model-2", "Gemma", "Long context",
                    LoadedModelSessionStatus.Running, false, 8090, 41.2, 1, 4, 37.6),
                new LoadedSessionDashboardRow("session-1", "model-1", "Qwen", "Default",
                    LoadedModelSessionStatus.Unreachable, true, 8081, null, null, null, null)
            ]);
            overview.Root.UpdateLayout();

            card = Assert.Single(dashboard.Cards, item => item.LoadedModels is not null);
            var buttons = card.LoadedModels!.SessionButtons;
            Assert.Equal(2, buttons.Count);
            var texts = VisualDescendants<TextBlock>(card.Root).Where(text => text.Visibility == Visibility.Visible).Select(text => text.Text).ToArray();
            Assert.DoesNotContain(LocalLlmConsole.Localization.Loc.T("Dashboard.LoadedModels.Empty"), texts);
            Assert.Contains("2", texts);
            Assert.Contains("Gemma · Long context", texts);
            Assert.Contains(texts, text => text.Contains("41.2 t/s", StringComparison.Ordinal)
                && text.Contains("1/4", StringComparison.Ordinal)
                && text.Contains("38%", StringComparison.Ordinal)
                && text.Contains("8090", StringComparison.Ordinal));
            Assert.Contains(texts, text => text.StartsWith("N/A", StringComparison.Ordinal) && text.Contains("8081", StringComparison.Ordinal));
            Assert.All(buttons, button =>
            {
                Assert.True(button.Focusable);
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)));
                Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(button)));
            });
            Assert.Contains(LocalLlmConsole.Localization.Loc.T("Dashboard.LoadedModels.Selected"),
                AutomationProperties.GetName(buttons[1]), StringComparison.Ordinal);

            var focused = buttons[0];
            dashboard.ApplyLoadedSessions(
            [
                new LoadedSessionDashboardRow("session-2", "model-2", "Gemma", "Long context",
                    LoadedModelSessionStatus.Running, false, 8090, 42, 2, 4, 40),
                new LoadedSessionDashboardRow("session-1", "model-1", "Qwen", "Default",
                    LoadedModelSessionStatus.Running, true, 8081, 10, 0, 1, 5)
            ]);
            Assert.Same(focused, card.LoadedModels.SessionButtons[0]);

            focused.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            var selectedRow = Assert.IsType<OverviewSessionRow>(overview.LoadedSessionsGrid.SelectedItem);
            Assert.Equal("session-2", selectedRow.SessionId);
            Assert.Equal(1, selections);
            focused.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            Assert.Equal(1, selections);

            var menu = OpenContextMenu(card.Root);
            var addMetrics = menu.Items.OfType<MenuItem>()
                .Single(item => Equals(item.Header, LocalLlmConsole.Localization.Loc.T("Dashboard.AddMetrics")));
            Assert.False(addMetrics.IsEnabled);
            menu.IsOpen = false;

            dashboard.ApplyLoadedSessions([]);
            Assert.Empty(card.LoadedModels.SessionButtons);
            Assert.Equal(Visibility.Visible, card.Root.Visibility);
        });
    }
}
