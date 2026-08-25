using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using LocalLlmConsole.Localization;
using Microsoft.Data.Sqlite;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Theory]
    [InlineData(580, 6, 1)]
    [InlineData(620, 6, 2)]
    [InlineData(1024, 6, 2)]
    [InlineData(1139, 6, 2)]
    [InlineData(1140, 6, 3)]
    [InlineData(1600, 2, 2)]
    [InlineData(1600, 0, 1)]
    public void OverviewMetricLayoutProtectsReadableCardWidths(double width, int visibleCards, int expectedColumns)
        => Assert.Equal(expectedColumns, OverviewResponsiveLayout.MetricColumnCount(width, visibleCards));

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
    public void UiRowReconciliationPreservesIdentityOrderAndBindingNotifications()
    {
        var first = new UiRow { C1 = "first", Data = new JsonObject { ["Id"] = "1" } };
        var second = new UiRow { C1 = "second", Data = new JsonObject { ["Id"] = "2" } };
        var rows = new System.Collections.ObjectModel.ObservableCollection<UiRow>([first, second]);
        var changes = new List<string?>();
        second.PropertyChanged += (_, args) => changes.Add(args.PropertyName);

        var changed = UiRowCollectionUpdater.Reconcile(
            rows,
            [
                new UiRow { C1 = "second updated", Data = new JsonObject { ["Id"] = "2" } },
                new UiRow { C1 = "first", Data = new JsonObject { ["Id"] = "1" } }
            ],
            row => row.Data["Id"]?.ToString() ?? "");

        Assert.True(changed);
        Assert.Same(second, rows[0]);
        Assert.Same(first, rows[1]);
        Assert.Equal("second updated", rows[0].C1);
        Assert.Contains(nameof(UiRow.C1), changes);
        Assert.False(UiRowCollectionUpdater.Reconcile(
            rows,
            [
                new UiRow { C1 = "second updated", Data = new JsonObject { ["Id"] = "2" } },
                new UiRow { C1 = "first", Data = new JsonObject { ["Id"] = "1" } }
            ],
            row => row.Data["Id"]?.ToString() ?? ""));
    }

    [Fact]
    public void CurrentActionIsPinnedBelowHelpInItsOwnSidebarRow()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var helpIndex = xaml.IndexOf("x:Name=\"HelpNavButton\"", StringComparison.Ordinal);
        var currentActionIndex = xaml.IndexOf("x:Name=\"CurrentStatusLabel\"", StringComparison.Ordinal);

        Assert.True(helpIndex >= 0);
        Assert.True(currentActionIndex >= 0);
        Assert.True(currentActionIndex < helpIndex);
        Assert.Contains("<Border Grid.Row=\"3\"", xaml, StringComparison.Ordinal);
        Assert.Contains("<StackPanel Grid.Row=\"2\">", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void SettingsPageDoesNotExposeCacheFolder()
    {
        var source = ReadMainWindowSources();

        Assert.DoesNotContain("\"Cache folder\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"cacheRoot\"", source, StringComparison.Ordinal);
    }


    [Fact]
    public void MainWindowHasVisibleAppStatusLine()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();

        Assert.Contains("x:Name=\"AppStatusText\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Status.CurrentActionLabel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("ServiceStatusText", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeStatusText", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeStatusText", source, StringComparison.Ordinal);
        Assert.Contains("AppStatusText.Text", source, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.Yield", source, StringComparison.Ordinal);
    }

    [Fact]
    public void CoreUiSurfacesExposeAutomationNamesHelpAndLiveStatus()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var localization = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Localization.cs"));
        var accessibility = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "UiAccessibility.cs"));
        var sections = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Common", "PageSectionFactory.cs"));

        Assert.Contains("AutomationProperties.LiveSetting=\"Polite\"", xaml, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(LanguageCombo", localization, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetName(AppStatusText", localization, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHelpText(button", accessibility, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.NameProperty", sections, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.HelpTextProperty", sections, StringComparison.Ordinal);
        Assert.Contains("AutomationProperties.SetHeadingLevel", sections, StringComparison.Ordinal);
    }

    [Fact]
    public void MainWindowCancelsClosingSynchronouslyBeforeAsyncCleanup()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml.cs"));
        var handlerIndex = source.IndexOf("private async void Window_Closing", StringComparison.Ordinal);
        var cancelIndex = source.IndexOf("e.Cancel = true;", handlerIndex, StringComparison.Ordinal);
        var shutdownIndex = source.IndexOf("BeginShutdownAsync", handlerIndex, StringComparison.Ordinal);

        Assert.True(handlerIndex >= 0);
        Assert.True(cancelIndex > handlerIndex);
        Assert.True(shutdownIndex > cancelIndex);
    }


    [Fact]
    public void MainWindowUsesLlamaCppWindowsManagerBrandingAndIcon()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.xaml"));
        var source = ReadMainWindowSources();
        var project = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "LocalLlmConsole.App.csproj"));
        var iconPath = FindRepositoryFile("src", "LocalLlmConsole.App", "Assets", "AppIcon.ico");

        Assert.Contains("Title=\"llama.cpp Windows Manager v2.4.0\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Text=\"v2.4.0\"", xaml, StringComparison.Ordinal);

        Assert.Contains("AppVersionLabel = \"v2.4.0\"", source, StringComparison.Ordinal);
        Assert.Contains("<AssemblyName>LlamaCppWindowsManager</AssemblyName>", project, StringComparison.Ordinal);
        Assert.Contains("<ApplicationIcon>Assets\\AppIcon.ico</ApplicationIcon>", project, StringComparison.Ordinal);
        Assert.True(new FileInfo(iconPath).Length > 1024);
    }



    [Fact]
    public void OverviewLoadedSessionRowsSelectModelStatus()
    {
        var source = ReadMainWindowSources()
            + File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewSelectionController.cs"));
        var overviewFactory = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewPageFactory.cs"));
        var loadedSessionSelection = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "OverviewLoadedSessionSelectionApplicationService.cs"));

        Assert.Contains("loadedSessionsGrid.SelectionChanged", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("request.Actions.SelectLoadedSessionRowAsync", overviewFactory, StringComparison.Ordinal);
        Assert.Contains("SelectLoadedSessionRowAsync", source, StringComparison.Ordinal);
        Assert.Contains("_page.SelectedLoadedSessionRow", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.OverviewLoadedSessionSelectionApplication.SelectAsync", source, StringComparison.Ordinal);
        Assert.Contains("new OverviewLoadedSessionSelectionApplicationActions(", source, StringComparison.Ordinal);
        Assert.Contains("_page.SelectModelId", source, StringComparison.Ordinal);
        Assert.Contains("_runtime.RuntimeSessions.SelectModel", source, StringComparison.Ordinal);
        Assert.Contains("Selected session is no longer loaded.", loadedSessionSelection, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected session is no longer loaded.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Selected loaded model session.", source, StringComparison.Ordinal);
    }


    [Fact]
    public void SelectionReentrancyCoordinatorOwnsSelectionSuppression()
    {
        var coordinator = new SelectionReentrancyCoordinator();
        var source = ReadMainWindowSources()
            + File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Ui", "Pages", "Overview", "OverviewSelectionController.cs"));

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
        Assert.Contains("_coreServices.Ui.SelectionReentrancy.TryBeginModelGridSelection()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Ui.SelectionReentrancy.TryBeginLoadedSessionSelection()", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.SelectionReentrancy.SuppressLoadedSessionSelection()", source, StringComparison.Ordinal);
        Assert.Contains("_selection.IsLoadedSessionSelectionChanging", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectingModelGridRow", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_selectingLoadedSessionRow", source, StringComparison.Ordinal);
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
