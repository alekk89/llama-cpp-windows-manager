using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class OverviewTelemetryTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task OverviewModelSelectionApplicationServiceOwnsLoadedInactiveAndStoppedSelection()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "model-1",
            "Qwen",
            Path.Combine(root, "models", "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var activeSettings = AppSettings.CreateDefault(root) with { Port = 8084 };
        var service = new OverviewModelSelectionApplicationService();
        var calls = new List<string>();

        OverviewModelSelectionApplicationActions Actions(bool selectSucceeds = true)
            => new(
                modelId =>
                {
                    calls.Add($"select:{modelId}");
                    return new RuntimeSessionSelectResult(selectSucceeds, selectSucceeds ? activeSettings : null);
                },
                settings => calls.Add($"active:{settings?.Port}"),
                () =>
                {
                    calls.Add("save");
                    return Task.CompletedTask;
                },
                () =>
                {
                    calls.Add("metrics");
                    return Task.CompletedTask;
                },
                status => calls.Add($"status:{status}"));

        var ignored = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(null, IsLoaded: false, IsActive: false),
            Actions(),
            TestContext.Current.CancellationToken);
        var stopped = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: false, IsActive: false),
            Actions(),
            TestContext.Current.CancellationToken);
        var active = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: true, IsActive: true),
            Actions(),
            TestContext.Current.CancellationToken);
        var switched = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: true, IsActive: false),
            Actions(),
            TestContext.Current.CancellationToken);
        var staleLoaded = await service.SelectAsync(
            new OverviewModelSelectionApplicationRequest(model, IsLoaded: true, IsActive: false),
            Actions(selectSucceeds: false),
            TestContext.Current.CancellationToken);

        Assert.Equal(OverviewModelSelectionOutcome.Ignored, ignored);
        Assert.Equal(OverviewModelSelectionOutcome.NotLoaded, stopped);
        Assert.Equal(OverviewModelSelectionOutcome.ActiveLoaded, active);
        Assert.Equal(OverviewModelSelectionOutcome.SwitchedLoaded, switched);
        Assert.Equal(OverviewModelSelectionOutcome.NotLoaded, staleLoaded);
        Assert.Equal([
            "status:Qwen is not loaded. Load it to expose an OpenAI-compatible endpoint.",
            "metrics",
            "metrics",
            $"select:{model.Id}",
            "active:8084",
            "save",
            "metrics",
            $"select:{model.Id}",
            "status:Selected model is no longer loaded.",
            "metrics"
        ], calls);
    }


    [Fact]
    public async Task OverviewLoadedSessionSelectionApplicationServiceOwnsModelLookupRefreshAndRuntimeSelection()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "model-1",
            "Qwen",
            Path.Combine(root, "models", "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var activeSettings = AppSettings.CreateDefault(root) with { Port = 8085 };
        var service = new OverviewLoadedSessionSelectionApplicationService();
        var calls = new List<string>();
        var knownModels = new List<ModelRecord>();
        var selectSucceeds = true;

        var actions = new OverviewLoadedSessionSelectionApplicationActions(
            modelId =>
            {
                calls.Add($"find:{modelId}");
                return knownModels.FirstOrDefault(item => string.Equals(item.Id, modelId, StringComparison.OrdinalIgnoreCase));
            },
            () =>
            {
                calls.Add("refresh-selector");
                knownModels.Add(model);
                return Task.CompletedTask;
            },
            modelId => calls.Add($"select-ui:{modelId}"),
            modelId =>
            {
                calls.Add($"select-runtime:{modelId}");
                return new RuntimeSessionSelectResult(selectSucceeds, selectSucceeds ? activeSettings : null);
            },
            settings => calls.Add($"active:{settings?.Port}"),
            () =>
            {
                calls.Add("save");
                return Task.CompletedTask;
            },
            () =>
            {
                calls.Add("metrics");
                return Task.CompletedTask;
            },
            () => calls.Add("actions"),
            status => calls.Add($"status:{status}"));

        var ignored = await service.SelectAsync("", "", actions, TestContext.Current.CancellationToken);
        var selectedAfterRefresh = await service.SelectAsync(model.Id, "session-1", actions, TestContext.Current.CancellationToken);
        knownModels.Clear();
        selectSucceeds = false;
        var stale = await service.SelectAsync(model.Id, "session-1", actions, TestContext.Current.CancellationToken);

        Assert.Equal(OverviewLoadedSessionSelectionOutcome.Ignored, ignored);
        Assert.Equal(OverviewLoadedSessionSelectionOutcome.Selected, selectedAfterRefresh);
        Assert.Equal(OverviewLoadedSessionSelectionOutcome.Stale, stale);
        Assert.Equal([
            $"find:{model.Id}",
            "refresh-selector",
            $"find:{model.Id}",
            $"select-ui:{model.Id}",
            "select-runtime:session-1",
            "active:8085",
            "save",
            "metrics",
            "actions",
            "status:Selected loaded model Qwen.",
            $"find:{model.Id}",
            "refresh-selector",
            $"find:{model.Id}",
            $"select-ui:{model.Id}",
            "select-runtime:session-1",
            "status:Selected session is no longer loaded."
        ], calls);
    }


    [Fact]
    public async Task RuntimeMetricPollerServicePollsMetricsAndSlotsForRunningSessions()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = true };
        var session = RuntimeMetricSession(root, settings);
        var paths = new System.Collections.Concurrent.ConcurrentQueue<string>();
        using var handler = new CapturingHttpHandler(request =>
        {
            paths.Enqueue(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath switch
            {
                "/metrics" => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""
                    llama_tokens_predicted_total 42
                    llama_prompt_tokens_total 9
                    """)
                },
                "/slots" => new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"is_processing":true,"n_prompt_tokens_processed":9,"n_decoded":4,"n_prompt_tokens":12,"n_ctx":4096}]""")
                },
                _ => new HttpResponseMessage(System.Net.HttpStatusCode.NotFound)
            };
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeMetricPollerService(http);

        var results = await service.PollSessionsAsync([session], TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Equal("model-1|runtime-1|8081", result.RuntimeKey);
        Assert.Empty(result.Error);
        Assert.Contains(result.Samples, sample => sample.Name == "llama_tokens_predicted_total" && sample.Value == 42);
        Assert.Equal(9, result.SlotSnapshot?.PromptTokensProcessed);
        Assert.Equal(4, result.SlotSnapshot?.GeneratedTokens);
        Assert.Contains("/metrics", paths);
        Assert.Contains("/slots", paths);
    }


    [Fact]
    public async Task RuntimeMetricPollerServiceSkipsMetricsWhenDisabledButKeepsSlots()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = false };
        var session = RuntimeMetricSession(root, settings);
        var paths = new List<string>();
        using var handler = new CapturingHttpHandler(request =>
        {
            paths.Add(request.RequestUri!.AbsolutePath);
            return request.RequestUri.AbsolutePath == "/slots"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""[{"is_processing":false,"n_prompt_tokens_processed":5,"n_decoded":2}]""")
                }
                : new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeMetricPollerService(http);

        var result = Assert.Single(await service.PollSessionsAsync([session], TestContext.Current.CancellationToken));

        Assert.Empty(result.Samples);
        Assert.Empty(result.Error);
        Assert.Equal(5, result.SlotSnapshot?.PromptTokensProcessed);
        Assert.Equal(["/slots"], paths);
    }


    [Fact]
    public async Task RuntimeMetricPollerServiceReturnsErrorWhenMetricsFail()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081, EnableMetrics = true };
        var session = RuntimeMetricSession(root, settings);
        using var handler = new CapturingHttpHandler(request =>
            request.RequestUri!.AbsolutePath == "/slots"
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("[]") }
                : new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable));
        using var http = new HttpClient(handler);
        var service = new RuntimeMetricPollerService(http);

        var result = Assert.Single(await service.PollSessionsAsync([session], TestContext.Current.CancellationToken));

        Assert.Empty(result.Samples);
        Assert.Contains("503", result.Error, StringComparison.Ordinal);
        Assert.NotNull(result.SlotSnapshot);
    }


}
