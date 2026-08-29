using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class UiShellTests : ManagerRegressionTestBase
{
    [Fact]
    public void InitialWindowSizingFitsHighDpiWorkAreasWithoutDroppingBelowRequestedMinimumWhenSpaceAllows()
    {
        var constrained = WindowWorkAreaSizingService.Fit(1200, 780, 900, 600, 0, 0, 1056, 672);

        Assert.Equal(1024, constrained.Width);
        Assert.Equal(640, constrained.Height);
        Assert.Equal(900, constrained.MinimumWidth);
        Assert.Equal(600, constrained.MinimumHeight);
        Assert.Equal(16, constrained.Left);
        Assert.Equal(16, constrained.Top);

        var smaller = WindowWorkAreaSizingService.Fit(1200, 780, 900, 600, 0, 0, 820, 560);
        Assert.Equal(788, smaller.Width);
        Assert.Equal(528, smaller.Height);
        Assert.Equal(788, smaller.MinimumWidth);
        Assert.Equal(528, smaller.MinimumHeight);
    }

    [Fact]
    public void RuntimeMetricRowReconciliationPreservesIdentityAndBindingNotifications()
    {
        var viewModel = new RuntimeMetricsViewModel();
        viewModel.ReplaceSamples([new PrometheusSample("metric", "slot=1", 1, "1", "gauge", "first")]);
        var row = viewModel.Rows.Single();
        var changes = new List<string?>();
        row.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        viewModel.ReplaceSamples([new PrometheusSample("metric", "slot=1", 2, "2", "gauge", "updated")]);

        Assert.Same(row, viewModel.Rows.Single());
        Assert.Equal("2", row.Value);
        Assert.Contains(nameof(RuntimeMetricRow.Value), changes);
    }

    [Fact]
    public void SelectionReentrancyCoordinatorOwnsSelectionSuppression()
    {
        var coordinator = new SelectionReentrancyCoordinator();

        using (var modelSelection = coordinator.TryBeginModelGridSelection())
        {
            Assert.NotNull(modelSelection);
            Assert.True(coordinator.IsModelGridSelectionChanging);
            Assert.Null(coordinator.TryBeginModelGridSelection());
        }

        Assert.False(coordinator.IsModelGridSelectionChanging);

        using (var loadedSelection = coordinator.TryBeginLoadedSessionSelection())
        {
            Assert.NotNull(loadedSelection);
            Assert.True(coordinator.IsLoadedSessionSelectionChanging);
            using (coordinator.SuppressLoadedSessionSelection())
            {
                Assert.True(coordinator.IsLoadedSessionSelectionChanging);
                Assert.Null(coordinator.TryBeginLoadedSessionSelection());
            }

            Assert.True(coordinator.IsLoadedSessionSelectionChanging);
        }

        Assert.False(coordinator.IsLoadedSessionSelectionChanging);

        using (coordinator.SuppressLoadedSessionSelection())
        {
            Assert.True(coordinator.IsLoadedSessionSelectionChanging);
            Assert.Null(coordinator.TryBeginLoadedSessionSelection());
        }

        Assert.False(coordinator.IsLoadedSessionSelectionChanging);
    }

    [Fact]
    public void MainWindowUsesObservedBackgroundTasks()
    {
        var source = ReadMainWindowSources();

        Assert.DoesNotContain("_ = Refresh", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Monitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = CheckFor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Seed", source, StringComparison.Ordinal);
        Assert.Contains("RunBackground", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.App.BackgroundTasks.RunAsync(", source, StringComparison.Ordinal);
    }
}
