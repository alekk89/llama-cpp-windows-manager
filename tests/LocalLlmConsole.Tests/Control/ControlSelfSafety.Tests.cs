using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlSelfSafetyTests : ManagerRegressionTestBase
{
    private const string SelfByModel =
        "{\"ok\":true,\"identified\":true,\"confidence\":\"exact\",\"matchedBy\":\"model\",\"session\":{\"modelId\":\"self-model\",\"runtimeId\":\"runtime\",\"mode\":\"Native\"}}";
    private const string SelfByEndpoint =
        "{\"ok\":true,\"identified\":true,\"confidence\":\"exact\",\"matchedBy\":\"endpoint\",\"session\":{\"modelId\":\"self-model\",\"runtimeId\":\"runtime\",\"mode\":\"Native\"}}";

    [Theory]
    [InlineData("/api/v1/x/../models/self-model/unload")]
    [InlineData("/api/v1/x/%2E%2E/models/self-model/unload")]
    [InlineData("/api/v1/models/./self-model/unload")]
    [InlineData("/api/v1/models\\self-model/unload")]
    [InlineData("/api/v1/models%5Cself-model/unload")]
    public async Task RawRequestWithNonCanonicalPathIsRejectedBeforeAnyRequest(string path)
    {
        var result = await RunControlCliAsync(["request", "POST", path], Respond(SelfByModel));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Requests);
    }

    [Theory]
    [InlineData("/api/v1/models/self-model/load", "{\"Restart\":true}")]
    [InlineData("/api/v1/models/other-model/load", "{\"UNLOADOTHERS\":true}")]
    [InlineData("/api/v1/models/other-model/load", "{\"unloadOthers\":false,\"UnloadOthers\":true}")]
    public async Task RawModelFlagsAreGuardedCaseInsensitively(string path, string body)
    {
        var result = await RunControlCliAsync(["request", "POST", path, "--body", body], Respond(SelfByModel));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal("GET /api/v1/self", result.Requests[0]);
        Assert.DoesNotContain(result.Requests, request => request.StartsWith("POST", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("/api/v1/operations/app.shutdown", "{\"DryRun\":true,\"confirm\":true}")]
    [InlineData("/api/v1/operations/app.shutdown", "{\"dryRun\":true,\"DRYRUN\":false,\"confirm\":true}")]
    [InlineData("/api/v1/operations/wsl.setup", "{\"Action\":\"DeleteDistro\",\"confirm\":true}")]
    [InlineData("/api/v1/operations/runtime.delete", "{\"Runtime\":\"runtime\",\"confirm\":true}")]
    [InlineData("/api/v1/benchmarks/run", "{\"Plan\":{\"StopActiveSessions\":true},\"confirm\":true}")]
    public async Task RawOperationAndBenchmarkFieldsAreGuardedCaseInsensitively(string path, string body)
    {
        var self = "{\"ok\":true,\"identified\":true,\"confidence\":\"exact\",\"matchedBy\":\"model\",\"session\":{\"modelId\":\"self-model\",\"runtimeId\":\"runtime\",\"mode\":\"Wsl\"}}";
        var result = await RunControlCliAsync(["request", "POST", path, "--body", body], Respond(self));

        Assert.Equal(1, result.ExitCode);
        Assert.Equal(["GET /api/v1/self"], result.Requests);
    }

    [Theory]
    [InlineData("gateway.stop")]
    [InlineData("gateway.restart")]
    public async Task GatewayStopOrRestartRefusesSessionThatMayUseTheGateway(string operation)
    {
        var refused = await RunControlCliAsync(["operations", "run", operation, "--confirm"], Respond(SelfByModel));
        Assert.Equal(1, refused.ExitCode);
        Assert.Equal(["GET /api/v1/self"], refused.Requests);

        var ambiguous = await RunControlCliAsync(["operations", "run", operation, "--confirm"],
            Respond("{\"ok\":true,\"identified\":false,\"confidence\":\"ambiguous\",\"candidates\":[{\"modelId\":\"a\"},{\"modelId\":\"b\"}]}"));
        Assert.Equal(1, ambiguous.ExitCode);
        Assert.Equal(["GET /api/v1/self"], ambiguous.Requests);

        var authorized = await RunControlCliAsync(["operations", "run", operation, "--confirm", "--allow-self-stop"], Respond(SelfByModel));
        Assert.Equal(0, authorized.ExitCode);
        Assert.Equal([$"POST /api/v1/operations/{operation}"], authorized.Requests);
    }

    [Fact]
    public async Task GatewayOperationsProceedForDirectEndpointSessionsAndDryRuns()
    {
        var direct = await RunControlCliAsync(["operations", "run", "gateway.restart", "--confirm"], Respond(SelfByEndpoint));
        Assert.Equal(0, direct.ExitCode);
        Assert.Equal(["GET /api/v1/self", "POST /api/v1/operations/gateway.restart"], direct.Requests);

        var dryRun = await RunControlCliAsync(["operations", "run", "gateway.stop", "--dry-run"], Respond(SelfByModel));
        Assert.Equal(0, dryRun.ExitCode);
        Assert.Equal(["POST /api/v1/operations/gateway.stop"], dryRun.Requests);
    }

    [Fact]
    public void GatewayStopAndRestartRequireConfirmation()
    {
        Assert.True(ControlOperationCatalog.Resolve("gateway.stop").RequiresConfirmation);
        Assert.True(ControlOperationCatalog.Resolve("gateway.restart").RequiresConfirmation);
    }

    [Theory]
    [InlineData("/api/v1/operations/gateway.stop", "{\"confirm\":true}", "model", true)]
    [InlineData("/api/v1/operations/gateway.restart", "{\"confirm\":true}", "sessionId", true)]
    [InlineData("/api/v1/operations/gateway.restart", "{\"confirm\":true}", "endpoint", false)]
    [InlineData("/api/v1/operations/gateway.stop", "{\"dryRun\":true}", "model", false)]
    [InlineData("/api/v1/operations/app.shutdown", "{\"DryRun\":true,\"confirm\":true}", "model", true)]
    [InlineData("/api/v1/models/other-model/load", "{\"UnloadOthers\":true}", "model", true)]
    [InlineData("/api/v1/models/self-model/load", "{\"RESTART\":true}", "model", true)]
    [InlineData("/api/v1/models/other-model/load", "{\"Restart\":true}", "model", false)]
    [InlineData("/api/v1/benchmarks/run", "{\"plan\":{\"StopActiveSessions\":true}}", "model", true)]
    [InlineData("/api/v1/benchmarks/run", "{\"plan\":{\"StopActiveSessions\":true},\"dryRun\":true}", "model", false)]
    [InlineData("/api/v1/x/../models/other-model/load", "{}", "model", true)]
    [InlineData("/api/v1/x/%2E%2E/models/other-model/load", "{}", "model", true)]
    public async Task ServerAdmissionAppliesTheSameSelfStopRules(string path, string body, string matchedBy, bool refused)
    {
        var admission = new ControlRequestAdmissionService((identifier, _) => Task.FromResult(identifier));
        var identity = new ControlAdmissionIdentity(true, "session-self", "self-model", "Self", "runtime", "Runtime",
            RuntimeMode.Native, "exact", matchedBy);
        var request = new LocalControlRequest("POST", path, new Dictionary<string, string>(),
            JsonNode.Parse(body)!.AsObject(), new Dictionary<string, string>());

        var attempt = admission.EnsureAllowedAsync(request, ControlAdmissionContext.Visual(identity), TestContext.Current.CancellationToken);

        if (refused) await Assert.ThrowsAsync<ControlAdmissionException>(() => attempt);
        else await attempt;
        await admission.EnsureAllowedAsync(request, ControlAdmissionContext.Visual(identity, allowSelfStop: true), TestContext.Current.CancellationToken);
    }

    private const string NotManaged =
        "{\"ok\":true,\"identified\":false,\"confidence\":\"not-managed\",\"servedByManager\":false,\"matchedBy\":\"endpoint\",\"candidates\":[]}";

    [Theory]
    [InlineData("unload", "self-model")]
    [InlineData("restart", "self-model")]
    public async Task StopsProceedWhenTheManagerDoesNotServeTheAgent(string command, string model)
    {
        var result = await RunControlCliAsync([command, model], Respond(NotManaged));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("GET /api/v1/self", result.Requests[0]);
        Assert.Contains(result.Requests, request => request.StartsWith("POST /api/v1/models/self-model/", StringComparison.Ordinal));
    }

    [Fact]
    public async Task GatewayStopProceedsWhenTheManagerDoesNotServeTheAgent()
    {
        var result = await RunControlCliAsync(["operations", "run", "gateway.stop", "--confirm"], Respond(NotManaged));

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(["GET /api/v1/self", "POST /api/v1/operations/gateway.stop"], result.Requests);
    }

    [Theory]
    // Claude Code reports its own endpoint; a global OPENAI_BASE_URL meant for other tools is ignored.
    [InlineData("CLAUDECODE=1;OPENAI_BASE_URL=http://localhost:8081/v1;OPENAI_MODEL=local", "", "endpoint=https%3A%2F%2Fapi.anthropic.com")]
    [InlineData("CLAUDECODE=1;ANTHROPIC_BASE_URL=http://localhost:8082/v1", "", "endpoint=http%3A%2F%2Flocalhost%3A8082%2Fv1")]
    [InlineData("CLAUDECODE=1;CLAUDE_CODE_USE_BEDROCK=1", "", "endpoint=https%3A%2F%2Fbedrock-runtime.amazonaws.com")]
    [InlineData("OPENAI_BASE_URL=http://localhost:8081/v1;OPENAI_MODEL=local", "", "model=local&endpoint=http%3A%2F%2Flocalhost%3A8081%2Fv1")]
    [InlineData("CLAUDECODE=1;LLWM_ENDPOINT=http://127.0.0.1:8088/v1", "", "endpoint=http%3A%2F%2F127.0.0.1%3A8088%2Fv1")]
    [InlineData("CLAUDECODE=1", "--endpoint http://127.0.0.1:9000/v1", "endpoint=http%3A%2F%2F127.0.0.1%3A9000%2Fv1")]
    public void SelfHintsUseTheCallingAgentsOwnEndpoint(string environment, string flags, string expectedQuery)
    {
        var env = environment.Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split('=', 2))
            .ToDictionary(pair => pair[0], pair => pair[1]);
        var args = new LocalLlmConsole.ControlCli.Arguments(["self", .. flags.Split(' ', StringSplitOptions.RemoveEmptyEntries)]);

        var query = LocalLlmConsole.ControlCli.ControlCliArgumentValues.SelfQuery(args, name => env.GetValueOrDefault(name));

        Assert.Equal("?" + expectedQuery, query);
    }

    private static Func<string, string> Respond(string self)
        => path => path == "/api/v1/self"
            ? self
            : path.StartsWith("/api/v1/models/", StringComparison.Ordinal)
                ? $"{{\"ok\":true,\"model\":{{\"id\":\"{path["/api/v1/models/".Length..].Split('/')[0]}\"}}}}"
                : "{\"ok\":true}";
}
