using System.Diagnostics;
using System.Text;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class GatewaySchedulingAndHardwareTests : ManagerRegressionTestBase
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
    public async Task GatewayRequestGateReleasesIdleModelEntriesAfterChurn()
    {
        var gates = new ModelGatewayRequestGate();

        for (var index = 0; index < 250; index++)
        {
            using var lease = await gates.EnterAsync(
                $"model-{index}",
                "default",
                TestContext.Current.CancellationToken);
        }

        Assert.Equal(0, gates.TrackedModelCount);
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
        var fixture = File.ReadAllText(FindRepositoryFile("tests", "fixtures", "upstream", "nvidia-smi-valid.csv")).Trim();
        var malformed = File.ReadAllText(FindRepositoryFile("tests", "fixtures", "upstream", "nvidia-smi-malformed.csv")).Trim();
        var formatted = GpuStatusService.FormatNvidiaSmiCsvLine(fixture);

        Assert.Equal("GPU 0: Example GPU | 76% load | 62 °C | 12.0/24.0 GiB VRAM | 205.4 W | 1695 MHz core", formatted);
        Assert.Equal("", GpuStatusService.FormatNvidiaSmiCsvLine(malformed));
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
        var json = File.ReadAllText(FindRepositoryFile("tests", "fixtures", "upstream", "windows-gpu-valid.json"));

        var formatted = GpuStatusService.FormatWindowsGpuStatusJson(json);

        Assert.Equal(["GPU 0: Example Graphics Adapter | 53.4% load | 8.0/24.0 GiB VRAM"], formatted);
        Assert.Empty(GpuStatusService.FormatWindowsGpuStatusJson("[{malformed"));
        Assert.Equal(
            ["GPU 0: Intel(R) Graphics | 12% load | 1.5 GiB VRAM used"],
            GpuStatusService.FormatWindowsGpuStatusJson("[{\"Index\":0,\"Name\":\"Intel(R) Graphics\",\"Utilization\":12,\"MemoryUsedBytes\":1610612736}]"));
    }

    [Fact]
    public void GpuStatusServiceFormatsWindowsCpuTemperatureJson()
    {
        Assert.Equal("CPU: 57.2C", GpuStatusService.FormatWindowsCpuTemperatureJson("{\"TemperatureCelsius\":57.2}"));
        Assert.Equal("CPU: 42C", GpuStatusService.FormatWindowsCpuTemperatureJson("[{\"CurrentTemperature\":3151.5},{\"TemperatureCelsius\":36.4}]"));
        Assert.Equal("", GpuStatusService.FormatWindowsCpuTemperatureJson("{}"));
        Assert.Equal(
            $"CPU: AMD Ryzen 9 7950X{Environment.NewLine}Telemetry: 18.5% load | 16C/32T | 57.2 °C thermal | 5200 MHz core",
            GpuStatusService.FormatWindowsCpuStatusJson("{\"Name\":\"AMD Ryzen 9 7950X 16-Core Processor\",\"Utilization\":18.5,\"PhysicalCores\":16,\"LogicalProcessors\":32,\"TemperatureCelsius\":57.2,\"CurrentClockMHz\":5200}"));
        Assert.Equal(
            "RAM: 12.0/32.0 GiB | 37.5% | 6000 MHz",
            GpuStatusService.FormatWindowsMemoryStatusJson("{\"UsedBytes\":12884901888,\"TotalBytes\":34359738368,\"UsagePercent\":37.5,\"ClockMHz\":6000}"));
    }


    [Fact]
    public async Task GpuStatusProbeServiceRunsThroughProcessRunner()
    {
        var commands = new List<string>();
        var runner = new ScriptedProcessRunner(psi =>
        {
            commands.Add($"{Path.GetFileName(psi.FileName)} {string.Join(" ", psi.ArgumentList)}");
            if (string.Equals(Path.GetFileName(psi.FileName), "powershell.exe", StringComparison.OrdinalIgnoreCase))
            {
                var script = DecodePowerShellCommand(psi);
                if (script.Contains("Win32_OperatingSystem", StringComparison.Ordinal))
                    return new ProcessRunResult(0, "{\"UsedBytes\":12884901888,\"TotalBytes\":34359738368,\"UsagePercent\":37.5}", "");
                return script.Contains("MSAcpi_ThermalZoneTemperature", StringComparison.Ordinal)
                    ? new ProcessRunResult(0, "{\"TemperatureCelsius\":57.2}", "")
                    : new ProcessRunResult(0, "[{\"Index\":0,\"Name\":\"AMD Radeon RX 7900 XTX\",\"Utilization\":53.4,\"MemoryUsedBytes\":8589934592,\"MemoryTotalBytes\":25769803776}]", "");
            }
            if (psi.ArgumentList.Contains("--query-gpu=memory.free,memory.total"))
                return new ProcessRunResult(0, "1024, 24576\n8192, 24576", "");
            if (psi.ArgumentList.Contains("--query-gpu=index,name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,clocks.gr"))
                return new ProcessRunResult(0, "0, NVIDIA RTX, 76, 62, 12288, 24576, 205.4, 1695", "");
            return new ProcessRunResult(0, "[level_zero:gpu][level_zero:0] Intel(R) Arc(TM) A770 Graphics", "");
        });
        var service = new GpuStatusProbeService(runner, () => "sycl-ls.exe", () => "nvidia-smi.exe", () => "powershell.exe");

        var memory = await service.MemoryAsync(TestContext.Current.CancellationToken);
        var summary = await service.SummaryAsync(TestContext.Current.CancellationToken);
        var windows = await service.WindowsSummaryAsync(TestContext.Current.CancellationToken);
        var cpu = await service.CpuTemperatureAsync(TestContext.Current.CancellationToken);
        var cpuSummary = await service.CpuSummaryAsync(TestContext.Current.CancellationToken);
        var memorySummary = await service.SystemMemorySummaryAsync(TestContext.Current.CancellationToken);
        var sycl = await service.WindowsIntelArcSummaryAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(memory);
        Assert.Equal(9, memory.FreeGiB);
        Assert.Equal(48, memory.TotalGiB);
        Assert.Equal("GPU 0: NVIDIA RTX | 76% load | 62 °C | 12.0/24.0 GiB VRAM | 205.4 W | 1695 MHz core", summary);
        Assert.Equal("GPU 0: AMD Radeon RX 7900 XTX | 53.4% load | 8.0/24.0 GiB VRAM", windows);
        Assert.Equal("CPU: 57.2C", cpu);
        Assert.Equal("Telemetry: 57.2 °C thermal", cpuSummary);
        Assert.Equal("RAM: 12.0/32.0 GiB | 37.5%", memorySummary);
        Assert.Equal("Intel(R) Arc(TM) A770 Graphics", sycl);
        Assert.Contains(commands, command => command.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)
            && command.Contains("-EncodedCommand", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("--query-gpu=memory.free,memory.total", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.Contains("--query-gpu=index,name,utilization.gpu,temperature.gpu,memory.used,memory.total,power.draw,clocks.gr", StringComparison.Ordinal));
        Assert.Contains(commands, command => command.StartsWith("sycl-ls", StringComparison.OrdinalIgnoreCase));

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


}
