using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


public sealed class UiCoordinationTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task DebouncedAsyncActionRunsOnlyLatestScheduledAction()
    {
        using var debounce = new DebouncedAsyncAction(TimeSpan.FromMilliseconds(40));
        var observed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var background = new List<Task>();
        void RunObserved(Func<Task> action)
            => background.Add(Task.Run(async () =>
            {
                try { await action(); }
                catch (OperationCanceledException) { }
            }));

        debounce.Schedule(
            _ =>
            {
                observed.Enqueue("first");
                return Task.CompletedTask;
            },
            RunObserved);
        debounce.Schedule(
            _ =>
            {
                observed.Enqueue("second");
                return Task.CompletedTask;
            },
            RunObserved);

        await Task.Delay(120, TestContext.Current.CancellationToken);
        await Task.WhenAll(background);

        debounce.Schedule(
            _ =>
            {
                observed.Enqueue("cancelled");
                return Task.CompletedTask;
            },
            RunObserved);
        debounce.Cancel();
        await Task.Delay(80, TestContext.Current.CancellationToken);
        await Task.WhenAll(background);

        Assert.Equal(["second"], observed.ToArray());
    }


    [Fact]
    public async Task OverviewModelSelectionControllerCancelsSupersededSelection()
    {
        var firstStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new List<int>();
        var invocation = 0;
        var actionUpdates = 0;
        var suppressLoadedSelection = true;
        var loadedSelections = 0;
        var controller = new OverviewPageActionController(new OverviewPageActionControllerActions(
            async cancellationToken =>
            {
                var current = Interlocked.Increment(ref invocation);
                if (current == 1)
                {
                    firstStarted.SetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }

                completed.Add(current);
            },
            () => Task.CompletedTask,
            () => actionUpdates++,
            () => Task.CompletedTask,
            () => suppressLoadedSelection,
            _ => { loadedSelections++; return Task.CompletedTask; },
            () => Task.CompletedTask,
            _ => null,
            _ => Task.CompletedTask,
            _ => "",
            _ => Task.CompletedTask,
            _ => Task.CompletedTask,
            async action =>
            {
                try { await action(); }
                catch (OperationCanceledException) { }
            }));
        var actions = controller.Build();

        var first = actions.SelectModelSessionAsync();
        await firstStarted.Task.WaitAsync(TestContext.Current.CancellationToken);
        var second = actions.SelectModelSessionAsync();
        await Task.WhenAll(first, second).WaitAsync(TestContext.Current.CancellationToken);

        Assert.Equal([2], completed);
        Assert.Equal(1, actionUpdates);

        await actions.SelectLoadedSessionRowAsync();
        Assert.Equal(0, loadedSelections);
        suppressLoadedSelection = false;
        await actions.SelectLoadedSessionRowAsync();
        Assert.Equal(1, loadedSelections);
    }


    [Fact]
    public void DownloadHistoryPageStateOwnsModeAndTimerRefreshGate()
    {
        var state = new DownloadHistoryPageState();

        Assert.False(state.IsShowingHistory);
        Assert.False(state.TryBeginTimerRefresh());

        state.ShowHistory();

        Assert.True(state.IsShowingHistory);
        Assert.True(state.TryBeginTimerRefresh());
        Assert.False(state.TryBeginTimerRefresh());

        state.CompleteTimerRefresh();

        Assert.True(state.TryBeginTimerRefresh());

        state.ShowSearch();

        Assert.False(state.IsShowingHistory);
        Assert.False(state.TryBeginTimerRefresh());
    }


    [Fact]
    public void RefreshGatePreventsOverlappingRefreshes()
    {
        var gate = new RefreshGate();

        Assert.True(gate.TryBegin());
        Assert.False(gate.TryBegin());

        gate.Complete();

        Assert.True(gate.TryBegin());
    }


    [Fact]
    public async Task UiAsyncRefreshTimerControllerOwnsAsyncTickErrorHandling()
    {
        var timerFactory = new ManualUiTimerFactory();
        var controller = new UiAsyncRefreshTimerController(timerFactory);
        var observed = new List<string>();
        var errors = new List<string>();

        controller.Start(
            TimeSpan.FromSeconds(1.5),
            () =>
            {
                observed.Add("tick");
                return Task.CompletedTask;
            },
            ex => errors.Add(ex.Message));

        Assert.True(controller.IsRunning);
        Assert.Single(timerFactory.Timers);
        Assert.Equal(TimeSpan.FromSeconds(1.5), timerFactory.Timers[0].Interval);
        Assert.True(timerFactory.Timers[0].Started);

        await timerFactory.Timers[0].FireAsync();
        Assert.Equal(["tick"], observed);
        Assert.Empty(errors);

        controller.Start(
            TimeSpan.FromSeconds(1),
            () => throw new InvalidOperationException("refresh failed"),
            ex => errors.Add(ex.Message));

        Assert.False(timerFactory.Timers[0].Started);
        Assert.Equal(2, timerFactory.Timers.Count);
        await timerFactory.Timers[1].FireAsync();
        Assert.Equal(["refresh failed"], errors);

        controller.Stop();
        Assert.False(controller.IsRunning);
        Assert.False(timerFactory.Timers[1].Started);
    }

    [Fact]
    public async Task UiAsyncRefreshTimerControllerCoalescesOverlappingTicks()
    {
        var timerFactory = new ManualUiTimerFactory();
        var controller = new UiAsyncRefreshTimerController(timerFactory);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        controller.Start(
            TimeSpan.FromSeconds(1),
            async () =>
            {
                Interlocked.Increment(ref calls);
                await release.Task;
            },
            _ => { });

        timerFactory.Timers[0].Fire();
        timerFactory.Timers[0].Fire();
        Assert.Equal(1, calls);

        release.SetResult();
        await Task.Delay(10, TestContext.Current.CancellationToken);
        await timerFactory.Timers[0].FireAsync();
        Assert.Equal(2, calls);
    }



    [Fact]
    public async Task ModelDeletionApplicationServiceOwnsPromptsBlockingAndRefresh()
    {
        var root = CreateTempRoot();
        var modelsRoot = Path.Combine(root, "models");
        var baseModel = new ModelRecord(
            "base-model",
            "Base Model",
            Path.Combine(modelsRoot, "base", "model.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var appOwned = baseModel with
        {
            Id = "app-owned",
            Name = "Downloaded Model",
            Ownership = OwnershipKind.AppOwned
        };
        var alias = new ModelRecord(
            "variant-model",
            "Base Model 32K",
            baseModel.ModelPath,
            OwnershipKind.RegistryOnly,
            ModelAliasService.CreateMetadata(baseModel, [baseModel]),
            DateTimeOffset.UtcNow);
        var service = new ModelDeletionApplicationService();
        var calls = new List<string>();
        var loaded = false;
        var confirm = true;

        ModelDeletionApplicationActions Actions()
            => new(
                _ => loaded,
                confirmation =>
                {
                    calls.Add($"confirm:{confirmation.Title}:{confirmation.Message}");
                    return confirm;
                },
                async (message, action) =>
                {
                    calls.Add($"busy:{message}");
                    await action();
                },
                (model, rootPath) =>
                {
                    calls.Add($"delete:{model.Id}:{rootPath}");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("refresh-models");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("refresh-overview");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"));

        var ignored = await service.DeleteAsync(null, modelsRoot, Actions());

        loaded = true;
        var blocked = await service.DeleteAsync(baseModel, modelsRoot, Actions());

        loaded = false;
        confirm = false;
        var cancelled = await service.DeleteAsync(appOwned, modelsRoot, Actions());

        confirm = true;
        var deleted = await service.DeleteAsync(alias, modelsRoot, Actions());
        var externalConfirmation = ModelDeletionApplicationService.BuildConfirmation(baseModel);
        var appOwnedConfirmation = ModelDeletionApplicationService.BuildConfirmation(appOwned);
        var aliasConfirmation = ModelDeletionApplicationService.BuildConfirmation(alias);

        Assert.Equal(ModelDeletionApplicationOutcome.Ignored, ignored);
        Assert.Equal(ModelDeletionApplicationOutcome.BlockedLoaded, blocked);
        Assert.Equal(ModelDeletionApplicationOutcome.Cancelled, cancelled);
        Assert.Equal(ModelDeletionApplicationOutcome.Deleted, deleted);
        Assert.Contains("status:Unload the selected model before deleting it.", calls);
        Assert.Contains("remove the model registration only", externalConfirmation.Message, StringComparison.Ordinal);
        Assert.Contains("delete the downloaded model files", appOwnedConfirmation.Message, StringComparison.Ordinal);
        Assert.Contains("remove this saved model variant without deleting the GGUF file", aliasConfirmation.Message, StringComparison.Ordinal);
        Assert.Contains(calls, call => call.StartsWith("confirm:Remove model:", StringComparison.Ordinal)
            && call.Contains("delete the downloaded model files", StringComparison.Ordinal));
        Assert.Contains("busy:Removing model...", calls);
        Assert.Contains($"delete:{alias.Id}:{modelsRoot}", calls);
        Assert.True(calls.IndexOf($"delete:{alias.Id}:{modelsRoot}") < calls.IndexOf("refresh-models"));
        Assert.True(calls.IndexOf("refresh-models") < calls.IndexOf("refresh-overview"));
    }

    private static WindowsStartupRegistrationService DisabledStartupRegistration()
        => new(() => null, _ => { }, () => { }, () => "app.exe");

}
