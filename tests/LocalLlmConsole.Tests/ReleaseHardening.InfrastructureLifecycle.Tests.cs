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


}
