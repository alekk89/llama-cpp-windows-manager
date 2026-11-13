using System.Collections.Concurrent;
using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

/// <summary>
/// Reproduces the UI-thread conditions of an unexpected WSL exit: the caller owns a
/// single-threaded SynchronizationContext (like the WPF dispatcher) and the WSL stop
/// command completes asynchronously, posting its continuation back to that context.
/// </summary>
public sealed class RuntimeSessionDispatcherSafetyTests : ManagerRegressionTestBase
{
    private static readonly TimeSpan DeadlockTimeout = TimeSpan.FromSeconds(20);

    [Fact]
    public void ReconcilingAnExitedWslSessionDoesNotBlockTheDispatcher()
    {
        var root = CreateTempRoot();
        var runner = new ContextPostingProcessRunner();
        // A deadlocked dispatcher thread keeps holding the manager state lock, so the manager is
        // disposed only after the dispatcher completed; a regression then fails instead of hanging.
        var manager = new LoadedModelSessionManager(() => new LlamaProcessSupervisor(
            new WslRuntimeStopService(runner, () => "wsl.exe"),
            new NativeRuntimeStopService()));
        var supervisor = AttachExitedWslSession(manager, root);

        var outcome = RunOnDispatcherThread(async () =>
        {
            var result = await new RuntimeSessionReconciler().ReconcileAsync(
                manager,
                _ => Task.FromResult(true),
                _ => Task.FromResult(true));
            return result.RemovedSessionCount;
        });

        Assert.True(outcome.Completed, "Reconciling an exited WSL session deadlocked the dispatcher thread.");
        using var completedManager = manager;
        Assert.Null(outcome.Error);
        Assert.Equal(1, outcome.Value);
        Assert.Empty(manager.Snapshots());
        Assert.Equal(1, runner.Calls);
        Assert.Equal(LlamaRuntimeState.Stopped, supervisor.State);
        var failed = Assert.Single(manager.OverviewSnapshots());
        Assert.Equal(LoadedModelSessionStatus.Failed, failed.Status);
    }

    [Fact]
    public void SynchronousSupervisorDisposeDoesNotNeedTheCallersDispatcher()
    {
        var root = CreateTempRoot();
        var runner = new ContextPostingProcessRunner();
        var supervisor = new LlamaProcessSupervisor(
            new WslRuntimeStopService(runner, () => "wsl.exe"),
            new NativeRuntimeStopService());
        supervisor.AttachExisting(WslRuntime(root), "model-a", WslSettings(root), Path.Combine(root, "a.log"), LlamaRuntimeState.Loaded, "marker-a");

        var outcome = RunOnDispatcherThread(() =>
        {
            supervisor.Dispose();
            return Task.FromResult(0);
        });

        Assert.True(outcome.Completed, "Synchronous supervisor disposal deadlocked the dispatcher thread.");
        Assert.Null(outcome.Error);
        Assert.Equal(1, runner.Calls);
        Assert.False(supervisor.IsRunning);
    }

    private static LlamaProcessSupervisor AttachExitedWslSession(LoadedModelSessionManager manager, string root)
    {
        var model = new ModelRecord("model-a", "Model A", Path.Combine(root, "a.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        manager.AttachExisting(WslRuntime(root), model, WslSettings(root), Path.Combine(root, "a.log"), LlamaRuntimeState.Loaded, "marker-a", "session-a", DateTimeOffset.UtcNow);
        var supervisor = manager.ActiveSupervisor;
        supervisor.MarkAttachedHostExited(1);
        return supervisor;
    }

    private static RuntimeRecord WslRuntime(string root)
        => new("runtime-wsl", "llama.cpp WSL", RuntimeMode.Wsl, RuntimeBackend.Cpu, Path.Combine(root, "llama-server"), "{}", DateTimeOffset.UtcNow);

    private static AppSettings WslSettings(string root)
        => AppSettings.CreateDefault(root) with { WslDistro = "Ubuntu-24.04", Port = 8081 };

    private sealed record DispatcherOutcome(bool Completed, int Value, Exception? Error);

    /// <summary>
    /// Runs <paramref name="action"/> on a dedicated thread that owns a queue-based
    /// SynchronizationContext and pumps it until the returned task completes. A
    /// synchronous wait on that thread for posted work never completes.
    /// </summary>
    private static DispatcherOutcome RunOnDispatcherThread(Func<Task<int>> action)
    {
        var value = 0;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            var context = new QueueSynchronizationContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var task = action();
                while (!task.IsCompleted)
                    context.RunOne(TimeSpan.FromMilliseconds(50));
                value = task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        { IsBackground = true, Name = "Fake dispatcher" };
        thread.Start();
        var completed = thread.Join(DeadlockTimeout);
        return new DispatcherOutcome(completed, value, error);
    }

    private sealed class QueueSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = [];

        public override void Post(SendOrPostCallback d, object? state) => _queue.Add((d, state));

        public override void Send(SendOrPostCallback d, object? state)
            => throw new NotSupportedException("The fake dispatcher only accepts posted work.");

        public void RunOne(TimeSpan timeout)
        {
            if (_queue.TryTake(out var item, timeout))
                item.Callback(item.State);
        }
    }

    /// <summary>Completes like wsl.exe: asynchronously, resuming on the caller's context.</summary>
    private sealed class ContextPostingProcessRunner : IProcessRunner
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public async Task<ProcessRunResult> RunAsync(ProcessStartInfo psi, TimeSpan timeout, CancellationToken cancellationToken = default, string? standardInput = null)
        {
            Interlocked.Increment(ref _calls);
            await Task.Delay(10, cancellationToken);
            return new ProcessRunResult(0, "", "");
        }
    }
}
