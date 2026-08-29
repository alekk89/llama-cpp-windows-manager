using System.Text.Json.Nodes;
using System.Net.Http.Headers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlApiTests : ManagerRegressionTestBase
{
    [Fact]
    public void ControlJsonPatchSupportsEveryTypedLaunchSettingWithoutLeakingSecrets()
    {
        var defaults = AppSettings.CreateDefault(CreateTempRoot());
        var profile = ModelLaunchSettings.FromAppSettings(defaults, "cuda-runtime");
        var patched = ControlJsonPatch.Apply(profile, new JsonObject
        {
            ["contextSize"] = 65536,
            ["gpuLayers"] = 123,
            ["visionProjectorPath"] = @"D:\Models\mmproj.gguf",
            ["mtpHeadPath"] = @"D:\Models\mtp-head.gguf",
            ["gpuDevices"] = "CUDA0,CUDA1",
            ["gpuSplit"] = "3,1",
            ["customParameters"] = "--n-cpu-moe 999"
        });

        Assert.Equal(65536, patched.ContextSize);
        Assert.Equal(123, patched.GpuLayers);
        Assert.Equal(@"D:\Models\mmproj.gguf", patched.VisionProjectorPath);
        Assert.Equal(@"D:\Models\mtp-head.gguf", patched.MtpHeadPath);
        Assert.Equal("CUDA0,CUDA1", patched.GpuDevices);
        Assert.Equal("3,1", patched.GpuSplit);
        Assert.Equal("--n-cpu-moe 999", patched.CustomParameters);
        Assert.Throws<InvalidOperationException>(() => ControlJsonPatch.Apply(profile, new JsonObject { ["notASetting"] = true }));

        var redacted = ControlJsonPatch.RedactedAppSettings(defaults with
        {
            ModelApiKey = new string('a', 32),
            ModelApiKeyBackup = new string('b', 32)
        });
        Assert.Equal("[configured]", redacted["modelApiKey"]?.ToString());
        Assert.Equal("[configured]", redacted["modelApiKeyBackup"]?.ToString());
        Assert.Null(redacted["workspaceRoot"]);
        Assert.DoesNotContain(new string('a', 32), redacted.ToJsonString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ControlProfileScopeRejectsCrossModelProfileIds()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var firstModel = new ModelRecord("first", "First", Path.Combine(root, "first.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var secondModel = new ModelRecord("second", "Second", Path.Combine(root, "second.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var secondProfile = new NamedModelLaunchProfile(
            "shared-profile-id",
            secondModel.Id,
            "Second profile",
            ModelLaunchSettings.FromAppSettings(settings, "runtime"),
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidOperationException>(() =>
            ControlProfileScope.EnsureCreateIdAvailable(secondProfile, firstModel, secondProfile.Id));
        Assert.Throws<KeyNotFoundException>(() =>
            ControlProfileScope.ResolveOwned([secondProfile], firstModel, secondProfile.Id));
    }

    [Fact]
    public void ControlSelfIdentificationUnderstandsGatewayProfileRouteIds()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var model = new ModelRecord("qwen", "Qwen", Path.Combine(root, "qwen.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var defaultProfile = new NamedModelLaunchProfile(
            "default:qwen",
            model.Id,
            "Default",
            ModelLaunchSettings.FromAppSettings(settings, "runtime"),
            DateTimeOffset.UtcNow,
            true);
        var tunedProfile = new NamedModelLaunchProfile(
            "Profile 128K",
            model.Id,
            "128K",
            ModelLaunchSettings.FromAppSettings(settings with { ContextSize = 131072 }, "runtime"),
            DateTimeOffset.UtcNow);
        var otherProfile = tunedProfile with { Id = "Profile 32K", Name = "32K" };
        var route = ModelGatewayRouteId.EnsureUnique([
            new ModelGatewayModelRoute(model, defaultProfile),
            new ModelGatewayModelRoute(model, tunedProfile),
            new ModelGatewayModelRoute(model, otherProfile)
        ]).Single(candidate => candidate.Profile.Id == tunedProfile.Id);
        var session = new LoadedModelSessionSnapshot(
            "session",
            model.Id,
            model.Name,
            "runtime",
            "Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cuda,
            settings with { Port = 8090 },
            Path.Combine(root, "runtime.log"),
            DateTimeOffset.UtcNow,
            "",
            123,
            LoadedModelSessionStatus.Running,
            true,
            true,
            LaunchProfileId: tunedProfile.Id,
            LaunchProfileName: tunedProfile.Name);

        Assert.True(ControlSelfIdentification.MatchesModelHint(
            session,
            [model],
            [defaultProfile, tunedProfile, otherProfile],
            $"local-llm-console/{route.Id}"));
        Assert.False(ControlSelfIdentification.MatchesModelHint(
            session,
            [model],
            [defaultProfile, tunedProfile, otherProfile],
            "qwen--profile-32k"));
    }

    [Fact]
    public void BareSaveProfileFlagDoesNotBecomeTheProfileNameTrue()
    {
        var request = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("load", "qwen", "--save-profile");

        Assert.Equal("POST", request.Method);
        Assert.True(request.Body?["saveProfile"]?.GetValue<bool>());
        Assert.Equal("", request.Body?["saveProfileName"]?.GetValue<string>());
    }

    [Fact]
    public void SessionsInspectTargetsTheAuthenticatedManagerInspectionRoute()
    {
        var request = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("sessions", "inspect", "session-qwen");

        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/sessions/session-qwen/inspect", request.Path);
        Assert.Null(request.Body);
    }

    [Fact]
    public void GatewayInspectTargetsTheAuthenticatedManagerInspectionRoute()
    {
        var request = LocalLlmConsole.ControlCli.ControlCliRequestFactory.BuildForTests("gateway", "inspect");

        Assert.Equal("GET", request.Method);
        Assert.Equal("/api/v1/gateway/inspect", request.Path);
        Assert.Null(request.Body);
    }

    [Fact]
    public async Task RawRequestCannotBypassCurrentModelStopProtection()
    {
        var root = CreateTempRoot();
        var connectionPath = Path.Combine(root, "control.json");
        var port = FreeTcpPort();
        File.WriteAllText(connectionPath, JsonSerializer.Serialize(new
        {
            version = 1,
            processId = Environment.ProcessId,
            baseUrl = $"http://127.0.0.1:{port}",
            protectedToken = "test-token",
            workspaceRoot = root,
            startedAt = DateTimeOffset.UtcNow
        }));

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var received = new List<string>();
        var server = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening && received.Count < 2)
                {
                    var context = await listener.GetContextAsync();
                    received.Add($"{context.Request.HttpMethod} {context.Request.Url?.AbsolutePath}");
                    var json = context.Request.Url?.AbsolutePath switch
                    {
                        "/api/v1/self" => "{\"ok\":true,\"identified\":true,\"session\":{\"modelId\":\"self-model\",\"runtimeId\":\"runtime\",\"mode\":\"Native\"}}",
                        "/api/v1/models/self-model" => "{\"ok\":true,\"model\":{\"id\":\"self-model\"}}",
                        _ => "{\"ok\":true}"
                    };
                    var bytes = Encoding.UTF8.GetBytes(json);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) when (!listener.IsListening)
            {
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            var exitCode = await LocalLlmConsole.ControlCli.Program.Main([
                "request",
                "POST",
                "/api/v1/models/self-model/%75nload",
                "--connection",
                connectionPath,
                "--compact"
            ]);

            Assert.Equal(1, exitCode);
            Assert.Equal(["GET /api/v1/self", "GET /api/v1/models/self-model"], received);
        }
        finally
        {
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        static int FreeTcpPort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }
    }

    [Fact]
    public async Task LoadWithRestartCannotBypassCurrentModelStopProtection()
    {
        var result = await RunGuardedCliRequestAsync(
            ["load", "self-model", "--restart"],
            _ => "{\"ok\":true,\"identified\":true,\"session\":{\"modelId\":\"self-model\",\"runtimeId\":\"runtime\",\"mode\":\"Native\"}}",
            _ => "{\"ok\":true,\"model\":{\"id\":\"self-model\"}}",
            expectedRequestCount: 2);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["GET /api/v1/self", "GET /api/v1/models/self-model"], result.Requests);
    }

    [Fact]
    public async Task EncodedRawOperationCannotBypassCurrentSessionProtection()
    {
        var result = await RunGuardedCliRequestAsync(
            ["request", "POST", "/api/v1/operations/app%2Eshutdown", "--body", "{\"confirm\":true}"],
            _ => "{\"ok\":true,\"identified\":true,\"session\":{\"modelId\":\"self-model\",\"runtimeId\":\"runtime\",\"mode\":\"Native\"}}",
            _ => "{\"ok\":true}",
            expectedRequestCount: 1);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["GET /api/v1/self"], result.Requests);
    }

    [Fact]
    public async Task DestructiveCommandFailsClosedWhenSelfIdentityIsAmbiguous()
    {
        var result = await RunGuardedCliRequestAsync(
            ["unload", "other-model"],
            _ => "{\"ok\":true,\"identified\":false,\"confidence\":\"ambiguous\",\"candidates\":[{\"modelId\":\"first\"},{\"modelId\":\"second\"}]}",
            _ => "{\"ok\":true}",
            expectedRequestCount: 1);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["GET /api/v1/self"], result.Requests);
    }

    [Fact]
    public async Task DestructiveCommandTreatsSingleSessionInferenceAsUnverified()
    {
        var result = await RunGuardedCliRequestAsync(
            ["unload", "candidate-model"],
            _ => "{\"ok\":true,\"identified\":true,\"confidence\":\"inferred-single-running-session\",\"session\":{\"modelId\":\"candidate-model\"}}",
            _ => "{\"ok\":true}",
            expectedRequestCount: 1);

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["GET /api/v1/self"], result.Requests);
    }

    [Fact]
    public async Task DestructiveCommandProceedsWhenNoManagedSessionIsRunning()
    {
        var result = await RunGuardedCliRequestAsync(
            ["unload", "stopped-model"],
            _ => "{\"ok\":true,\"identified\":false,\"confidence\":\"ambiguous\",\"candidates\":[]}",
            _ => "{\"ok\":true}",
            expectedRequestCount: 2);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["GET /api/v1/self", "POST /api/v1/models/stopped-model/unload"], result.Requests);
    }

    private static async Task<(int ExitCode, IReadOnlyList<string> Requests)> RunGuardedCliRequestAsync(
        string[] command,
        Func<string, string> selfResponse,
        Func<string, string> otherResponse,
        int expectedRequestCount)
    {
        var root = CreateTempRoot();
        var connectionPath = Path.Combine(root, "control.json");
        var port = FreeTcpPort();
        File.WriteAllText(connectionPath, JsonSerializer.Serialize(new
        {
            version = 1,
            processId = Environment.ProcessId,
            baseUrl = $"http://127.0.0.1:{port}",
            protectedToken = "test-token",
            workspaceRoot = root,
            startedAt = DateTimeOffset.UtcNow
        }));

        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var received = new List<string>();
        var server = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening && received.Count < expectedRequestCount)
                {
                    var context = await listener.GetContextAsync();
                    var path = context.Request.Url?.AbsolutePath ?? "";
                    received.Add($"{context.Request.HttpMethod} {path}");
                    var json = path == "/api/v1/self" ? selfResponse(path) : otherResponse(path);
                    var bytes = Encoding.UTF8.GetBytes(json);
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "application/json";
                    context.Response.ContentLength64 = bytes.Length;
                    await context.Response.OutputStream.WriteAsync(bytes);
                    context.Response.Close();
                }
            }
            catch (HttpListenerException) when (!listener.IsListening)
            {
            }
        }, TestContext.Current.CancellationToken);

        try
        {
            var args = command.Concat(["--connection", connectionPath, "--compact"]).ToArray();
            var exitCode = await LocalLlmConsole.ControlCli.Program.Main(args);
            return (exitCode, received);
        }
        finally
        {
            listener.Stop();
            await server.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        }

        static int FreeTcpPort()
        {
            var tcp = new TcpListener(IPAddress.Loopback, 0);
            tcp.Start();
            var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
            tcp.Stop();
            return port;
        }
    }

    [Fact]
    public void CompanionDiscoveryReturnsAllEligibleVisionAndDraftHeads()
    {
        var root = CreateTempRoot();
        var model = Path.Combine(root, "model-q4.gguf");
        var visionA = Path.Combine(root, "mmproj-f16.gguf");
        var visionB = Path.Combine(root, "vision-head-q8.gguf");
        var draftA = Path.Combine(root, "mtp-model-assistant.gguf");
        var draftB = Path.Combine(root, "draft-helper.gguf");
        foreach (var path in new[] { model, visionA, visionB, draftA, draftB }) File.WriteAllBytes(path, []);

        var vision = ModelCatalogService.FindVisionProjectors(model);
        var draft = ModelCatalogService.FindDraftModels(model);

        Assert.Equal(2, vision.Count);
        Assert.Contains(visionA, vision, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(visionB, vision, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, draft.Count);
        Assert.Contains(draftA, draft, StringComparer.OrdinalIgnoreCase);
        Assert.Contains(draftB, draft, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(visionA, ModelCatalogService.FindVisionProjector(model));
    }

}
