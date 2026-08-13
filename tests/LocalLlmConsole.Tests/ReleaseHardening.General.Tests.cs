using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;
using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Windows;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task CorruptSettingsAreBackedUpAndDefaulted()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        await using var store = new StateStore(databasePath);
        await store.InitializeAsync();

        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync(TestContext.Current.CancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = """
INSERT INTO settings (key, value_json, updated_at)
VALUES ('port', '"not-a-port"', $updated_at)
ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;
""";
            command.Parameters.AddWithValue("$updated_at", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(TestContext.Current.CancellationToken);
        }

        var settings = await store.GetAppSettingsAsync(root);

        Assert.Equal(AppSettings.CreateDefault(root).Port, settings.Port);
        Assert.True(Directory.EnumerateFiles(Path.Combine(root, "state", "corrupt-settings"), "*.json").Any());
    }


    [Fact]
    public void GlobalUsingsDoNotLeakWpfIntoServices()
    {
        var globalUsings = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "GlobalUsings.cs"));

        Assert.DoesNotContain("global using System.Windows;", globalUsings, StringComparison.Ordinal);
        Assert.DoesNotContain("global using System.Windows.Controls;", globalUsings, StringComparison.Ordinal);
        Assert.DoesNotContain("global using Forms =", globalUsings, StringComparison.Ordinal);
        Assert.DoesNotContain("global using Wpf", globalUsings, StringComparison.Ordinal);
    }


    [Fact]
    public void LocalAppServiceObservesRequestHandlerTasks()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "LocalAppService.cs"));

        Assert.Contains("QueueRequest(context, cancellationToken)", source, StringComparison.Ordinal);
        Assert.Contains("_requestHandlers", source, StringComparison.Ordinal);
        Assert.Contains("ObserveCompletionAsync", source, StringComparison.Ordinal);
        Assert.Contains("LastListenerError", source, StringComparison.Ordinal);
        Assert.Contains("_listenerErrorCount", source, StringComparison.Ordinal);
        Assert.Contains("await Task.Delay(250, cancellationToken)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_ = Task.Run(() => HandleAsync", source, StringComparison.Ordinal);
    }


    [Fact]
    public async Task StateStoreInitializationServiceRetriesAfterQuarantiningCorruptDatabase()
    {
        var root = CreateTempRoot();
        var databasePath = Path.Combine(root, "state", "local-llm-console.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        await File.WriteAllTextAsync(databasePath, "not a sqlite database", TestContext.Current.CancellationToken);
        var quarantineCalls = 0;
        var service = new StateStoreInitializationService();

        var result = await service.InitializeAsync(new StateStoreInitializationRequest(
            root,
            databasePath,
            () => new StateStore(databasePath),
            path =>
            {
                quarantineCalls++;
                return StateStore.QuarantineDatabaseFiles(path);
            }));

        await using var store = result.StateStore;
        var reloaded = await store.GetAppSettingsAsync(root);

        Assert.Equal(root, result.Settings.WorkspaceRoot);
        Assert.Equal(root, reloaded.WorkspaceRoot);
        Assert.Equal(1, quarantineCalls);
        Assert.True(File.Exists(databasePath));
        Assert.True(Directory.EnumerateDirectories(Path.Combine(root, "state"), "corrupt-database-*").Any());
    }


    [Fact]
    public async Task LocalAppServiceStartupServiceFallsBackAndDisposesFailedPort()
    {
        var created = new List<FakeLocalAppServiceHost>();
        var service = new LocalAppServiceStartupService();

        var result = await service.StartAsync(new LocalAppServiceStartupRequest(
            PreferredPort: 8090,
            MaxFallbackPort: 8092,
            CreateService: port =>
            {
                var host = new FakeLocalAppServiceHost(port, port == 8090 ? new System.Net.Sockets.SocketException() : null);
                created.Add(host);
                return host;
            }));

        Assert.Equal(2, created.Count);
        Assert.Equal(8091, result.Port);
        Assert.Same(created[1], result.Service);
        Assert.True(created[0].Disposed);
        Assert.False(created[1].Disposed);
        Assert.True(created[1].Started);
        Assert.Contains("moved to 127.0.0.1:8091", result.StatusMessage, StringComparison.Ordinal);
    }


    [Fact]
    public async Task BackgroundTaskApplicationServiceReportsFailuresAndIgnoresCancellation()
    {
        var service = new BackgroundTaskApplicationService();
        var statuses = new List<string>();
        var errors = new List<Exception>();
        var actions = new BackgroundTaskApplicationActions(
            statuses.Add,
            error =>
            {
                errors.Add(error);
                return Task.CompletedTask;
            });

        await service.RunAsync(
            () => throw new OperationCanceledException(),
            "Cancelled task failed",
            actions);
        await service.RunAsync(
            () => throw new InvalidOperationException("offline"),
            "Background refresh failed",
            actions);

        Assert.Equal(["Background refresh failed: offline"], statuses);
        var error = Assert.Single(errors);
        Assert.IsType<InvalidOperationException>(error);
        Assert.Equal("offline", error.Message);
    }


    [Fact]
    public async Task ForegroundTaskApplicationServiceOwnsBusyAndEventErrorBoundaries()
    {
        var service = new ForegroundTaskApplicationService();
        var calls = new List<string>();
        var errors = new List<Exception>();
        var dialogs = new List<string>();
        var currentStatus = "";

        ForegroundTaskApplicationActions Actions(bool canBegin = true)
            => new(
                message =>
                {
                    calls.Add($"begin:{message}");
                    return canBegin;
                },
                () => calls.Add("end"),
                status =>
                {
                    currentStatus = status;
                    calls.Add($"status:{status}");
                },
                () => currentStatus,
                () =>
                {
                    calls.Add("yield");
                    return Task.CompletedTask;
                },
                error =>
                {
                    errors.Add(error);
                    calls.Add($"log:{error.Message}");
                    return Task.CompletedTask;
                },
                error =>
                {
                    dialogs.Add(error.Message);
                    calls.Add($"dialog:{error.Message}");
                });

        await service.RunBusyAsync(
            "Loading...",
            () =>
            {
                calls.Add($"action:{currentStatus}");
                return Task.CompletedTask;
            },
            Actions());
        await service.RunBusyAsync(
            "Skipped",
            () => throw new InvalidOperationException("Should not run."),
            Actions(canBegin: false));
        await service.RunBusyAsync(
            "Saving...",
            () => throw new InvalidOperationException("save failed"),
            Actions());
        await service.RunEventAsync(
            () => throw new InvalidOperationException("event failed"),
            Actions());

        Assert.Equal([
            "begin:Loading...",
            "status:Loading...",
            "yield",
            "action:Loading...",
            "status:",
            "end",
            "begin:Skipped",
            "begin:Saving...",
            "status:Saving...",
            "yield",
            "status:save failed",
            "log:save failed",
            "dialog:save failed",
            "end",
            "status:event failed",
            "log:event failed",
            "dialog:event failed"
        ], calls);
        Assert.Equal(["save failed", "event failed"], errors.Select(error => error.Message).ToArray());
        Assert.Equal(["save failed", "event failed"], dialogs);
    }


    [Fact]
    public void ShellIntegrationServiceOwnsProcessLaunchAndFolderCreation()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "ShellIntegrationService.cs"));
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
        Assert.DoesNotContain("Process.Start", source, StringComparison.Ordinal);
    }



    [Fact]
    public void ClipboardServiceOwnsClipboardSetTextAction()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "ClipboardService.cs"));
        var copied = new List<string>();
        var service = new ClipboardService(copied.Add);

        service.SetText("secret-key");

        Assert.Equal(["secret-key"], copied);
        Assert.Throws<ArgumentNullException>(() => service.SetText(null!));
        Assert.DoesNotContain("System.Windows.Clipboard.SetText", source, StringComparison.Ordinal);
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
                () => AddAsync("refresh-jobs"),
                () => AddAsync("refresh-overview"),
                () => AddAsync("refresh-download-history"),
                () => AddAsync("refresh-install-state")));

        Assert.Equal([
            "wait:job-1:1500",
            "ui:begin",
            "scan-models",
            "refresh-models",
            "refresh-jobs",
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

        await service.CleanupAsync(new AppShutdownCleanupActions(
            () => calls.Add("stop-download-history"),
            () => calls.Add("stop-runtime-dashboard"),
            () => calls.Add("cancel-launch-settings"),
            () => calls.Add("stop-readiness"),
            () => calls.Add("dispose-tray"),
            () => AddAsync("pause-downloads"),
            () => calls.Add("kill-processes"),
            () => AddAsync("cleanup-wsl-builds"),
            () => AddAsync("dispose-gateway"),
            () => AddAsync("stop-runtime-sessions"),
            () => calls.Add("dispose-sessions"),
            () => calls.Add("dispose-runtime-package-client"),
            () => calls.Add("dispose-metrics-client"),
            () => calls.Add("dispose-runtime-probe-client"),
            () => calls.Add("clear-active-settings"),
            () => calls.Add("clear-active-session"),
            () => AddAsync("dispose-local-service"),
            () => AddAsync("dispose-state-store")));

        Assert.Equal([
            "stop-download-history",
            "stop-runtime-dashboard",
            "cancel-launch-settings",
            "stop-readiness",
            "dispose-tray",
            "pause-downloads",
            "kill-processes",
            "cleanup-wsl-builds",
            "dispose-gateway",
            "stop-runtime-sessions",
            "dispose-sessions",
            "dispose-runtime-package-client",
            "dispose-metrics-client",
            "dispose-runtime-probe-client",
            "clear-active-settings",
            "clear-active-session",
            "dispose-local-service",
            "dispose-state-store"
        ], calls);
    }


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



    private sealed class FakeLocalAppServiceHost : ILocalAppServiceHost
    {
        private readonly Exception? _failure;

        public FakeLocalAppServiceHost(int port, Exception? failure = null)
        {
            _failure = failure;
            BaseUri = new Uri($"http://127.0.0.1:{port}/");
        }

        public Uri BaseUri { get; }
        public bool Started { get; private set; }
        public bool Disposed { get; private set; }

        public Task StartAsync()
        {
            if (_failure is not null) throw _failure;
            Started = true;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Disposed = true;
            return ValueTask.CompletedTask;
        }
    }

    private sealed class FakeSingleInstanceLease : ISingleInstanceLease
    {
        public FakeSingleInstanceLease(bool ownsInstance)
        {
            OwnsInstance = ownsInstance;
        }

        public bool OwnsInstance { get; private set; }

        public bool Released { get; private set; }

        public bool Disposed { get; private set; }

        public void Release()
        {
            Released = true;
            OwnsInstance = false;
        }

        public void Dispose()
            => Disposed = true;
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

    private sealed class FakeDownloadOperations : IHuggingFaceDownloadOperations
    {
        private readonly HashSet<string> _activeJobIds = new(StringComparer.OrdinalIgnoreCase);

        public List<string> ResumedJobIds { get; } = [];
        public List<string> PausedJobIds { get; } = [];
        public List<string> StoppedJobIds { get; } = [];

        public Task ResumeDownloadAsync(JobRecord job, AppSettings settings)
        {
            ResumedJobIds.Add(job.Id);
            _activeJobIds.Add(job.Id);
            return Task.CompletedTask;
        }

        public Task PauseDownloadAsync(JobRecord job)
        {
            PausedJobIds.Add(job.Id);
            _activeJobIds.Remove(job.Id);
            return Task.CompletedTask;
        }

        public Task StopDownloadAsync(JobRecord job)
        {
            StoppedJobIds.Add(job.Id);
            _activeJobIds.Remove(job.Id);
            return Task.CompletedTask;
        }

        public bool IsDownloadActive(string jobId) => _activeJobIds.Contains(jobId);
    }

}
