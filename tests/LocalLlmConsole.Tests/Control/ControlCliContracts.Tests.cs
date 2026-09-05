using System.Net;
using System.Text.Json;
using LocalLlmConsole.ControlCli;

namespace LocalLlmConsole.Tests;

public sealed class ControlCliContractsTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData("models get owner/model", "GET", "/api/v1/models/owner%2Fmodel")]
    [InlineData("models delete owner/model", "DELETE", "/api/v1/models/owner%2Fmodel?confirm=false")]
    [InlineData("models delete owner/model --confirm", "DELETE", "/api/v1/models/owner%2Fmodel?confirm=true")]
    [InlineData("profiles list owner/model", "GET", "/api/v1/models/owner%2Fmodel/profiles")]
    [InlineData("profiles delete owner/model --id profile/a", "DELETE", "/api/v1/models/owner%2Fmodel/profiles/profile%2Fa")]
    [InlineData("groups assign owner/model profile/a --group Batch", "PUT", "/api/v1/models/owner%2Fmodel/profiles/profile%2Fa/group")]
    [InlineData("groups unassign owner/model profile/a", "DELETE", "/api/v1/models/owner%2Fmodel/profiles/profile%2Fa/group")]
    [InlineData("sessions get session/a", "GET", "/api/v1/sessions/session%2Fa")]
    [InlineData("sessions logs session/a --tail 12", "GET", "/api/v1/sessions/session%2Fa/logs?tail=12")]
    [InlineData("sessions metrics session/a", "GET", "/api/v1/sessions/session%2Fa/metrics")]
    [InlineData("logs tail runtime.log --tail 25", "GET", "/api/v1/logs/runtime.log?tail=25")]
    [InlineData("settings rotate-key", "POST", "/api/v1/settings/model-api-key/rotate")]
    [InlineData("jobs pause job/a", "POST", "/api/v1/jobs/job%2Fa/pause")]
    [InlineData("jobs resume job/a", "POST", "/api/v1/jobs/job%2Fa/resume")]
    [InlineData("jobs cancel job/a", "POST", "/api/v1/jobs/job%2Fa/cancel")]
    [InlineData("hf search owner/model", "GET", "/api/v1/huggingface/search?q=owner%2Fmodel")]
    public void CommandsPreserveIdentifiersAndExplicitConfirmation(string command, string method, string path)
    {
        var request = ControlCliRequestFactory.BuildForTests(command.Split(' '));
        Assert.Equal(method, request.Method);
        Assert.Equal(path, request.Path);
        if (method == "GET" || method == "DELETE") Assert.Null(request.Body);
    }

    [Fact]
    public void ProfileAndDownloadOptionsReachTheApiWithoutLosingTypesOrPaths()
    {
        var profile = ControlCliRequestFactory.BuildForTests("profiles", "create", "model", "--name", "Long context",
            "--id", "profile", "--default", "--set", "contextSize=65536", "--set", "enableMetrics=false");
        Assert.Equal("Long context", profile.Body!["name"]!.GetValue<string>());
        Assert.Equal("profile", profile.Body["id"]!.GetValue<string>());
        Assert.True(profile.Body["isDefault"]!.GetValue<bool>());
        Assert.False(profile.Body["replace"]!.GetValue<bool>());
        Assert.Equal(65536L, profile.Body["settings"]!["contextSize"]!.GetValue<long>());
        Assert.False(profile.Body["settings"]!["enableMetrics"]!.GetValue<bool>());

        var download = ControlCliRequestFactory.BuildForTests("hf", "download", "--repo", "owner/repo",
            "--file", "folder/model.gguf", "--revision", "pinned-commit", "--dry-run");
        Assert.Equal("owner/repo", download.Body!["repo"]!.GetValue<string>());
        Assert.Equal("folder/model.gguf", download.Body["path"]!.GetValue<string>());
        Assert.Equal("pinned-commit", download.Body["revision"]!.GetValue<string>());
        Assert.True(download.Body["dryRun"]!.GetValue<bool>());
    }

    [Theory]
    [InlineData("models import")]
    [InlineData("models import --file a --folder b")]
    [InlineData("profiles create model")]
    [InlineData("profiles update model")]
    [InlineData("groups create --name Batch --idle-minutes banana")]
    [InlineData("runtimes register")]
    [InlineData("sessions logs")]
    [InlineData("hf search")]
    [InlineData("request POST /api/v1/settings --body []")]
    public void InvalidCommandsAreRejectedBeforeSendingARequest(string command)
        => Assert.Throws<InvalidOperationException>(() => ControlCliRequestFactory.BuildForTests(command.Split(' ')));

    [Fact]
    public void RawBodyFilePreservesNestedJsonAndRejectsNonObjects()
    {
        var path = Path.Combine(CreateTempRoot(), "body.json");
        File.WriteAllText(path, """{"settings":{"contextSize":8192},"confirm":false}""");
        var request = ControlCliRequestFactory.BuildForTests("request", "patch", "/api/v1/settings", "--body-file", path);
        Assert.Equal("PATCH", request.Method);
        Assert.Equal(8192, request.Body!["settings"]!["contextSize"]!.GetValue<int>());
        Assert.False(request.Body["confirm"]!.GetValue<bool>());
        File.WriteAllText(path, "[]");
        Assert.Throws<InvalidOperationException>(() => ControlCliRequestFactory.BuildForTests("request", "POST", "/", "--body-file", path));
    }

    [Theory]
    [InlineData("benchmarks run --wait", true)]
    [InlineData("benchmark start --wait", true)]
    [InlineData("benchmarks wait run", true)]
    [InlineData("benchmarks run --wait --dry-run", false)]
    [InlineData("benchmarks run", false)]
    [InlineData("models load model --wait", false)]
    public void OnlyBenchmarkWaitCommandsPoll(string command, bool expected)
        => Assert.Equal(expected, ControlCliBenchmarkWaiter.ShouldWait(new Arguments(command.Split(' '))));

    [Theory]
    [InlineData("Completed", 5, 0)]
    [InlineData("Failed", 4, 2)]
    [InlineData("Cancelled", 3, 2)]
    [InlineData("Interrupted", 6, 2)]
    public async Task TerminalBenchmarkResponsesReturnTheirExitCodeWithoutPolling(string status, int numericStatus, int expected)
    {
        using var http = new HttpClient(new CapturingHttpHandler(_ => throw new InvalidOperationException("Terminal runs must not poll.")));
        foreach (var value in new object[] { status, numericStatus })
        {
            var text = BenchmarkResponse(value, 4);
            var result = await ControlCliBenchmarkWaiter.WaitAsync(http, text, new Arguments(["benchmarks", "wait", "run"]));
            Assert.Equal(expected, result.ExitCode);
            Assert.Equal(text, result.Text);
        }
    }

    [Fact]
    public async Task BenchmarkWaitAdvancesRevisionAndPreservesHttpErrors()
    {
        var requests = new List<Uri>();
        const string errorBody = """{"ok":false,"error":"run no longer exists"}""";
        using var http = new HttpClient(new CapturingHttpHandler(request =>
        {
            requests.Add(request.RequestUri!);
            return new HttpResponseMessage(requests.Count == 1 ? HttpStatusCode.OK : HttpStatusCode.NotFound)
            {
                Content = new StringContent(requests.Count == 1 ? BenchmarkResponse("Paused", 8) : errorBody)
            };
        }))
        { BaseAddress = new Uri("http://127.0.0.1/") };

        var result = await ControlCliBenchmarkWaiter.WaitAsync(http, BenchmarkResponse("Running", 7),
            new Arguments(["benchmarks", "wait", "run", "--timeout", "60"]));

        Assert.Equal(255, result.ExitCode);
        Assert.Equal(errorBody, result.Text);
        Assert.Equal(2, requests.Count);
        Assert.Contains("run%2Fid/wait?afterRevision=7", requests[0].OriginalString, StringComparison.Ordinal);
        Assert.Contains("afterRevision=8", requests[1].Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BenchmarkWaitRejectsMissingRunIdentity()
    {
        using var http = new HttpClient(new CapturingHttpHandler(_ => throw new InvalidOperationException("Must not poll.")));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => ControlCliBenchmarkWaiter.WaitAsync(
            http, "{}", new Arguments(["benchmarks", "wait", "run"])));
        Assert.Contains("run id", error.Message, StringComparison.Ordinal);
    }

    private static string BenchmarkResponse(object status, int revision)
        => JsonSerializer.Serialize(new { Run = new { Job = new { Id = "run/id", Status = status }, Payload = new { Revision = revision } } });
}
