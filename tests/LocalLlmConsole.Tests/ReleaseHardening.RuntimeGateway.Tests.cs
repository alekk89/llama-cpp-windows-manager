using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void GatewayExposesSavedProfilesWithClientNeutralModelIds()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord(
            "qwen-model",
            "Qwen Model",
            Path.Combine(root, "qwen.gguf"),
            OwnershipKind.External,
            "{}",
            DateTimeOffset.UtcNow);
        var settings = AppSettings.CreateDefault(root);
        var defaultProfile = new NamedModelLaunchProfile(
            "default:qwen-model",
            model.Id,
            "Default",
            ModelLaunchSettings.FromAppSettings(settings, "runtime"),
            DateTimeOffset.UtcNow,
            true);
        var tunedProfile = new NamedModelLaunchProfile(
            "Profile 128K / DFlash",
            model.Id,
            "128K DFlash",
            ModelLaunchSettings.FromAppSettings(settings with { ContextSize = 131072 }, "runtime"),
            DateTimeOffset.UtcNow,
            false);
        var routes = new[]
        {
            new ModelGatewayModelRoute(model, defaultProfile),
            new ModelGatewayModelRoute(model, tunedProfile)
        };

        Assert.Equal(model.Id, routes[0].Id);
        Assert.Equal("qwen-model--profile-128k-dflash", routes[1].Id);
        Assert.Same(routes[1], ModelGatewayRequestResolver.ResolveModel(routes, routes[1].Id));
        Assert.DoesNotContain("opencode", routes[1].Id, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GatewayDisambiguatesNormalizedProfileIdCollisionsDeterministically()
    {
        var root = CreateTempRoot();
        var model = new ModelRecord("qwen-model", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var launchSettings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(root), "runtime");
        var first = new NamedModelLaunchProfile("Profile 128K / DFlash", model.Id, "First", launchSettings, DateTimeOffset.UtcNow);
        var second = new NamedModelLaunchProfile("profile-128k-dflash", model.Id, "Second", launchSettings, DateTimeOffset.UtcNow);
        var source = new[] { new ModelGatewayModelRoute(model, first), new ModelGatewayModelRoute(model, second) };

        var routes = ModelGatewayRouteId.EnsureUnique(source);
        var repeated = ModelGatewayRouteId.EnsureUnique(source);

        Assert.NotEqual(routes[0].Id, routes[1].Id, StringComparer.OrdinalIgnoreCase);
        Assert.StartsWith("qwen-model--profile-128k-dflash-", routes[0].Id, StringComparison.Ordinal);
        Assert.StartsWith("qwen-model--profile-128k-dflash-", routes[1].Id, StringComparison.Ordinal);
        Assert.Equal(routes.Select(route => route.Id), repeated.Select(route => route.Id));
    }

    [Fact]
    public async Task GatewayAllowsSameProfileConcurrencyAndWaitsBeforeSwitchingProfiles()
    {
        var gates = new ModelGatewayRequestGate();
        using var first = await gates.EnterAsync("qwen", "profile-a", TestContext.Current.CancellationToken);

        var sameProfile = await gates.EnterAsync("QWEN", "PROFILE-A", TestContext.Current.CancellationToken);
        var blocked = gates.EnterAsync("qwen", "profile-b", TestContext.Current.CancellationToken);
        Assert.False(blocked.IsCompleted);

        using var differentModel = await gates.EnterAsync("llama", "profile-a", TestContext.Current.CancellationToken);

        first.Dispose();
        Assert.False(blocked.IsCompleted);
        sameProfile.Dispose();
        using var switched = await blocked.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GatewayGivesWaitingProfilePriorityOverNewActiveProfileRequests()
    {
        var gates = new ModelGatewayRequestGate();
        using var active = await gates.EnterAsync("qwen", "profile-a", TestContext.Current.CancellationToken);
        var switchRequest = gates.EnterAsync("qwen", "profile-b", TestContext.Current.CancellationToken);
        var lateActiveRequest = gates.EnterAsync("qwen", "profile-a", TestContext.Current.CancellationToken);

        Assert.False(switchRequest.IsCompleted);
        Assert.False(lateActiveRequest.IsCompleted);
        active.Dispose();

        using var switched = await switchRequest.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.False(lateActiveRequest.IsCompleted);
        switched.Dispose();

        using var resumed = await lateActiveRequest.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task CancelledGatewayProfileWaiterDoesNotConsumeAnActiveLease()
    {
        var gates = new ModelGatewayRequestGate();
        using var active = await gates.EnterAsync("qwen", "profile-a", TestContext.Current.CancellationToken);
        using var cancellation = new CancellationTokenSource();
        var cancelledWaiter = gates.EnterAsync("qwen", "profile-b", cancellation.Token);
        var next = gates.EnterAsync("qwen", "profile-c", TestContext.Current.CancellationToken);

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelledWaiter);
        active.Dispose();

        using var admitted = await next.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void DisplayFormatServiceFormatsMetricsBytesElapsedAndLongText()
    {
        Assert.Equal("0s", DisplayFormatService.Elapsed(TimeSpan.FromSeconds(-1)));
        Assert.Equal("59s", DisplayFormatService.Elapsed(TimeSpan.FromSeconds(59.9)));
        Assert.Equal("1m 05s", DisplayFormatService.Elapsed(TimeSpan.FromSeconds(65)));
        Assert.Equal("2h 03m 04s", DisplayFormatService.Elapsed(new TimeSpan(2, 3, 4)));
        Assert.Equal("1.5 KB", DisplayFormatService.Bytes(1536));
        Assert.Equal("", DisplayFormatService.Bytes(0));
        Assert.Equal("0 B", DisplayFormatService.BytesOrZero(0));
        Assert.Equal("12.346", DisplayFormatService.MetricNumber(12.3456));
        Assert.Equal("No release notes were provided.", DisplayFormatService.TrimForDisplay("", 100));
        Assert.Equal("abcdef\n\n...", DisplayFormatService.TrimForDisplay("abcdefgh", 6));
    }


    [Fact]
    public void GpuStatusServiceFormatsNvidiaSmiCsvLine()
    {
        var formatted = GpuStatusService.FormatNvidiaSmiCsvLine("0, NVIDIA RTX, 76, 62, 12288, 24576");

        Assert.Equal("GPU 0: NVIDIA RTX | 76% | 62C | 12.0/24.0 GiB", formatted);
        Assert.Equal("GPU 0: 76% | 62C | 12.0/24.0 GiB", GpuStatusService.NormalizeMetricSeparators("GPU 0: 76%|62C|12.0/24.0 GiB"));
    }


    [Fact]
    public void GpuStatusServiceFormatsIntelArcSyclLine()
    {
        var formatted = GpuStatusService.FormatIntelArcStatus("[level_zero:gpu][level_zero:0] Intel(R) Arc(TM) A770 Graphics");

        Assert.Equal("Intel(R) Arc(TM) A770 Graphics", formatted);
        Assert.Equal("Intel Arc GPU", GpuStatusService.FormatIntelArcStatus(""));
    }


    [Fact]
    public void GpuStatusServiceFormatsWindowsGpuCounterJson()
    {
        const string json = """
        [{"Index":0,"Name":"AMD Radeon RX 7900 XTX","Utilization":53.4,"MemoryUsedBytes":8589934592,"MemoryTotalBytes":25769803776}]
        """;

        var formatted = GpuStatusService.FormatWindowsGpuStatusJson(json);

        Assert.Equal(["GPU 0: AMD Radeon RX 7900 XTX | 53.4% | 8.0/24.0 GiB"], formatted);
        Assert.Equal(
            ["GPU 0: Intel(R) Graphics | 12% | 1.5 GiB used"],
            GpuStatusService.FormatWindowsGpuStatusJson("[{\"Index\":0,\"Name\":\"Intel(R) Graphics\",\"Utilization\":12,\"MemoryUsedBytes\":1610612736}]"));
    }

    [Fact]
    public void GpuStatusServiceFormatsWindowsCpuTemperatureJson()
    {
        Assert.Equal("CPU: 57.2C", GpuStatusService.FormatWindowsCpuTemperatureJson("{\"TemperatureCelsius\":57.2}"));
        Assert.Equal("CPU: 42C", GpuStatusService.FormatWindowsCpuTemperatureJson("[{\"CurrentTemperature\":3151.5},{\"TemperatureCelsius\":36.4}]"));
        Assert.Equal("", GpuStatusService.FormatWindowsCpuTemperatureJson("{}"));
        Assert.Equal(
            $"CPU: AMD Ryzen 9 7950X{Environment.NewLine}Telemetry: 18.5% load | 16C/32T | 57.2 °C thermal",
            GpuStatusService.FormatWindowsCpuStatusJson("{\"Name\":\"AMD Ryzen 9 7950X 16-Core Processor\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32,\"TemperatureCelsius\":57.2}"));
    }


    [Fact]
    public async Task GpuStatusProbeServiceRunsThroughProcessRunner()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Infrastructure", "GpuStatusService.cs"));
        var commands = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            commands.Add($"{Path.GetFileName(psi.FileName)} {string.Join(" ", psi.ArgumentList)}");
            if (string.Equals(Path.GetFileName(psi.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                var script = DecodePowerShellCommand(psi);
                return script.Contains("MSAcpi_ThermalZoneTemperature", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"TemperatureCelsius\":57.2}", "")
                    : new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"AMD Radeon RX 7900 XTX\",\"Utilization\":53.4,\"MemoryUsedBytes\":8589934592,\"MemoryTotalBytes\":25769803776}]", "");
            }
            if (psi.ArgumentList.Contains("--query-gpu=memory.free,memory.total"))
                return new ProcessRunResult(0, "1024, 24576\n8192, 24576", "");
            if (psi.ArgumentList.Contains("--query-gpu=index,name,utilization.gpu,temperature.gpu,memory.used,memory.total"))
                return new ProcessRunResult(0, "0, NVIDIA RTX, 76, 62, 12288, 24576", "");
            return new ProcessRunResult(0, "[level_zero:gpu][level_zero:0] Intel(R) Arc(TM) A770 Graphics", "");
        });
        var service = new GpuStatusProbeService(runner, () => "sycl-ls.exe", () => "nvidia-smi.exe", () => "powershell.exe");

        var memory = await service.MemoryAsync(TestContext.Current.CancellationToken);
        var summary = await service.SummaryAsync(TestContext.Current.CancellationToken);
        var windows = await service.WindowsSummaryAsync(TestContext.Current.CancellationToken);
        var cpu = await service.CpuTemperatureAsync(TestContext.Current.CancellationToken);
        var cpuSummary = await service.CpuSummaryAsync(TestContext.Current.CancellationToken);
        var sycl = await service.WindowsIntelArcSummaryAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(memory);
        Assert.Equal(9, memory.FreeGiB);
        Assert.Equal(48, memory.TotalGiB);
        Assert.Equal("GPU 0: NVIDIA RTX | 76% | 62C | 12.0/24.0 GiB", summary);
        Assert.Equal("GPU 0: AMD Radeon RX 7900 XTX | 53.4% | 8.0/24.0 GiB", windows);
        Assert.Equal("CPU: 57.2C", cpu);
        Assert.Equal("Telemetry: 57.2 °C thermal", cpuSummary);
        Assert.Equal("Intel(R) Arc(TM) A770 Graphics", sycl);
        Assert.Contains(commands, command => command.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)
            && command.Contains("-EncodedCommand", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("--query-gpu=memory.free,memory.total", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("--query-gpu=index,name,utilization.gpu,temperature.gpu,memory.used,memory.total", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.StartsWith("sycl-ls", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Process.Start(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new GpuStatusProbeService", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new TrackedProcessRunner", source, StringComparison.Ordinal);

        static string DecodePowerShellCommand(ProcessStartInfo psi)
        {
            var encodedIndex = -1;
            for (var i = 0; i < psi.ArgumentList.Count; i++)
            {
                if (string.Equals(psi.ArgumentList[i], "-EncodedCommand", StringComparison.OrdinalIgnoreCase))
                {
                    encodedIndex = i;
                    break;
                }
            }

            return encodedIndex >= 0 && encodedIndex + 1 < psi.ArgumentList.Count
                ? System.Text.Encoding.Unicode.GetString(Convert.FromBase64String(psi.ArgumentList[encodedIndex + 1]))
                : "";
        }
    }


    [Fact]
    public void RuntimeEndpointServiceBuildsLocalAndLanUrls()
    {
        var root = CreateTempRoot();
        var local = AppSettings.CreateDefault(root) with
        {
            Host = "0.0.0.0",
            Port = 8081,
            ModelAccessMode = "local"
        };
        var lan = local with { ModelAccessMode = "models", Host = "192.168.1.20" };
        var gateway = local with { ModelAccessMode = "gateway", Host = "127.0.0.1" };

        Assert.Equal("http://127.0.0.1:8081", RuntimeEndpointService.LocalServerBaseUrl(local));
        Assert.Equal("http://127.0.0.1:8081/v1", RuntimeEndpointService.LocalOpenAiBaseUrl(local));
        Assert.Equal("http://127.0.0.1:8082/v1", RuntimeEndpointService.LocalGatewayOpenAiBaseUrl(local));
        Assert.Equal("http://192.168.1.20:8081/v1", RuntimeEndpointService.LanOpenAiBaseUrl(lan));
        Assert.Equal("http://192.168.1.20:8081/v1", RuntimeEndpointService.EndpointDisplay(lan));
        Assert.Equal("http://127.0.0.1:8081/v1", RuntimeEndpointService.EndpointDisplay(gateway));
        Assert.Contains("LAN:", RuntimeEndpointService.GatewayEndpointDisplay(gateway), StringComparison.Ordinal);
        Assert.Equal("[::1]", RuntimeEndpointService.UrlHost("::1"));
    }


    [Fact]
    public void ModelGatewayOptionsFollowAppSettings()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 9091,
            AutoLoadGatewayPolicy = "Single active model",
            ModelAccessMode = "gateway",
            ModelApiKey = new string('a', 32)
        };

        var options = ModelGatewayOptions.FromSettings(settings);

        Assert.True(options.Enabled);
        Assert.True(options.AllowLanAccess);
        Assert.Equal(9091, options.Port);
        Assert.Equal("http://+:9091/", options.ListenerPrefix);
        Assert.Equal(ModelGatewaySwapPolicy.SingleActive, options.SwapPolicy);
    }


    [Fact]
    public void ModelGatewayHostFactoryServiceOwnsGatewayHostAndControllerCreation()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8091
        };
        var calls = new List<string>();
        var expectedController = new FakeModelGatewayRuntimeController();
        var expectedHost = new FakeModelGatewayHost();
        var service = new ModelGatewayHostFactoryService(
            actions =>
            {
                calls.Add("controller");
                Assert.NotNull(actions.ListModelsAsync);
                Assert.NotNull(actions.RunningSessionsAsync);
                Assert.NotNull(actions.EnsureModelLoadedAsync);
                return expectedController;
            },
            (options, runtime) =>
            {
                calls.Add($"host:{options.Port}");
                Assert.Same(expectedController, runtime);
                return expectedHost;
            });
        var actions = new ModelGatewayRuntimeControllerActions(
            _ => Task.FromResult<IReadOnlyList<ModelGatewayModelRoute>>([]),
            _ => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>([]),
            (_, _, _) => Task.FromException<LoadedModelSessionSnapshot>(new NotSupportedException()));

        var controller = service.CreateRuntimeController(actions);
        var host = service.CreateGatewayHost(ModelGatewayOptions.FromSettings(settings), controller);

        Assert.Same(expectedController, controller);
        Assert.Same(expectedHost, host);
        Assert.Equal(["controller", "host:8091"], calls);
        Assert.Throws<ArgumentNullException>(() => service.CreateRuntimeController(null!));
        Assert.Throws<ArgumentNullException>(() => service.CreateGatewayHost(null!, controller));
        Assert.Throws<ArgumentNullException>(() => service.CreateGatewayHost(ModelGatewayOptions.FromSettings(settings), null!));
    }


    [Fact]
    public void ModelGatewayRequestBodyReaderRejectsOversizedBodies()
    {
        var small = System.Text.Encoding.UTF8.GetBytes("""{"model":"qwen"}""");
        var tooLarge = System.Text.Encoding.UTF8.GetBytes("""{"model":"qwen","messages":["0123456789"]}""");
        var source = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayService.cs"));

        var read = ModelGatewayRequestBodyReader.ReadBodyBuffer(new MemoryStream(small), small.Length, small.Length);
        var declared = Assert.Throws<ModelGatewayRequestBodyTooLargeException>(() =>
            ModelGatewayRequestBodyReader.ReadBodyBuffer(new MemoryStream(small), small.Length + 1, small.Length));
        var streamed = Assert.Throws<ModelGatewayRequestBodyTooLargeException>(() =>
            ModelGatewayRequestBodyReader.ReadBodyBuffer(new MemoryStream(tooLarge), -1, small.Length));

        Assert.Equal(small, read);
        Assert.Contains("too large", declared.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("too large", streamed.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("413", source, StringComparison.Ordinal);
        Assert.Contains("request_too_large", source, StringComparison.Ordinal);
        Assert.Contains("MaxRequestBodyBytes", source, StringComparison.Ordinal);
    }


    [Fact]
    public void ModelGatewayReturnsActionableLoadAndProxyErrors()
    {
        var source = string.Concat(
            File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayService.cs")),
            File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "ModelGatewayResponseWriter.cs")));
        var workflow = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "GatewayModelLoadWorkflowService.cs"));

        Assert.Contains("\"model_load_failed\"", source, StringComparison.Ordinal);
        Assert.Contains("\"upstream_unavailable\"", source, StringComparison.Ordinal);
        Assert.Contains("Auto-load gateway could not load", source, StringComparison.Ordinal);
        Assert.Contains("direct endpoint", source, StringComparison.Ordinal);
        Assert.Contains("Gateway could not auto-load", workflow, StringComparison.Ordinal);
        Assert.Contains("Install or register a runtime", workflow, StringComparison.Ordinal);
    }


    [Fact]
    public void ModelGatewayResponseContractsExposeStableOpenAiDataAndSafeErrors()
    {
        var root = CreateTempRoot();
        var now = new DateTimeOffset(2026, 7, 31, 10, 0, 0, TimeSpan.Zero);
        var settings = AppSettings.CreateDefault(root) with { Port = 8093 };
        var first = new ModelRecord("z-model", "Zulu", Path.Combine(root, "zulu.gguf"), OwnershipKind.External, "{}", now);
        var second = new ModelRecord("a-model", "Alpha", Path.Combine(root, "alpha.gguf"), OwnershipKind.External, "{}", now.AddMinutes(1));
        var firstProfile = new NamedModelLaunchProfile("default:z-model", first.Id, "Default", ModelLaunchSettings.FromAppSettings(settings), now, true);
        var secondProfile = new NamedModelLaunchProfile("default:a-model", second.Id, "Default", ModelLaunchSettings.FromAppSettings(settings), now.AddMinutes(1), true);
        var running = new LoadedModelSessionSnapshot(
            "session-1",
            first.Id,
            first.Name,
            "runtime-1",
            "CPU runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            Path.Combine(root, "runtime.log"),
            now,
            "",
            123,
            LoadedModelSessionStatus.Running,
            IsRunning: true,
            IsSelected: true);
        var stopped = running with
        {
            SessionId = "session-2",
            ModelId = second.Id,
            ModelName = second.Name,
            Status = LoadedModelSessionStatus.Stopped,
            IsRunning = false
        };

        using var modelsJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            ModelGatewayResponseWriter.ModelsResponse([new(first, firstProfile), new(second, secondProfile)])));
        using var runningJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            ModelGatewayResponseWriter.RunningModelRows([stopped, running])));
        using var errorJson = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(
            ModelGatewayResponseWriter.GatewayError("upstream offline", "upstream_unavailable", "upstream_unavailable")));

        Assert.Equal("list", modelsJson.RootElement.GetProperty("object").GetString());
        var modelRows = modelsJson.RootElement.GetProperty("data").EnumerateArray().ToArray();
        Assert.Equal(["a-model", "z-model"], modelRows.Select(row => row.GetProperty("id").GetString()!).ToArray());
        Assert.Equal("local-llm-console", modelRows[0].GetProperty("owned_by").GetString());
        var runningRow = Assert.Single(runningJson.RootElement.EnumerateArray());
        Assert.Equal(first.Id, runningRow.GetProperty("id").GetString());
        Assert.Equal("http://127.0.0.1:8093/v1", runningRow.GetProperty("endpoint").GetString());
        Assert.Equal("upstream_unavailable", errorJson.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Friendly name -> model-id", ModelGatewayResponseWriter.GatewayClientLoadError(
            new ModelGatewayModelRoute(first with { Id = "model-id" }, firstProfile with { ModelId = "model-id" }),
            "Friendly name",
            new InvalidOperationException("runtime unavailable")), StringComparison.Ordinal);
        Assert.Equal("socket offline", ModelGatewayResponseWriter.InnermostMessage(
            new InvalidOperationException("outer", new IOException("socket offline"))));
        Assert.Equal("http://127.0.0.1:8093/", GatewayUrlReservationService.ListenerPrefixForPort(8093, allowLan: false));
        Assert.Equal("http://+:8093/", GatewayUrlReservationService.ListenerPrefixForPort(8093, allowLan: true));
    }


    [Fact]
    public async Task ModelGatewayListenerAuthenticatesListsLoadsAndProxiesOpenAiRequestsEndToEnd()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var apiKey = new string('g', 32);
        var model = new ModelRecord("qwen-id", "Friendly Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var launchSettings = AppSettings.CreateDefault(root) with { Port = 8123, ModelApiKey = "direct-runtime-key" };
        var runtime = new GatewayIntegrationRuntimeController([model], launchSettings);
        using var upstreamHandler = new GatewayProxyHandler();
        var proxy = new ModelGatewayUpstreamProxy(new HttpClient(upstreamHandler));
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, apiKey, false, ModelGatewaySwapPolicy.KeepLoaded),
            runtime,
            proxy);
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };

        using var unauthorized = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        using var health = await client.GetAsync("health", TestContext.Current.CancellationToken);
        using var models = await client.GetAsync("v1/models", TestContext.Current.CancellationToken);
        using var proxied = await client.PostAsync(
            "v1/chat/completions?trace=1",
            new StringContent("""{"model":"Friendly Qwen","messages":[{"role":"user","content":"hello"}]}""", Encoding.UTF8, "application/json"),
            TestContext.Current.CancellationToken);
        using var running = await client.GetAsync("running", TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.OK, health.StatusCode);
        Assert.Contains("qwen-id", await models.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.Accepted, proxied.StatusCode);
        Assert.Equal("{" + "\"proxied\":true}", await proxied.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Contains("qwen-id", await running.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(1, runtime.EnsureLoadedCount);
        var forwarded = Assert.Single(upstreamHandler.Requests);
        Assert.Equal("/v1/chat/completions?trace=1", forwarded.PathAndQuery);
        Assert.Equal("Bearer direct-runtime-key", forwarded.Authorization);
        Assert.Contains("\"model\":\"Friendly Qwen\"", forwarded.Body, StringComparison.Ordinal);
    }


    [Fact]
    public async Task ModelGatewayListenerReturnsStableClientErrorsForRejectedRequestsEndToEnd()
    {
        var root = CreateTempRoot();
        var port = ReserveLoopbackPort();
        var apiKey = new string('h', 32);
        var model = new ModelRecord("qwen-id", "Friendly Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new GatewayIntegrationRuntimeController([model], AppSettings.CreateDefault(root))
        {
            LoadFailure = new InvalidOperationException("runtime unavailable")
        };
        await using var gateway = new ModelGatewayService(
            new ModelGatewayOptions(true, "local", port, apiKey, true, ModelGatewaySwapPolicy.SingleActive, MaxRequestBodyBytes: 96),
            runtime);
        await gateway.StartAsync(TestContext.Current.CancellationToken);
        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/"), Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

        using var invalid = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"messages\":[]}"), TestContext.Current.CancellationToken);
        using var unknown = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"model\":\"unknown\"}"), TestContext.Current.CancellationToken);
        using var loadFailed = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"model\":\"qwen-id\"}"), TestContext.Current.CancellationToken);
        using var oversized = await client.PostAsync("v1/chat/completions", JsonContent("{" + "\"model\":\"qwen-id\",\"padding\":\"" + new string('x', 120) + "\"}"), TestContext.Current.CancellationToken);
        using var rejectedHostRequest = new HttpRequestMessage(HttpMethod.Get, "health");
        rejectedHostRequest.Headers.Host = $"example.com:{port}";
        using var rejectedHost = await client.SendAsync(rejectedHostRequest, TestContext.Current.CancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("invalid_request_error", await invalid.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("model_not_found", await unknown.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, loadFailed.StatusCode);
        Assert.Contains("model_load_failed", await loadFailed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.RequestEntityTooLarge, oversized.StatusCode);
        Assert.Contains("request_too_large", await oversized.Content.ReadAsStringAsync(TestContext.Current.CancellationToken), StringComparison.Ordinal);
        Assert.Equal(System.Net.HttpStatusCode.Forbidden, rejectedHost.StatusCode);
    }


    [Fact]
    public void GatewayActivityStatusTrackerOwnsGatewayStatusText()
    {
        Loc.LoadLanguage("en");
        var tracker = new GatewayActivityStatusTracker();
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082,
            AutoLoadGatewayPolicy = "singleActive"
        };
        var model = new ModelRecord("model", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var now = DateTimeOffset.UtcNow;

        var disabled = tracker.Build(settings with { AutoLoadGatewayEnabled = false }, gatewayListening: false, now);
        var listening = tracker.Build(settings, gatewayListening: true, now);
        tracker.Start(model, "switching to", now);
        var activity = tracker.Build(settings, gatewayListening: true, now + TimeSpan.FromSeconds(5));
        tracker.SetPhase("loading");
        var loading = tracker.Build(settings, gatewayListening: true, now + TimeSpan.FromSeconds(6));
        tracker.Fail("not enough VRAM");
        var failed = tracker.Build(settings, gatewayListening: true, now + TimeSpan.FromSeconds(7));
        tracker.Complete();
        var completed = tracker.Build(settings, gatewayListening: false, now + TimeSpan.FromSeconds(8));

        Assert.Contains("disabled", disabled.Line, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(GatewayStatusVisualKind.Normal, disabled.VisualKind);
        Assert.Contains("listening at http://127.0.0.1:8082", listening.Line, StringComparison.Ordinal);
        Assert.Contains(Loc.T("Pref.SingleActiveModel"), listening.Line, StringComparison.Ordinal);
        Assert.Equal(GatewayStatusVisualKind.Activity, activity.VisualKind);
        Assert.Contains("switching to Qwen", activity.Line, StringComparison.Ordinal);
        Assert.Contains("loading Qwen", loading.Line, StringComparison.Ordinal);
        Assert.Equal(GatewayStatusVisualKind.Warning, failed.VisualKind);
        Assert.Contains("not enough VRAM", failed.Line, StringComparison.Ordinal);
        Assert.Equal(GatewayStatusVisualKind.Normal, completed.VisualKind);
        Assert.Contains("not listening", completed.Line, StringComparison.Ordinal);
    }


    [Fact]
    public void GatewayActivityStatusControllerOwnsActivityTimer()
    {
        var source = ReadMainWindowSources();
        var state = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.State.cs"));
        var controllerSource = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "Services", "Gateway", "GatewayActivityStatusController.cs"));
        var factorySource = ReadAppServiceFactorySources();
        var timerFactory = new ManualUiTimerFactory();
        var controller = new GatewayActivityStatusController(new GatewayActivityStatusTracker(), timerFactory);
        var settings = AppSettings.CreateDefault(CreateTempRoot()) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var model = new ModelRecord("model", "Qwen", "qwen.gguf", OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var tickCount = 0;

        controller.Start(model, "switching to", DateTimeOffset.UtcNow, () => tickCount++);

        Assert.True(controller.HasActivityTimer);
        Assert.Equal(1, tickCount);
        Assert.Single(timerFactory.Timers);
        Assert.Equal(TimeSpan.FromSeconds(1), timerFactory.Timers[0].Interval);
        Assert.True(timerFactory.Timers[0].Started);
        Assert.Contains("switching to Qwen", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);

        timerFactory.Timers[0].Fire();
        Assert.Equal(2, tickCount);

        controller.SetPhase("loading", () => tickCount++);
        Assert.Equal(3, tickCount);
        Assert.Contains("loading Qwen", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);

        controller.Fail("not enough VRAM", () => tickCount++);
        Assert.False(controller.HasActivityTimer);
        Assert.False(timerFactory.Timers[0].Started);
        Assert.Equal(4, tickCount);
        Assert.Contains("not enough VRAM", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);

        controller.Start(model, "loading", DateTimeOffset.UtcNow, () => tickCount++);
        Assert.True(controller.HasActivityTimer);
        Assert.Equal(2, timerFactory.Timers.Count);
        controller.Complete(() => tickCount++);
        Assert.False(controller.HasActivityTimer);
        Assert.False(timerFactory.Timers[1].Started);
        Assert.Contains("listening", controller.Build(settings, gatewayListening: true, DateTimeOffset.UtcNow).Line, StringComparison.Ordinal);
        Assert.Contains("DispatcherUiTimerFactory", factorySource, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherUiTimerFactory", controllerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("new GatewayActivityStatusController()", state, StringComparison.Ordinal);
        Assert.DoesNotContain("_gatewayActivity = _coreServices.GatewayActivity", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.GatewayActivity.Start(model, phase, DateTimeOffset.Now, UpdateGatewayStatusText)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.GatewayActivity.Complete(UpdateGatewayStatusText)", source, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Ui.GatewayActivity.Fail(message, UpdateGatewayStatusText)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_coreServices.Ui.GatewayActivityTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GatewayActivityTimer_Tick", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StopGatewayActivityTimer", source, StringComparison.Ordinal);
    }


    [Fact]
    public async Task GatewayModelLoadWorkflowStopsConflictingSessionsFixesGatewayPortAndWaitsForReady()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var runtime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var target = new ModelRecord("target", "Target Model", Path.Combine(root, "target.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var loaded = new ModelRecord("loaded", "Loaded Model", Path.Combine(root, "loaded.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var profiled = new ModelRecord("profiled", "Profiled Model", Path.Combine(root, "profiled.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(target);
        await store.UpsertModelAsync(loaded);
        await store.UpsertModelAsync(profiled);
        await store.SaveModelLaunchSettingsAsync(target.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8082 }, runtime.Id));
        await store.SaveModelLaunchSettingsAsync(profiled.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8081 }, runtime.Id));
        await store.SaveModelLaunchSettingsAsync(loaded.Id, ModelLaunchSettings.FromAppSettings(settings with { Port = 8083 }, runtime.Id));
        using var sessions = CreateLoadedModelSessionManager();
        sessions.AttachExisting(runtime, loaded, settings with { Port = 8083 }, Path.Combine(root, "loaded.log"), LlamaRuntimeState.Loaded, "", "loaded-session", DateTimeOffset.UtcNow);
        var runtimeSessions = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var profiles = new ModelLaunchProfileService(store, sessions);
        var workflow = new GatewayModelLoadWorkflowService(store, profiles, runtimeSessions);
        var targetProfile = await profiles.EnsureDefaultAsync(target, settings);
        var phases = new List<string>();
        var stopped = new List<string>();
        AppSettings? startedSettings = null;

        var result = await workflow.EnsureLoadedAsync(new GatewayModelLoadWorkflowRequest(
            target,
            targetProfile,
            ModelGatewaySwapPolicy.SingleActive,
            settings,
            async (model, _) =>
            {
                stopped.Add(model.Id);
                await runtimeSessions.StopModelAsync(model.Id);
            },
            (startedRuntime, model, _, launchSettings, _) =>
            {
                startedSettings = launchSettings;
                sessions.AttachExisting(startedRuntime, model, launchSettings, Path.Combine(root, "target.log"), LlamaRuntimeState.Loading, "", "target-session", DateTimeOffset.UtcNow);
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(true),
            (model, _, _) =>
            {
                sessions.MarkModelLoadedIfRunning(model.Id);
                return Task.FromResult(sessions.SessionForModel(model.Id));
            },
            phases.Add,
            ReadyTimeout: TimeSpan.FromSeconds(1),
            PollInterval: TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        var savedTargetProfile = await store.GetModelLaunchSettingsAsync(target.Id);
        Assert.Equal([loaded.Id], stopped);
        Assert.Equal(8084, savedTargetProfile?.Port);
        Assert.Equal(8084, startedSettings?.Port);
        Assert.Equal(target.Id, result.Session.ModelId);
        Assert.Equal(LoadedModelSessionStatus.Running, result.Session.Status);
        Assert.Contains(phases, phase => phase.Contains("freeing VRAM", StringComparison.OrdinalIgnoreCase));
        Assert.Contains("preparing", phases);
        Assert.Contains("starting", phases);
        Assert.Contains("waiting for API from", phases);
    }

    [Fact]
    public async Task GatewayModelLoadWorkflowRestartsSameModelForRequestedProfile()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082,
            Port = 8084
        };
        var runtime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("qwen", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var defaultProfile = new NamedModelLaunchProfile(
            "default:qwen", model.Id, "Default", ModelLaunchSettings.FromAppSettings(settings with { Port = 8084 }, runtime.Id), DateTimeOffset.UtcNow, true);
        var tunedProfile = new NamedModelLaunchProfile(
            "profile-qwen-128k", model.Id, "128K", ModelLaunchSettings.FromAppSettings(settings with { Port = 8085, ContextSize = 131072 }, runtime.Id), DateTimeOffset.UtcNow, false);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(model);
        await store.SaveNamedModelLaunchProfileAsync(defaultProfile);
        await store.SaveNamedModelLaunchProfileAsync(tunedProfile);
        using var sessions = CreateLoadedModelSessionManager();
        sessions.AttachExisting(
            runtime, model, defaultProfile.Settings.ApplyTo(settings), Path.Combine(root, "default.log"),
            LlamaRuntimeState.Loaded, "", "default-session", DateTimeOffset.UtcNow,
            launchProfileId: defaultProfile.Id, launchProfileName: defaultProfile.Name);
        var runtimeSessions = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var workflow = new GatewayModelLoadWorkflowService(store, new ModelLaunchProfileService(store, sessions), runtimeSessions);
        var stopped = 0;
        NamedModelLaunchProfile? startedProfile = null;
        var phases = new List<string>();

        var result = await workflow.EnsureLoadedAsync(new GatewayModelLoadWorkflowRequest(
            model,
            tunedProfile,
            ModelGatewaySwapPolicy.KeepLoaded,
            settings,
            async (_, _) =>
            {
                stopped++;
                await runtimeSessions.StopModelAsync(model.Id);
            },
            (startedRuntime, startedModel, profile, launchSettings, _) =>
            {
                startedProfile = profile;
                sessions.AttachExisting(
                    startedRuntime, startedModel, launchSettings, Path.Combine(root, "tuned.log"),
                    LlamaRuntimeState.Loading, "", "tuned-session", DateTimeOffset.UtcNow,
                    launchProfileId: profile.Id, launchProfileName: profile.Name);
                return Task.CompletedTask;
            },
            (_, _) => Task.FromResult(true),
            (readyModel, _, _) =>
            {
                sessions.MarkModelLoadedIfRunning(readyModel.Id);
                return Task.FromResult(sessions.SessionForModel(readyModel.Id));
            },
            phases.Add,
            ReadyTimeout: TimeSpan.FromSeconds(1),
            PollInterval: TimeSpan.FromMilliseconds(1)),
            TestContext.Current.CancellationToken);

        Assert.Equal(1, stopped);
        Assert.Equal(tunedProfile.Id, startedProfile?.Id);
        Assert.Equal(tunedProfile.Id, result.Session.LaunchProfileId);
        Assert.Equal(8085, result.LaunchSettings.Port);
        Assert.Contains(phases, phase => phase.Contains("switching from Default to 128K", StringComparison.Ordinal));
    }


    [Fact]
    public async Task GatewayRuntimeApplicationServiceOwnsActivityRefreshAndErrorBoundary()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8084,
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8082
        };
        var runtime = new RuntimeRecord("runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda, Path.Combine(root, "llama-server.exe"), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("target", "Target Model", Path.Combine(root, "target.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        await store.UpsertModelAsync(model);
        await store.SaveModelLaunchSettingsAsync(model.Id, ModelLaunchSettings.FromAppSettings(settings, runtime.Id));
        using var sessions = CreateLoadedModelSessionManager();
        var runtimeSessions = new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs"));
        var application = new GatewayRuntimeApplicationService(new GatewayModelLoadWorkflowService(
            store,
            new ModelLaunchProfileService(store, sessions),
            runtimeSessions));
        var profile = await new ModelLaunchProfileService(store, sessions).EnsureDefaultAsync(model, settings);
        var calls = new List<string>();

        var result = await application.EnsureModelLoadedAsync(
            new GatewayRuntimeLoadApplicationRequest(
                model,
                profile,
                ModelGatewaySwapPolicy.KeepLoaded,
                settings,
                ExistingSession: null),
            new GatewayRuntimeLoadApplicationActions(
                (_, _) => throw new InvalidOperationException("Keep-loaded policy should not stop models."),
                (startedRuntime, runtimeModel, _, launchSettings, _) =>
                {
                    calls.Add($"start:{runtimeModel.Id}:{launchSettings.Port}");
                    sessions.AttachExisting(startedRuntime, runtimeModel, launchSettings, Path.Combine(root, "target.log"), LlamaRuntimeState.Loading, "", "target-session", DateTimeOffset.UtcNow);
                    return Task.CompletedTask;
                },
                (_, _) => Task.FromResult(true),
                (runtimeModel, _, _) =>
                {
                    calls.Add($"ready:{runtimeModel.Id}");
                    sessions.MarkModelLoadedIfRunning(runtimeModel.Id);
                    return Task.FromResult(sessions.SessionForModel(runtimeModel.Id));
                },
                (runtimeModel, phase) => calls.Add($"activity:{phase}:{runtimeModel.Id}"),
                phase => calls.Add($"phase:{phase}"),
                () => calls.Add("complete"),
                message => calls.Add($"fail:{message}"),
                () => { calls.Add("overview"); return Task.CompletedTask; },
                () => { calls.Add("metrics"); return Task.CompletedTask; },
                () => calls.Add("actions"),
                status => calls.Add($"status:{status}")),
            TestContext.Current.CancellationToken);

        Assert.Equal(model.Id, result.ModelId);
        Assert.Contains($"activity:switching to:{model.Id}", calls);
        Assert.Contains("status:Gateway auto-loading Target Model with profile Default...", calls);
        Assert.Contains($"start:{model.Id}:8084", calls);
        Assert.Contains($"ready:{model.Id}", calls);
        Assert.Contains("status:Gateway loaded Target Model at http://127.0.0.1:8084/v1.", calls);
        Assert.Contains("complete", calls);
        Assert.Contains("overview", calls);
        Assert.Contains("metrics", calls);
        Assert.Contains("actions", calls);
        Assert.DoesNotContain(calls, call => call.StartsWith("fail:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ModelGatewayLifecycleApplicationServiceOwnsRestartAndFailureCleanup()
    {
        var root = CreateTempRoot();
        var service = new ModelGatewayLifecycleApplicationService();
        var apiKey = new string('a', 40);
        var settings = AppSettings.CreateDefault(root) with
        {
            AutoLoadGatewayEnabled = true,
            AutoLoadGatewayPort = 8099,
            ModelApiKey = apiKey
        };
        var existing = new FakeModelGatewayHost();
        var created = new List<FakeModelGatewayHost>();
        var calls = new List<string>();
        IModelGatewayHost? currentGateway = existing;

        var result = await service.RestartAsync(
            new ModelGatewayLifecycleRestartRequest(currentGateway, settings),
            Actions(
                gateway => currentGateway = gateway,
                _ => Task.FromResult(settings),
                (_, _) =>
                {
                    var gateway = new FakeModelGatewayHost();
                    created.Add(gateway);
                    return gateway;
                },
                calls),
            TestContext.Current.CancellationToken);

        Assert.True(existing.Disposed);
        var started = Assert.Single(created);
        Assert.True(started.Started);
        Assert.Same(started, currentGateway);
        Assert.True(result.GatewayStarted);
        Assert.Contains("Auto-load gateway listening at http://127.0.0.1:8099/v1.", calls);
        Assert.Contains("status", calls);

        var disabled = settings with { AutoLoadGatewayEnabled = false };
        calls.Clear();
        result = await service.RestartAsync(
            new ModelGatewayLifecycleRestartRequest(currentGateway, disabled),
            Actions(
                gateway => currentGateway = gateway,
                _ => throw new InvalidOperationException("Disabled gateway should not require an API key."),
                (_, _) => throw new InvalidOperationException("Disabled gateway should not create a host."),
                calls),
            TestContext.Current.CancellationToken);

        Assert.True(started.Disposed);
        Assert.Null(currentGateway);
        Assert.False(result.GatewayStarted);
        Assert.DoesNotContain("key", calls);
        Assert.DoesNotContain(calls, call => call.StartsWith("create:", StringComparison.Ordinal));
        Assert.Contains("status", calls);

        var stopOnlyGateway = new FakeModelGatewayHost();
        currentGateway = stopOnlyGateway;
        calls.Clear();
        var stopped = await service.StopAsync(
            new ModelGatewayLifecycleStopRequest(currentGateway),
            new ModelGatewayLifecycleStopActions(
                gateway =>
                {
                    calls.Add(gateway is null ? "gateway:null" : "gateway:set");
                    currentGateway = gateway;
                },
                () => calls.Add("status")));

        Assert.True(stopped);
        Assert.True(stopOnlyGateway.Disposed);
        Assert.Null(currentGateway);
        Assert.Equal(["gateway:null", "status"], calls);

        var failed = new FakeModelGatewayHost(new InvalidOperationException("port busy"));
        calls.Clear();
        var failureResult = await service.RestartAsync(
            new ModelGatewayLifecycleRestartRequest(null, settings),
            Actions(
                gateway => currentGateway = gateway,
                _ => Task.FromResult(settings),
                (_, _) => failed,
                calls),
            TestContext.Current.CancellationToken);

        Assert.False(failureResult.GatewayStarted);
        Assert.True(failed.Disposed);
        Assert.Null(currentGateway);
        Assert.Contains("status", calls);
        Assert.Contains(calls, call => call.Contains("port busy"));

        ModelGatewayLifecycleActions Actions(
            Action<IModelGatewayHost?> setGateway,
            Func<AppSettings, Task<AppSettings>> ensureApiKey,
            Func<ModelGatewayOptions, IModelGatewayRuntimeController, IModelGatewayHost> createGateway,
            List<string> callLog)
            => new(
                gateway =>
                {
                    callLog.Add(gateway is null ? "gateway:null" : "gateway:set");
                    setGateway(gateway);
                },
                settings =>
                {
                    callLog.Add("key");
                    return ensureApiKey(settings);
                },
                () =>
                {
                    callLog.Add("controller");
                    return new FakeModelGatewayRuntimeController();
                },
                (options, controller) =>
                {
                    callLog.Add($"create:{options.Port}:{options.SwapPolicy}");
                    return createGateway(options, controller);
                },
                () => callLog.Add("status"),
                callLog.Add);
    }


    [Fact]
    public void RuntimeEndpointServiceAddsBearerTokenWhenPresent()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { ModelApiKey = "  secret-token  " };

        using var request = RuntimeEndpointService.RuntimeGetRequest("http://127.0.0.1:8081/health", settings);

        Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
        Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
    }


    [Fact]
    public async Task RuntimeEndpointProbeServiceRequiresSuccessForAliveAndSendsAuth()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with
        {
            Port = 8081,
            ModelApiKey = "secret-token"
        };
        var requests = new List<HttpRequestMessage>();
        using var handler = new CapturingHttpHandler(request =>
        {
            requests.Add(CloneRequest(request));
            if (request.RequestUri?.AbsolutePath == "/health")
                return new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError);
            if (request.RequestUri?.AbsolutePath == "/v1/models")
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeEndpointProbeService(http);

        var alive = await service.IsAliveAsync(settings, TestContext.Current.CancellationToken);

        Assert.True(alive);
        Assert.Equal(["/health", "/v1/models"], requests.Select(request => request.RequestUri!.AbsolutePath).ToArray());
        Assert.All(requests, request =>
        {
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("secret-token", request.Headers.Authorization?.Parameter);
        });
    }


    [Fact]
    public async Task RuntimeEndpointProbeServiceTreatsAnyHttpResponseAsResponding()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081 };
        var requests = new List<HttpRequestMessage>();
        using var handler = new CapturingHttpHandler(request =>
        {
            requests.Add(CloneRequest(request));
            return new HttpResponseMessage(System.Net.HttpStatusCode.ServiceUnavailable);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeEndpointProbeService(http);

        var responding = await service.IsRespondingAsync(settings, TestContext.Current.CancellationToken);

        Assert.True(responding);
        Assert.Equal(["/health"], requests.Select(request => request.RequestUri!.AbsolutePath).ToArray());
    }


    [Fact]
    public async Task RuntimeEndpointProbeServiceReadsServedModelsAndFailsClosed()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root) with { Port = 8081 };
        using var handler = new CapturingHttpHandler(request =>
        {
            if (request.RequestUri?.AbsolutePath == "/v1/models")
            {
                return new HttpResponseMessage(System.Net.HttpStatusCode.OK)
                {
                    Content = new StringContent("""{"data":[{"id":"qwen"}],"models":["llama"]}""")
                };
            }

            return new HttpResponseMessage(System.Net.HttpStatusCode.NotFound);
        });
        using var http = new HttpClient(handler);
        var service = new RuntimeEndpointProbeService(http);
        using var failingHandler = new CapturingHttpHandler(_ => throw new HttpRequestException("offline"));
        using var failingHttp = new HttpClient(failingHandler);
        var failingService = new RuntimeEndpointProbeService(failingHttp);

        var served = await service.ServedModelsAsync(settings, TestContext.Current.CancellationToken);
        var failed = await failingService.ServedModelsAsync(settings, TestContext.Current.CancellationToken);

        Assert.Equal(["qwen", "llama"], served);
        Assert.Empty(failed);
    }


    [Fact]
    public void MainWindowDelegatesRuntimeEndpointProbesToService()
    {
        var runtimeSession = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.RuntimeSession.cs"));
        var runtimeLifecycle = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.ModelRuntimeLifecycle.cs"));
        var gateway = File.ReadAllText(FindRepositoryFile("src", "LocalLlmConsole.App", "MainWindow.Gateway.cs"));

        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.ServedModelsAsync", runtimeSession, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync", runtimeSession, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsRespondingAsync", runtimeSession, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsRespondingAsync", runtimeLifecycle, StringComparison.Ordinal);
        Assert.Contains("_coreServices.Runtime.RuntimeEndpointProbe.IsAliveAsync", gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("new HttpClient", runtimeSession, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEndpointAliveAsync", runtimeSession + runtimeLifecycle + gateway, StringComparison.Ordinal);
        Assert.DoesNotContain("RuntimeEndpointRespondingAsync", runtimeSession + runtimeLifecycle + gateway, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeEndpointServiceParsesServedModelsAndMatchesRegistrations()
    {
        const string json = """
        {
          "data": [
            { "id": "registered-id" },
            { "model": "D:\\models\\Qwen3-8B.gguf" },
            { "name": "Friendly Qwen" }
          ],
          "models": [ "plain-model" ]
        }
        """;
        var now = DateTimeOffset.UtcNow;
        var model = new ModelRecord("registered-id", "Friendly Qwen", @"D:\models\Qwen3-8B.gguf", OwnershipKind.External, "{}", now);

        var served = RuntimeEndpointService.ExtractServedModelIds(json).ToArray();

        Assert.Equal(["registered-id", @"D:\models\Qwen3-8B.gguf", "Friendly Qwen", "plain-model"], served);
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, "registered-id"));
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, @"D:\other\Qwen3-8B.gguf"));
        Assert.True(RuntimeEndpointService.ServedModelMatches(model, "Friendly Qwen"));
        Assert.False(RuntimeEndpointService.ServedModelMatches(model, "other-model"));
    }


    private static HttpRequestMessage CloneRequest(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        return clone;
    }

    private static LoadedModelSessionSnapshot RuntimeMetricSession(string root, AppSettings settings)
        => RuntimeSession(root, settings, LoadedModelSessionStatus.Running, isRunning: true);

    private static LoadedModelSessionSnapshot RuntimeSession(
        string root,
        AppSettings settings,
        LoadedModelSessionStatus status,
        bool isRunning)
        => new(
            "session-1",
            "model-1",
            "Qwen",
            "runtime-1",
            "Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            settings,
            Path.Combine(root, "runtime.log"),
            DateTimeOffset.UtcNow,
            "",
            0,
            status,
            IsRunning: isRunning,
            IsSelected: true);

    private static StringContent JsonContent(string json)
        => new(json, Encoding.UTF8, "application/json");

    private static int ReserveLoopbackPort()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    private sealed class GatewayIntegrationRuntimeController(
        IReadOnlyList<ModelRecord> models,
        AppSettings launchSettings) : IModelGatewayRuntimeController
    {
        private readonly List<LoadedModelSessionSnapshot> _sessions = [];

        public Exception? LoadFailure { get; init; }

        public int EnsureLoadedCount { get; private set; }

        public Task<IReadOnlyList<ModelGatewayModelRoute>> ListModelsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<ModelGatewayModelRoute>>(models.Select(model => new ModelGatewayModelRoute(
                model,
                new NamedModelLaunchProfile(
                    $"default:{model.Id}",
                    model.Id,
                    "Default",
                    ModelLaunchSettings.FromAppSettings(launchSettings),
                    model.UpdatedAt,
                    true))).ToArray());

        public Task<IReadOnlyList<LoadedModelSessionSnapshot>> RunningSessionsAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<LoadedModelSessionSnapshot>>(_sessions.ToArray());

        public Task<LoadedModelSessionSnapshot> EnsureModelLoadedAsync(
            ModelGatewayModelRoute route,
            ModelGatewaySwapPolicy policy,
            CancellationToken cancellationToken = default)
        {
            EnsureLoadedCount++;
            if (LoadFailure is not null) throw LoadFailure;
            var model = route.Model;
            var session = new LoadedModelSessionSnapshot(
                "gateway-session",
                model.Id,
                model.Name,
                "runtime-id",
                "CPU runtime",
                RuntimeMode.Native,
                RuntimeBackend.Cpu,
                launchSettings,
                "runtime.log",
                DateTimeOffset.UtcNow,
                "",
                123,
                LoadedModelSessionStatus.Running,
                IsRunning: true,
                IsSelected: true,
                LaunchProfileId: route.Profile.Id,
                LaunchProfileName: route.Profile.Name);
            _sessions.Add(session);
            return Task.FromResult(session);
        }
    }

    private sealed class GatewayProxyHandler : HttpMessageHandler
    {
        public List<(string PathAndQuery, string Authorization, string Body)> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((
                request.RequestUri?.PathAndQuery ?? "",
                request.Headers.Authorization?.ToString() ?? "",
                request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken)));
            return new HttpResponseMessage(System.Net.HttpStatusCode.Accepted)
            {
                Content = new StringContent("{" + "\"proxied\":true}", Encoding.UTF8, "application/json")
            };
        }
    }


}
