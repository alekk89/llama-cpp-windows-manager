using System.Net;
using System.Text;
using System.Collections.Concurrent;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task EndpointInspectionReadsLiveDirectEndpointWithoutGenerating()
    {
        var requests = new ConcurrentBag<HttpRequestMessage>();
        using var http = new HttpClient(new CapturingHttpHandler(request =>
        {
            requests.Add(request);
            var json = request.RequestUri?.AbsolutePath switch
            {
                "/health" => "{\"status\":\"ok\"}",
                "/v1/models" => "{\"data\":[{\"id\":\"qwen-live\",\"owned_by\":\"llamacpp\",\"meta\":{\"n_ctx_train\":262144,\"n_params\":27000000000,\"size\":29100000000}}]}",
                "/props" => "{\"model_path\":\"D:/models/qwen.gguf\",\"total_slots\":2,\"build_info\":\"b9999\",\"modalities\":{\"vision\":true},\"chat_template_caps\":{\"supports_reasoning\":true,\"supports_tool_calls\":true},\"default_generation_settings\":{\"n_ctx\":16384,\"speculative\":true,\"params\":{\"n_predict\":-1,\"max_tokens\":16384,\"reasoning_format\":\"deepseek\",\"temperature\":1.0,\"top_k\":20,\"top_p\":0.95,\"min_p\":0.0}}}",
                "/slots" => "[{\"id\":0,\"is_processing\":true,\"n_ctx\":16384,\"speculative\":true,\"params\":{\"n_predict\":-1,\"max_tokens\":4096,\"reasoning_format\":\"deepseek\",\"temperature\":0.7,\"top_k\":40,\"top_p\":0.9,\"min_p\":0.05},\"next_token\":{\"n_decoded\":512,\"n_remain\":3584}}]",
                _ => "{}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }));
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            Port = 28081,
            ModelApiKey = new string('k', 40)
        };
        var session = new LoadedModelSessionSnapshot(
            "session-qwen", "qwen", "Qwen", "runtime", "Official", RuntimeMode.Native, RuntimeBackend.Cuda,
            settings, "runtime.log", DateTimeOffset.UtcNow, "marker", 42, LoadedModelSessionStatus.Running, true, true);

        var report = await new EndpointInspectionService(http).InspectDirectAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("ok", report.Health);
        Assert.Equal("qwen-live", Assert.Single(report.Models).Id);
        Assert.Equal(262144, report.Models[0].TrainingContext);
        Assert.Equal(16384, report.Defaults?.ContextSize);
        Assert.Equal(16384, report.Defaults?.MaximumOutputTokens);
        Assert.Equal("Supported", report.Defaults?.Vision);
        Assert.Contains("request-controlled", report.Defaults?.Reasoning, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("deepseek", report.Defaults?.ReasoningFormat);
        Assert.True(report.Defaults?.Speculative);
        Assert.True(Assert.Single(report.Slots).IsProcessing);
        Assert.Equal(4096, report.Slots[0].MaximumOutputTokens);
        Assert.Equal(512, report.Slots[0].DecodedTokens);
        Assert.Equal(3584, report.Slots[0].RemainingTokens);
        Assert.Equal("deepseek", report.Slots[0].ReasoningFormat);
        Assert.Equal(["/health", "/props", "/slots", "/v1/models"], requests.Select(request => request.RequestUri!.AbsolutePath).Order().ToArray());
        Assert.All(requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(settings.ModelApiKey, request.Headers.Authorization?.Parameter);
        });
        Assert.DoesNotContain(requests, request => request.Method != HttpMethod.Get);
        Assert.Empty(report.UnavailableSources);
    }

    [Fact]
    public async Task EndpointInspectionReadsGatewayAdvertisementAndRunningState()
    {
        var requests = new ConcurrentBag<HttpRequestMessage>();
        using var http = new HttpClient(new CapturingHttpHandler(request =>
        {
            requests.Add(request);
            var json = request.RequestUri?.AbsolutePath switch
            {
                "/health" => "{\"ok\":true,\"gateway\":\"model-auto-load\"}",
                "/v1/models" => "{\"data\":[{\"id\":\"qwen:128k\",\"name\":\"Qwen\",\"profile_name\":\"128k\",\"owned_by\":\"local-llm-console\"}]}",
                "/running" => "{\"data\":[{\"id\":\"qwen\",\"name\":\"Qwen\",\"endpoint\":\"http://127.0.0.1:8089/v1\",\"status\":\"Running\",\"runtime\":\"CUDA\",\"startedAt\":\"2026-08-15T10:00:00Z\"}]}",
                _ => "{}"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }));
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            AutoLoadGatewayPort = 28082,
            ModelApiKey = new string('g', 40)
        };

        var report = await new EndpointInspectionService(http).InspectGatewayAsync(
            settings,
            "Prefer keeping loaded models",
            "Local only",
            TestContext.Current.CancellationToken);

        Assert.Equal(EndpointInspectionKind.Gateway, report.Kind);
        Assert.Equal("Healthy", report.Health);
        Assert.Equal("qwen:128k", Assert.Single(report.Models).Id);
        Assert.Equal("128k", report.Models[0].Profile);
        Assert.Equal("http://127.0.0.1:8089/v1", Assert.Single(report.RunningModels).Endpoint);
        Assert.Null(report.Defaults);
        Assert.Empty(report.Slots);
        Assert.Equal("Prefer keeping loaded models", report.GatewayPolicy);
        Assert.All(requests, request => Assert.Equal(settings.ModelApiKey, request.Headers.Authorization?.Parameter));
    }

    [Fact]
    public async Task EndpointInspectionKeepsPartialResultsWhenOptionalEndpointIsUnavailable()
    {
        using var http = new HttpClient(new CapturingHttpHandler(request =>
            request.RequestUri?.AbsolutePath == "/props"
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"error\":\"not found\"}") }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(request.RequestUri?.AbsolutePath == "/v1/models" ? "{\"data\":[{\"id\":\"model\"}]}" : "{}") }));
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with { Port = 28083 };
        var session = new LoadedModelSessionSnapshot(
            "session", "model", "Model", "runtime", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu,
            settings, "runtime.log", DateTimeOffset.UtcNow, "marker", 1, LoadedModelSessionStatus.Running, true, true);

        var report = await new EndpointInspectionService(http).InspectDirectAsync(session, TestContext.Current.CancellationToken);

        Assert.Equal("model", Assert.Single(report.Models).Id);
        Assert.Null(report.Defaults);
        Assert.Contains(report.UnavailableSources, source => source.StartsWith("/props: HTTP 404", StringComparison.Ordinal));
    }
}
