using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ReadinessPollingTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task CancellationDuringThePollingDelayNeverProbesOrMarksTheSession()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var service = new RuntimeReadinessWorkflowService(async (interval, token) =>
        {
            Assert.Equal(TimeSpan.FromSeconds(2), interval);
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        var pending = service.WaitUntilReadyAsync(new RuntimeReadinessWorkflowRequest(
            "session", AppSettings.CreateDefault(CreateTempRoot()),
            _ => throw new InvalidOperationException("Must not inspect after cancellation."),
            (_, _) => throw new InvalidOperationException("Must not probe after cancellation."),
            _ => throw new InvalidOperationException("Must not mark loaded after cancellation.")), cancellation.Token);

        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
    }

    [Fact]
    public async Task UnavailableAuthenticationRetriesBeforeTheSessionCanBecomeLoaded()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var session = RuntimeSession(root, settings, LoadedModelSessionStatus.Loading, true);
        var events = new List<string>();
        var checks = 0;
        var service = new RuntimeReadinessWorkflowService((interval, token) =>
        {
            token.ThrowIfCancellationRequested();
            Assert.Equal(TimeSpan.FromSeconds(7), interval);
            events.Add("delay");
            return Task.CompletedTask;
        });

        var result = await service.WaitUntilReadyAsync(new RuntimeReadinessWorkflowRequest(
            session.ModelId, settings, _ => session,
            (_, _) => { events.Add("probe"); return Task.FromResult(true); },
            _ => { events.Add("mark"); return true; }, TimeSpan.FromSeconds(7),
            (_, _) =>
            {
                events.Add("auth");
                return Task.FromResult(new RuntimeAuthenticationProbeResult(
                    ++checks == 1 ? RuntimeAuthenticationProbeStatus.Unavailable : RuntimeAuthenticationProbeStatus.Verified, "probe"));
            }), TestContext.Current.CancellationToken);

        Assert.Equal(RuntimeReadinessStatus.Loaded, result.Status);
        Assert.Equal(["delay", "probe", "auth", "delay", "probe", "auth", "mark"], events);
    }
}
