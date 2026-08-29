using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class WpfOverviewSessionTests : WpfUiTestBase
{
    [Fact]
    public async Task OverviewSessionActionsAndRuntimeLogRenderIndependently()
    {
        await RunStaAsync(() =>
        {
            var (_, overview) = CreateOverviewSurface();
            Assert.Equal(0, Grid.GetRow(overview.LoadButton));
            Assert.Equal(1, Grid.GetRowSpan(overview.LoadButton));
            Assert.Equal(240, overview.ModelCombo.Width);
            Assert.Equal(220, overview.LaunchProfileCombo.Width);
            Assert.Equal(Grid.GetRow(overview.ModelCombo), Grid.GetRow(overview.LaunchProfileCombo));
            Assert.Equal(Grid.GetRow(overview.ModelCombo), Grid.GetRow(overview.LoadButton));
            Assert.True(Grid.GetColumn(overview.LoadButton) > Grid.GetColumn(overview.LaunchProfileCombo));
            Assert.InRange(overview.LoadButton.ActualHeight, 28, 36);

            var overviewState = new LocalLlmConsole.OverviewPageState();
            overviewState.Apply(overview);
            overviewState.SetModelActionsEnabled(true, true, true, false);
            Assert.Equal(Visibility.Visible, overview.LoadButton.Visibility);
            Assert.Equal("Loaded", overview.LoadButton.Content);
            Assert.False(overview.LoadButton.IsEnabled);
            overviewState.SetModelActionsEnabled(true, true, false, false);
            Assert.Equal("Load", overview.LoadButton.Content);
            Assert.True(overview.LoadButton.IsEnabled);
            overviewState.SetModelActionsEnabled(true, true, false, true);
            Assert.False(overview.LoadButton.IsEnabled);
            Assert.Equal("The model file is missing. Restore it or remove the catalog entry before loading.", overview.LoadButton.ToolTip);
            Assert.True(ToolTipService.GetShowOnDisabled(overview.LoadButton));

            var launchProfileText = VisualDescendants<TextBlock>(overview.LaunchProfileCombo)
                .Select(text => text.Text)
                .ToArray();
            Assert.Contains("Default", launchProfileText);
            Assert.DoesNotContain(launchProfileText, text => text.Contains(nameof(OverviewLaunchProfileChoice), StringComparison.Ordinal));
            Assert.Equal(8, overview.LoadedSessionsGrid.Columns.Count);
            var sessionRow = Assert.Single(overview.LoadedSessionsGrid.Items.Cast<object>());
            Assert.NotNull(sessionRow);
            Assert.IsType<DataGridTemplateColumn>(overview.LoadedSessionsGrid.Columns[4]);
            Assert.Contains("Double-click", overview.LoadedSessionsGrid.ToolTip?.ToString(), StringComparison.Ordinal);
            Assert.False(overview.RuntimeLogBox.IsUndoEnabled);
            var unloadAction = Assert.Single(
                VisualDescendants<Button>(overview.LoadedSessionsGrid),
                button => Equals(button.Content, "Unload"));
            Assert.Equal("Unload", AutomationProperties.GetName(unloadAction));
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetHelpText(unloadAction)));

            var runtimeDashboardState = new LocalLlmConsole.RuntimeDashboardPageState();
            runtimeDashboardState.Apply(overview);
            var logTextChanges = 0;
            overview.RuntimeLogBox.TextChanged += (_, _) => logTextChanges++;
            var firstLog = string.Join(Environment.NewLine, Enumerable.Range(0, 200).Select(index => $"line {index}"));
            runtimeDashboardState.SetRuntimeLogText(firstLog, followTail: true);
            overview.Root.UpdateLayout();
            var logScrollViewer = Assert.Single(VisualDescendants<ScrollViewer>(overview.RuntimeLogBox));
            Assert.Equal(logScrollViewer.ScrollableHeight, logScrollViewer.VerticalOffset, precision: 1);

            runtimeDashboardState.SetRuntimeLogText(firstLog, followTail: true);
            Assert.Equal(1, logTextChanges);
            logScrollViewer.ScrollToVerticalOffset(0);
            var secondLog = firstLog + Environment.NewLine + "new tail line";
            runtimeDashboardState.SetRuntimeLogText(secondLog, followTail: true);
            Assert.Equal(0, logScrollViewer.VerticalOffset, precision: 1);
            Assert.True(logScrollViewer.ScrollableHeight > 0);
            logScrollViewer.ScrollToEnd();
            runtimeDashboardState.SetRuntimeLogText(secondLog + Environment.NewLine + "another tail line", followTail: true);
            Assert.Equal(logScrollViewer.ScrollableHeight, logScrollViewer.VerticalOffset, precision: 1);
        });
    }
}
