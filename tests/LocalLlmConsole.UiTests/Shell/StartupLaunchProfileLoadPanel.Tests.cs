using System.Windows;
using System.Windows.Controls;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.UiTests;

public sealed class StartupLaunchProfileLoadPanelTests : WpfUiTestBase
{
    [Fact]
    public async Task StartupLoadsShowPerProfileProgressAndCancelSkipsQueuedProfiles()
    {
        await RunStaAsync(async () =>
        {
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"startup-loads-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();
            var service = await ConfigureStartupProfilesAsync(store, "Alpha", "Beta");
            var panel = new StartupLaunchProfileLoadPanel();
            var firstLoad = new TaskCompletionSource<LoadedModelSessionSnapshot>();
            var loadStarted = new TaskCompletionSource();
            var loadTokens = new List<CancellationToken>();
            var attempted = new List<string>();

            var running = panel.RunAsync(service.LoadConfiguredAsync, new StartupLaunchProfileLoadActions(
                (model, _, token) =>
                {
                    attempted.Add(model.Name);
                    loadTokens.Add(token);
                    loadStarted.TrySetResult();
                    return firstLoad.Task;
                },
                (_, _) => false,
                _ => { }));
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.True(panel.IsLoading);
            Assert.Equal([StartupLaunchProfileLoadState.Loading, StartupLaunchProfileLoadState.Pending], panel.States);
            var texts = VisualText(panel);
            Assert.Contains(texts, text => text.Contains("Alpha", StringComparison.Ordinal) && text.Contains("Loading", StringComparison.Ordinal));
            Assert.Contains(texts, text => text.Contains("Beta", StringComparison.Ordinal) && text.Contains("Waiting", StringComparison.Ordinal));
            var cancel = Assert.Single(VisualDescendants<Button>(panel));
            Assert.Equal("Cancel", cancel.Content);

            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(panel.IsCancelling);
            Assert.False(cancel.IsEnabled);
            Assert.False(loadTokens[0].IsCancellationRequested);

            firstLoad.SetResult(null!);
            var result = await running;

            Assert.NotNull(result);
            Assert.Equal(["Alpha"], attempted);
            Assert.True(result.Cancelled);
            Assert.Equal([StartupLaunchProfileLoadState.Loaded, StartupLaunchProfileLoadState.Skipped], panel.States);
            Assert.Equal(Visibility.Visible, panel.Visibility);
            Assert.Contains(result.StatusMessage, VisualText(panel));
            Assert.Equal("Close", cancel.Content);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
        });
    }

    [Fact]
    public async Task SuccessfulStartupLoadsHideThePanelAndAbortCancelsTheInFlightLoad()
    {
        await RunStaAsync(async () =>
        {
            await using var store = new StateStore(Path.Combine(TestWorkspace, $"startup-loads-{Guid.NewGuid():N}.db"));
            await store.InitializeAsync();
            var service = await ConfigureStartupProfilesAsync(store, "Alpha");
            var panel = new StartupLaunchProfileLoadPanel();
            var actions = new StartupLaunchProfileLoadActions(
                (_, _, _) => Task.FromResult<LoadedModelSessionSnapshot>(null!),
                (_, _) => false,
                _ => { });

            var completed = await panel.RunAsync(service.LoadConfiguredAsync, actions);
            Assert.Equal(1, completed!.LoadedCount);
            Assert.Equal(Visibility.Collapsed, panel.Visibility);
            Assert.False(panel.IsLoading);

            var loadStarted = new TaskCompletionSource();
            var aborted = panel.RunAsync(service.LoadConfiguredAsync, actions with
            {
                LoadAsync = async (_, _, token) =>
                {
                    loadStarted.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                    return null!;
                }
            });
            await loadStarted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            panel.Abort();
            var result = await aborted.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(result!.Cancelled);
            Assert.Empty(result.Failures);
            Assert.Equal([StartupLaunchProfileLoadState.Skipped], panel.States);
        });
    }

    private static async Task<StartupLaunchProfileApplicationService> ConfigureStartupProfilesAsync(
        StateStore store,
        params string[] modelNames)
    {
        var now = DateTimeOffset.UtcNow;
        var defaults = AppSettings.CreateDefault(TestWorkspace);
        var service = new StartupLaunchProfileApplicationService(store);
        for (var index = 0; index < modelNames.Length; index++)
        {
            var name = modelNames[index];
            var model = new ModelRecord($"model-{name}", name, Path.Combine(TestWorkspace, $"{name}.gguf"), OwnershipKind.External, "{}", now);
            var profile = new NamedModelLaunchProfile(
                $"profile-{name}", model.Id, "Default", ModelLaunchSettings.FromAppSettings(defaults with { Port = 8091 + index }), now, true);
            await store.UpsertModelAsync(model);
            await store.SaveNamedModelLaunchProfileAsync(profile);
            await service.SetLoadOnStartupAsync(profile.Id, true);
        }

        return service;
    }

    private static string[] VisualText(DependencyObject root)
        => LogicalText(root).ToArray();

    private static IEnumerable<string> LogicalText(DependencyObject root)
    {
        if (root is TextBlock { Text: { Length: > 0 } text })
            yield return text;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var nested in LogicalText(child))
                yield return nested;
    }
}
