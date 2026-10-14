using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ControlCli;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.Tests;

#pragma warning disable xUnit1051

public sealed class ProfileTransferControlTests : ManagerRegressionTestBase
{
    [Fact]
    public void CliDefaultsToPreviewAndRequiresExplicitApply()
    {
        var root = CreateTempRoot();
        var file = Path.Combine(root, "profiles.jsonc");
        File.WriteAllText(file, "// transfer\n{\"profiles\":[{},{}],}");
        var export = ControlCliRequestFactory.BuildForTests("profiles", "export", "--model", "model/a", "--id", "profile/b", "--file", file);
        Assert.Equal("GET", export.Method);
        Assert.Equal("/api/v1/models/model%2Fa/profiles/export?profile=profile%2Fb", export.Path);
        var preview = ControlCliRequestFactory.BuildForTests("profiles", "import", "--model", "model/a", "--file", file);
        Assert.True(preview.Body!["dryRun"]!.GetValue<bool>());
        Assert.Equal(2, preview.Body["choices"]!.AsArray().Count);
        Assert.Equal(0, preview.Body["choices"]![0]!["action"]!.GetValue<int>());
        var apply = ControlCliRequestFactory.BuildForTests("profiles", "import", "--model", "model/a", "--file", file,
            "--apply", "--runtime", "installed", "--conflict", "replace");
        Assert.False(apply.Body!["dryRun"]!.GetValue<bool>());
        Assert.Equal("installed", apply.Body["choices"]![0]!["runtimeId"]!.GetValue<string>());
        Assert.Equal(1, apply.Body["choices"]![0]!["action"]!.GetValue<int>());
        Assert.Throws<InvalidOperationException>(() => ControlCliRequestFactory.BuildForTests("profiles", "import", "--model", "a", "--file", file, "--conflict", "invalid"));
        ControlCliProfileTransferOutput.SaveExportFile("{\"ok\":true,\"text\":\"bundle\"}", new Arguments(["profiles", "export", "--file", file]));
        Assert.Equal("bundle", File.ReadAllText(file));
        Assert.Throws<InvalidOperationException>(() => ControlCliProfileTransferOutput.SaveExportFile("{\"ok\":false}", new Arguments(["profiles", "export", "--file", file])));
        Assert.Equal("bundle", File.ReadAllText(file));
    }

    [Fact]
    public async Task ControlApiExportsPreviewsAppliesAndRejectsMalformedBundlesWithoutLoading()
    {
        var root = CreateTempRoot();
        var factory = new AppServiceFactory(root);
        await using var store = new StateStore(factory.DatabasePath); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var catalog = factory.CreateModelCatalogService(store);
        var profiles = factory.CreateModelLaunchProfileService(store, sessions);
        var settings = AppSettings.CreateDefault(root);
        var refreshes = 0;
        using var http = new HttpClient();
        var jobs = factory.CreateJobEngine(store);
        var api = new LocalControlApi(new LocalControlDependencies(root, store, sessions, catalog, profiles,
            factory.CreateRuntimeRegistryService(store), factory.CreateHuggingFaceService(store, jobs, catalog),
            factory.CreateRuntimeTelemetryApplicationService(factory.CreateRuntimeMetricPollerService(http)),
            factory.CreateRuntimeLogTailService(), factory.CreateRuntimeEndpointProbeService(http), factory.CreateLogPageWorkflowService(store),
            new LocalControlActions(() => settings, (next, _) => Task.FromResult(next),
                (_, _, _, _, _, _) => throw new InvalidOperationException("Import must never load a model."),
                (_, _) => throw new InvalidOperationException("Import must never stop a model."),
                _ => { refreshes++; return Task.CompletedTask; })));
        var model = new ModelRecord("model", "Model", Path.Combine(root, "model.gguf"), OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        var executable = Path.Combine(root, "llama-server.exe"); await File.WriteAllTextAsync(executable, "stub");
        await store.UpsertRuntimeAsync(new("cpu", "CPU", RuntimeMode.Native, RuntimeBackend.Cpu, executable, "{}", DateTimeOffset.UtcNow));
        await store.SaveNamedModelLaunchProfileAsync(new("original", model.Id, "Default", ModelLaunchSettings.FromAppSettings(settings, "cpu"), DateTimeOffset.UtcNow, true));
        static LocalControlRequest Request(string method, string path, JsonObject? body = null)
            => new(method, path, new Dictionary<string, string>(), body, new Dictionary<string, string>());
        var export = await api.HandleAsync(Request("GET", "/api/v1/models/model/profiles/export"));
        Assert.Equal(200, export.StatusCode);
        var text = JsonSerializer.SerializeToNode(export.Body, ProfileBundleCodec.JsonOptions)!["text"]!.GetValue<string>();
        var preview = await api.HandleAsync(Request("POST", "/api/v1/models/model/profiles/import", new() { ["text"] = text }));
        Assert.Equal(200, preview.StatusCode); Assert.Equal(0, refreshes);
        Assert.Single(await store.ListNamedModelLaunchProfilesAsync(model.Id));
        var applied = await api.HandleAsync(Request("POST", "/api/v1/models/model/profiles/import", new() { ["text"] = text, ["dryRun"] = false }));
        Assert.Equal(200, applied.StatusCode); Assert.Equal(1, refreshes);
        Assert.Equal(2, (await store.ListNamedModelLaunchProfilesAsync(model.Id)).Count);
        Assert.Empty(sessions.Snapshots());
        var malformed = await api.HandleAsync(Request("POST", "/api/v1/models/model/profiles/import", new() { ["text"] = "broken", ["dryRun"] = false }));
        Assert.Equal(400, malformed.StatusCode);
        Assert.Equal(2, (await store.ListNamedModelLaunchProfilesAsync(model.Id)).Count);
    }
}
