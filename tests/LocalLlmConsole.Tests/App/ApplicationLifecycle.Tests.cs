using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


public sealed class ApplicationLifecycleTests : ManagerRegressionTestBase
{
    [Fact]
    public void ShellIntegrationServiceOwnsProcessLaunchAndFolderCreation()
    {
        var started = new List<ProcessStartInfo>();
        var service = new ShellIntegrationService(started.Add);
        var root = CreateTempRoot();
        var folder = Path.Combine(root, "logs");
        var logPath = Path.Combine(root, "logs", "runtime.log");
        var url = "https://github.com/example/repo";

        service.OpenFolder(folder);
        File.WriteAllText(logPath, "runtime");
        service.OpenPath(logPath);
        service.OpenUrl(url);

        Assert.True(Directory.Exists(folder));
        Assert.Equal([Path.GetFullPath(folder), Path.GetFullPath(logPath), url], started.Select(process => process.FileName).ToArray());
        Assert.All(started, process => Assert.True(process.UseShellExecute));
        Assert.Throws<ArgumentException>(() => service.OpenUrl("relative/path"));
        Assert.Throws<ArgumentException>(() => service.OpenUrl("javascript:alert(1)"));
        Assert.Throws<FileNotFoundException>(() => service.OpenPath(Path.Combine(root, "missing.log")));
        Assert.Throws<ArgumentException>(() => service.OpenPath(url));
    }



    [Fact]
    public void ClipboardServiceOwnsClipboardSetTextAction()
    {
        var copied = new List<string>();
        var service = new ClipboardService(copied.Add);

        service.SetText("secret-key");

        Assert.Equal(["secret-key"], copied);
        Assert.Throws<ArgumentNullException>(() => service.SetText(null!));
    }


    [Fact]
    public void DialogServiceOwnsThemedMessageBoxBridge()
    {
        var calls = new List<string>();
        var service = new DialogService((owner, message, title, buttons, image) =>
        {
            Assert.Null(owner);
            calls.Add($"{title}:{message}:{buttons}:{image}");
            return buttons == MessageBoxButton.YesNo ? MessageBoxResult.Yes : MessageBoxResult.OK;
        });

        var confirmed = service.Confirm(null, "Proceed?", "Confirm", MessageBoxImage.Warning);
        service.Notify(null, "Done", "Info", MessageBoxImage.Information);
        var result = service.Show(null, "Plain", "Show", MessageBoxButton.OKCancel, MessageBoxImage.Error);

        Assert.True(confirmed);
        Assert.Equal(MessageBoxResult.OK, result);
        Assert.Equal(
            [
                "Confirm:Proceed?:YesNo:Exclamation",
                "Info:Done:OK:Asterisk",
                "Show:Plain:OKCancel:Hand"
            ],
            calls);
    }


    [Fact]
    public void SingleInstanceApplicationServiceOwnsLeaseLifecycle()
    {
        var nonOwnerLease = new FakeSingleInstanceLease(ownsInstance: false);
        var ownerLease = new FakeSingleInstanceLease(ownsInstance: true);
        var leases = new Queue<FakeSingleInstanceLease>([nonOwnerLease, ownerLease]);
        var acquiredNames = new List<string>();
        var service = new SingleInstanceApplicationService(name =>
        {
            acquiredNames.Add(name);
            return leases.Dequeue();
        });

        Assert.False(service.TryAcquire("Local\\app"));

        Assert.True(nonOwnerLease.Disposed);
        Assert.False(nonOwnerLease.Released);

        Assert.True(service.TryAcquire("Local\\app"));
        Assert.True(service.TryAcquire("Local\\other"));

        service.Dispose();
        service.Dispose();

        Assert.Equal(["Local\\app", "Local\\app"], acquiredNames);
        Assert.True(ownerLease.Released);
        Assert.True(ownerLease.Disposed);
        Assert.Throws<ArgumentException>(() => new SingleInstanceApplicationService(_ => throw new InvalidOperationException()).TryAcquire(""));
    }


    [Fact]
    public async Task DownloadCompletionApplicationServiceWaitsThenRefreshesOnUiThread()
    {
        var service = new DownloadCompletionApplicationService();
        var calls = new List<string>();

        Task AddAsync(string call)
        {
            calls.Add(call);
            return Task.CompletedTask;
        }

        await service.MonitorAsync(
            "job-1",
            new DownloadCompletionApplicationActions(
                (jobId, interval) => AddAsync($"wait:{jobId}:{interval.TotalMilliseconds:0}"),
                async action =>
                {
                    calls.Add("ui:begin");
                    await action();
                    calls.Add("ui:end");
                },
                () => AddAsync("scan-models"),
                () => AddAsync("refresh-models"),
                () => AddAsync("refresh-overview"),
                () => AddAsync("refresh-download-history"),
                () => AddAsync("refresh-install-state")));

        Assert.Equal([
            "wait:job-1:1500",
            "ui:begin",
            "scan-models",
            "refresh-models",
            "refresh-overview",
            "refresh-download-history",
            "refresh-install-state",
            "ui:end"
        ], calls);
    }


    [Fact]
    public async Task AppStartupApplicationServiceOwnsStateLoadedServicesAndLocalServiceStartup()
    {
        var root = CreateTempRoot();
        var factory = new AppServiceFactory(root);
        var infrastructure = factory.CreateMainWindowInfrastructureServices();
        using var sessions = infrastructure.Sessions;
        var processRunner = infrastructure.ProcessRunner;
        using var runtimePackageClient = infrastructure.RuntimePackageClient;
        using var runtimeProbeClient = infrastructure.RuntimeProbeClient;
        using var metricsClient = infrastructure.MetricsClient;
        var core = factory.CreateMainWindowCoreServices(infrastructure.CoreServiceRequest());
        var createdHosts = new List<FakeLocalAppServiceHost>();
        var calls = new List<string>();
        StateStore? appliedStore = null;
        AppSettings? appliedSettings = null;
        MainWindowLoadedServices? appliedLoaded = null;
        ILocalAppServiceHost? appliedLocal = null;
        AppStartupApplicationResult? result = null;

        try
        {
            result = await core.App.StartupApplication.StartAsync(
                new AppStartupApplicationRequest(
                    root,
                    factory.DatabasePath,
                    factory.CreateStateStore,
                    stateStore => factory.CreateMainWindowLoadedServices(infrastructure.LoadedServiceRequest(stateStore, core)),
                    (_, _, port) =>
                    {
                        var host = new FakeLocalAppServiceHost(port);
                        createdHosts.Add(host);
                        return host;
                    },
                    PreferredLocalServicePort: 8095,
                    MaxLocalServiceFallbackPort: 8095),
                new AppStartupApplicationActions(
                    stateStore =>
                    {
                        appliedStore = stateStore;
                        calls.Add("state");
                    },
                    settings =>
                    {
                        appliedSettings = settings;
                        calls.Add("settings");
                    },
                    loadedServices =>
                    {
                        appliedLoaded = loadedServices;
                        calls.Add("loaded");
                    },
                    localService =>
                    {
                        appliedLocal = localService;
                        calls.Add("local");
                    },
                    status => calls.Add($"status:{status}")),
                TestContext.Current.CancellationToken);

            Assert.Same(result.StateStore, appliedStore);
            Assert.Same(result.Settings, appliedSettings);
            Assert.Same(result.LoadedServices, appliedLoaded);
            Assert.False(result.LoadedServices.App.Benchmarks.IsValueCreated);
            Assert.Same(result.LocalService, appliedLocal);
            Assert.Equal(8095, result.LocalServicePort);
            Assert.Equal("", result.LocalServiceStatusMessage);
            Assert.Equal(["state", "settings", "loaded", "local"], calls);
            Assert.True(Directory.Exists(root));
            Assert.True(Directory.Exists(result.Settings.ModelsRoot));
            Assert.True(Directory.Exists(result.Settings.RuntimeRoot));
            Assert.True(Directory.Exists(result.Settings.CacheRoot));
            Assert.Single(createdHosts);
            Assert.True(createdHosts[0].Started);
            Assert.False(createdHosts[0].Disposed);
        }
        finally
        {
            if (result?.LocalService is not null)
                await result.LocalService.DisposeAsync();
            if (result?.StateStore is not null)
                await result.StateStore.DisposeAsync();
        }
    }


    [Fact]
    public async Task AppStartupBackgroundApplicationServiceSeedsSuggestedLaunchProfilesQuietly()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var service = new AppStartupBackgroundApplicationService();
        var observedTimeout = false;

        var seeded = await service.SeedSuggestedLaunchProfilesAsync(
            new AppStartupSuggestedLaunchProfileSeedRequest(
                settings,
                (receivedSettings, cancellationToken) =>
                {
                    observedTimeout = cancellationToken.CanBeCanceled;
                    Assert.Same(settings, receivedSettings);
                    return Task.FromResult(2);
                }),
            TestContext.Current.CancellationToken);
        var skipped = await service.SeedSuggestedLaunchProfilesAsync(
            new AppStartupSuggestedLaunchProfileSeedRequest(settings, null),
            TestContext.Current.CancellationToken);
        var failed = await service.SeedSuggestedLaunchProfilesAsync(
            new AppStartupSuggestedLaunchProfileSeedRequest(
                settings,
                (_, _) => throw new InvalidOperationException("Offline")),
            TestContext.Current.CancellationToken);

        Assert.True(observedTimeout);
        Assert.True(seeded.ShouldRefreshLaunchSettings);
        Assert.Equal(2, seeded.SeededCount);
        Assert.Equal("Applied Hugging Face suggested launch defaults for 2 models.", seeded.StatusMessage);
        Assert.False(skipped.ShouldRefreshLaunchSettings);
        Assert.Equal(0, skipped.SeededCount);
        Assert.False(failed.ShouldRefreshLaunchSettings);
        Assert.Equal(0, failed.SeededCount);
    }


    [Fact]
    public void AppShutdownDecisionServiceBuildsPromptsAndClosingStatus()
    {
        var service = new AppShutdownDecisionService();
        var shutdownState = new AppShutdownStateController();

        var idle = service.Build(runningModelSessions: 0, activeDownloads: 0);
        var downloadsOnly = service.Build(runningModelSessions: 0, activeDownloads: 1);
        var modelsAndDownloads = service.Build(runningModelSessions: 2, activeDownloads: 3);

        Assert.Empty(idle.Confirmations);
        Assert.Equal("Closing...", idle.ClosingStatus);
        var downloadPrompt = Assert.Single(downloadsOnly.Confirmations);
        Assert.Equal(AppShutdownConfirmationKind.ActiveDownloads, downloadPrompt.Kind);
        Assert.Equal("Downloads in progress", downloadPrompt.Title);
        Assert.Contains("1 model download is still running.", downloadPrompt.Message, StringComparison.Ordinal);
        Assert.Equal("Pausing active downloads and closing...", downloadsOnly.ClosingStatus);
        Assert.Equal(2, modelsAndDownloads.Confirmations.Count);
        Assert.Equal(AppShutdownConfirmationKind.RunningModels, modelsAndDownloads.Confirmations[0].Kind);
        Assert.Contains("2 model sessions are running.", modelsAndDownloads.Confirmations[0].Message, StringComparison.Ordinal);
        Assert.Equal(AppShutdownConfirmationKind.ActiveDownloads, modelsAndDownloads.Confirmations[1].Kind);
        Assert.Contains("3 model downloads are still running.", modelsAndDownloads.Confirmations[1].Message, StringComparison.Ordinal);
        Assert.Equal("Stopping runtimes and closing...", modelsAndDownloads.ClosingStatus);

        Assert.Equal(AppShutdownCloseAdmission.CancelAndStartCleanup, shutdownState.BeginClosing());
        Assert.True(shutdownState.ShutdownRequested);
        Assert.Equal(AppShutdownCloseAdmission.CancelAlreadyInProgress, shutdownState.BeginClosing());
        shutdownState.ResetRequest();
        Assert.False(shutdownState.ShutdownRequested);
        Assert.Equal(AppShutdownCloseAdmission.CancelAndStartCleanup, shutdownState.BeginClosing());
        shutdownState.MarkCleanupComplete();
        Assert.True(shutdownState.CleanupComplete);
        Assert.False(shutdownState.ShutdownRequested);
        Assert.Equal(AppShutdownCloseAdmission.AllowClose, shutdownState.BeginClosing());
    }


    [Fact]
    public async Task AppShutdownApplicationServiceOwnsAdmissionConfirmationsAndCleanupState()
    {
        var decisions = new AppShutdownDecisionService();
        var state = new AppShutdownStateController();
        var application = new AppShutdownApplicationService(decisions, state);
        var calls = new List<string>();

        var completed = await application.BeginShutdownAsync(
            new AppShutdownApplicationRequest(RunningModelSessions: 1, ActiveDownloads: 1),
            new AppShutdownApplicationActions(
                prompt =>
                {
                    calls.Add($"confirm:{prompt.Kind}");
                    return Task.FromResult(true);
                },
                () => calls.Add("disable"),
                status => calls.Add($"status:{status}"),
                () =>
                {
                    calls.Add("cleanup");
                    return Task.CompletedTask;
                }));

        Assert.Equal(AppShutdownApplicationOutcomeKind.CleanupCompleted, completed.Kind);
        Assert.True(completed.CancelClosingEvent);
        Assert.True(completed.RequestClose);
        Assert.True(state.CleanupComplete);
        Assert.Equal([
            "confirm:RunningModels",
            "confirm:ActiveDownloads",
            "disable",
            "status:Stopping runtimes and closing...",
            "cleanup"
        ], calls);

        var allowed = await application.BeginShutdownAsync(
            new AppShutdownApplicationRequest(RunningModelSessions: 0, ActiveDownloads: 0),
            new AppShutdownApplicationActions(
                _ => throw new InvalidOperationException("Already cleaned up."),
                () => throw new InvalidOperationException("Already cleaned up."),
                _ => throw new InvalidOperationException("Already cleaned up."),
                () => throw new InvalidOperationException("Already cleaned up.")));
        Assert.Equal(AppShutdownApplicationOutcomeKind.AllowClose, allowed.Kind);
        Assert.False(allowed.CancelClosingEvent);
        Assert.False(allowed.RequestClose);
    }


    [Fact]
    public async Task ConfirmedControlShutdownSkipsInteractivePrompts()
    {
        var application = new AppShutdownApplicationService(
            new AppShutdownDecisionService(),
            new AppShutdownStateController());
        var confirmations = 0;
        var cleaned = false;

        var result = await application.BeginShutdownAsync(
            new AppShutdownApplicationRequest(RunningModelSessions: 2, ActiveDownloads: 1, Confirmed: true),
            new AppShutdownApplicationActions(
                _ =>
                {
                    confirmations++;
                    return Task.FromResult(false);
                },
                () => { },
                _ => { },
                () =>
                {
                    cleaned = true;
                    return Task.CompletedTask;
                }));

        Assert.Equal(0, confirmations);
        Assert.True(cleaned);
        Assert.Equal(AppShutdownApplicationOutcomeKind.CleanupCompleted, result.Kind);
        Assert.True(result.RequestClose);
    }


    [Fact]
    public async Task AppShutdownApplicationServiceResetsRequestAfterCancelledPromptOrFailure()
    {
        var cancelledState = new AppShutdownStateController();
        var cancelled = new AppShutdownApplicationService(new AppShutdownDecisionService(), cancelledState);
        var cancelledOutcome = await cancelled.BeginShutdownAsync(
            new AppShutdownApplicationRequest(RunningModelSessions: 1, ActiveDownloads: 0),
            new AppShutdownApplicationActions(
                _ => Task.FromResult(false),
                () => throw new InvalidOperationException("Should not disable UI."),
                _ => throw new InvalidOperationException("Should not set status."),
                () => throw new InvalidOperationException("Should not clean up.")));

        Assert.Equal(AppShutdownApplicationOutcomeKind.CancelledByUser, cancelledOutcome.Kind);
        Assert.True(cancelledOutcome.CancelClosingEvent);
        Assert.False(cancelledOutcome.RequestClose);
        Assert.False(cancelledState.ShutdownRequested);

        var failingState = new AppShutdownStateController();
        var failing = new AppShutdownApplicationService(new AppShutdownDecisionService(), failingState);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failing.BeginShutdownAsync(
            new AppShutdownApplicationRequest(RunningModelSessions: 0, ActiveDownloads: 0),
            new AppShutdownApplicationActions(
                _ => Task.FromResult(true),
                () => { },
                _ => { },
                () => throw new InvalidOperationException("cleanup failed"))));
        Assert.False(failingState.ShutdownRequested);
    }


    [Fact]
    public async Task AppShutdownCleanupApplicationServiceRunsCleanupInShutdownOrder()
    {
        var service = new AppShutdownCleanupApplicationService();
        var calls = new List<string>();

        Task AddAsync(string call)
        {
            calls.Add(call);
            return Task.CompletedTask;
        }

        var result = await service.CleanupAsync(new AppShutdownCleanupActions(
            () => calls.Add("stop-download-history"),
            () => calls.Add("stop-runtime-dashboard"),
            () => calls.Add("stop-gpu-energy"),
            () => calls.Add("cancel-pending-ui-work"),
            () => calls.Add("stop-readiness"),
            () => calls.Add("dispose-tray"),
            () => AddAsync("pause-downloads"),
            () => AddAsync("dispose-benchmarks"),
            () => calls.Add("kill-processes"),
            () => AddAsync("cleanup-wsl-builds"),
            () => AddAsync("dispose-gateway"),
            () => AddAsync("dispose-local-service"),
            () => AddAsync("drain-background-tasks"),
            () => AddAsync("stop-runtime-sessions"),
            () => calls.Add("dispose-sessions"),
            () => calls.Add("dispose-huggingface"),
            () => calls.Add("dispose-app-updates"),
            () => calls.Add("dispose-runtime-package-client"),
            () => calls.Add("dispose-metrics-client"),
            () => calls.Add("dispose-runtime-probe-client"),
            () => calls.Add("clear-active-settings"),
            () => calls.Add("clear-active-session"),
            () => AddAsync("dispose-state-store")));

        Assert.Equal([
            "stop-download-history",
            "stop-runtime-dashboard",
            "stop-gpu-energy",
            "cancel-pending-ui-work",
            "stop-readiness",
            "dispose-tray",
            "pause-downloads",
            "dispose-benchmarks",
            "stop-runtime-sessions",
            "kill-processes",
            "cleanup-wsl-builds",
            "dispose-gateway",
            "dispose-local-service",
            "drain-background-tasks",
            "dispose-sessions",
            "dispose-huggingface",
            "dispose-app-updates",
            "dispose-runtime-package-client",
            "dispose-metrics-client",
            "dispose-runtime-probe-client",
            "clear-active-settings",
            "clear-active-session",
            "dispose-state-store"
        ], calls);
        Assert.Empty(result.Failures);
    }

    [Fact]
    public async Task AppShutdownCleanupApplicationServiceContinuesAfterFailuresAndBoundsDrain()
    {
        var service = new AppShutdownCleanupApplicationService(TimeSpan.FromMilliseconds(25));
        var calls = new List<string>();
        var neverCompletes = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Task AddAsync(string call)
        {
            calls.Add(call);
            return Task.CompletedTask;
        }

        Task FailAsync(string call)
        {
            calls.Add(call);
            return Task.FromException(new InvalidOperationException("runtime stop was not verified"));
        }

        var result = await service.CleanupAsync(new AppShutdownCleanupActions(
            () => throw new InvalidOperationException("timer failed"),
            () => calls.Add("stop-runtime-dashboard"),
            () => calls.Add("stop-gpu-energy"),
            () => calls.Add("cancel-pending-ui-work"),
            () => calls.Add("stop-readiness"),
            () => calls.Add("dispose-tray"),
            () => AddAsync("pause-downloads"),
            () => AddAsync("dispose-benchmarks"),
            () => calls.Add("kill-processes"),
            () => AddAsync("cleanup-wsl-builds"),
            () => AddAsync("dispose-gateway"),
            () => AddAsync("dispose-local-service"),
            () => neverCompletes.Task,
            () => FailAsync("stop-runtime-sessions"),
            () => calls.Add("dispose-sessions"),
            () => calls.Add("dispose-huggingface"),
            () => calls.Add("dispose-app-updates"),
            () => calls.Add("dispose-runtime-package-client"),
            () => calls.Add("dispose-metrics-client"),
            () => calls.Add("dispose-runtime-probe-client"),
            () => calls.Add("clear-active-settings"),
            () => calls.Add("clear-active-session"),
            () => AddAsync("dispose-state-store")));

        Assert.Equal(3, result.Failures.Count);
        Assert.Contains(result.Failures, failure => failure.Stage == "stop download refresh timer");
        Assert.Contains(result.Failures, failure => failure.Stage == "drain background tasks" && failure.Exception is TimeoutException);
        Assert.Contains(result.Failures, failure => failure.Stage == "stop runtime sessions");
        Assert.True(calls.IndexOf("stop-runtime-sessions") < calls.IndexOf("dispose-gateway"));
        Assert.DoesNotContain("clear-active-session", calls);
        Assert.Contains("dispose-state-store", calls);
    }


}
