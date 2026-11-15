using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.UiTests;

public sealed class RefusedActionAndSelectionFeedbackTests : WpfUiTestBase
{
    [Fact]
    public async Task RefusedBusyActionShowsAVisibleNoticeOutsideTheDisabledPage()
    {
        await RunStaAsync(() =>
        {
            var window = new LocalLlmConsole.MainWindow();
            DetachLoadedStartup(window);
            var tryBeginUiBusy = typeof(LocalLlmConsole.MainWindow).GetMethod("TryBeginUiBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var tryBeginResponsive = typeof(LocalLlmConsole.MainWindow).GetMethod("TryBeginResponsiveActivity", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var endUiBusy = typeof(LocalLlmConsole.MainWindow).GetMethod("EndUiBusy", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                var notice = Assert.IsType<ShellNoticeBar>(window.FindName("ShellNotice"));
                var pageHost = Assert.IsType<ContentControl>(window.FindName("PageHost"));
                Assert.False(notice.IsShowing);

                Assert.True((bool)tryBeginUiBusy.Invoke(window, ["Installing runtime..."])!);
                Assert.False(notice.IsShowing);
                Assert.False(pageHost.IsEnabled);

                Assert.False((bool)tryBeginResponsive.Invoke(window, ["Refreshing models..."])!);
                Assert.True(notice.IsShowing);
                Assert.Equal(LocalLlmConsole.Localization.Loc.T("Status.PleaseWaitFor", "Installing runtime..."), notice.Message);
                Assert.False(IsDescendantOf(notice, pageHost));
                Assert.True(notice.IsEnabled);
            }
            finally
            {
                endUiBusy.Invoke(window, null);
                typeof(LocalLlmConsole.MainWindow)
                    .GetMethod("DisposeTrayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(window, null);
            }
        });
    }

    [Fact]
    public async Task ShellNoticeCanBeClosedAndHidesItselfAfterItsDuration()
    {
        await RunStaAsync(async () =>
        {
            var notice = new ShellNoticeBar(TimeSpan.FromMilliseconds(50));
            notice.Show("Please wait: Installing runtime...");
            Assert.True(notice.IsShowing);
            var close = Assert.Single(VisualDescendants<Button>(notice));
            Assert.Equal("Close", System.Windows.Automation.AutomationProperties.GetName(close));
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(notice.IsShowing);

            var hidden = new TaskCompletionSource();
            DependencyPropertyDescriptor.FromProperty(UIElement.VisibilityProperty, typeof(ShellNoticeBar))
                .AddValueChanged(notice, (_, _) =>
                {
                    if (!notice.IsShowing) hidden.TrySetResult();
                });
            notice.Show("Please wait: Installing runtime...");
            Assert.True(notice.IsShowing);
            await hidden.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(notice.IsShowing);
        });
    }

    [Fact]
    public async Task OverviewSelectionChangedDuringARefreshIsKept()
    {
        await RunStaAsync(async () =>
        {
            var (viewModel, page) = CreateOverview();
            var alpha = Model("alpha");
            var beta = Model("beta");
            var gamma = Model("gamma");
            viewModel.ReplaceModels([alpha, beta, gamma]);
            page.SelectModelChoice(alpha.Id, viewModel.ModelChoices);
            var refresher = new OverviewModelChoiceRefresher(viewModel, page);
            var load = new TaskCompletionSource<OverviewModelChoiceData>();

            var refreshing = refresher.RefreshAsync(() => load.Task, () => null);
            page.SelectModelId(beta.Id);
            load.SetResult(Data(alpha, beta, gamma));
            var fallback = await refreshing;

            Assert.Null(fallback);
            Assert.Equal(beta.Id, page.SelectedModel(viewModel.ModelChoices)?.Id);
        });
    }

    [Fact]
    public async Task OverviewSelectionFallbackIsReportedWhenTheSelectedModelDisappears()
    {
        await RunStaAsync(async () =>
        {
            var (viewModel, page) = CreateOverview();
            var alpha = Model("alpha");
            var beta = Model("beta");
            viewModel.ReplaceModels([alpha, beta]);
            page.SelectModelChoice(beta.Id, viewModel.ModelChoices);
            var refresher = new OverviewModelChoiceRefresher(viewModel, page);

            var fallback = await refresher.RefreshAsync(() => Task.FromResult(Data(alpha)), () => null);

            Assert.NotNull(fallback);
            Assert.Equal(beta.Id, fallback.Previous.Id);
            Assert.Equal(alpha.Id, fallback.Fallback?.Id);
            Assert.Equal(alpha.Id, page.SelectedModel(viewModel.ModelChoices)?.Id);
            Assert.Equal(LocalLlmConsole.Localization.Loc.T("Overview.SelectionFallback", "beta", "alpha"), fallback.Message);

            var removed = await refresher.RefreshAsync(() => Task.FromResult(Data()), () => null);
            Assert.Null(removed?.Fallback);
            Assert.Equal(LocalLlmConsole.Localization.Loc.T("Overview.SelectionRemoved", "alpha"), removed?.Message);

            var unchanged = await refresher.RefreshAsync(() => Task.FromResult(Data(alpha)), () => alpha.Id);
            Assert.Null(unchanged);
            Assert.Equal(alpha.Id, page.SelectedModel(viewModel.ModelChoices)?.Id);
        });
    }

    private static (OverviewPageViewModel ViewModel, OverviewPageState Page) CreateOverview()
    {
        var viewModel = new MainWindowViewModel();
        var overview = OverviewPageFactory.Create(new OverviewPageRequest(
            viewModel,
            new OverviewPageActions(
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
        var page = new OverviewPageState();
        page.Apply(overview);
        return (viewModel.Overview, page);
    }

    private static ModelRecord Model(string name)
        => new($"model-{name}", name, Path.Combine(TestWorkspace, $"{name}.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);

    private static OverviewModelChoiceData Data(params ModelRecord[] models)
        => new(models, new ModelGroupSnapshot([], new Dictionary<string, ModelGroupAssignment>()), [], new Dictionary<string, string>());

    private static bool IsDescendantOf(DependencyObject element, DependencyObject ancestor)
    {
        for (var current = element; current is not null; current = LogicalTreeHelper.GetParent(current))
        {
            if (ReferenceEquals(current, ancestor)) return true;
        }

        return false;
    }
}
