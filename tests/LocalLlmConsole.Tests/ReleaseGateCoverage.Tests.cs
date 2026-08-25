using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

#pragma warning disable xUnit1051

public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public async Task LocalControlApiRoutesCoverInventorySettingsValidationAndOperations()
    {
        var root = CreateTempRoot();
        var factory = new AppServiceFactory(root);
        await using var store = new StateStore(factory.DatabasePath);
        await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var catalog = factory.CreateModelCatalogService(store);
        var profiles = factory.CreateModelLaunchProfileService(store, sessions);
        var modelGroups = factory.CreateModelGroupService(store);
        var runtimes = factory.CreateRuntimeRegistryService(store);
        var jobs = factory.CreateJobEngine(store);
        using var http = new HttpClient(new StaticJsonHandler("[]"));
        var huggingFace = factory.CreateHuggingFaceService(store, jobs, catalog);
        var telemetry = factory.CreateRuntimeTelemetryApplicationService(factory.CreateRuntimeMetricPollerService(http));
        var settings = AppSettings.CreateDefault(root);
        var refreshes = 0;
        var operations = 0;
        var auditLog = new ControlApiAuditLogService(Path.Combine(root, "logs"), () => 1);
        var api = new LocalControlApi(new LocalControlDependencies(
            root,
            store,
            sessions,
            catalog,
            profiles,
            runtimes,
            huggingFace,
            telemetry,
            factory.CreateRuntimeLogTailService(),
            factory.CreateRuntimeEndpointProbeService(http),
            factory.CreateLogPageWorkflowService(store),
            new LocalControlActions(
                () => settings,
                (next, _) => Task.FromResult(settings = next),
                (_, _, _, _, _, _) => throw new InvalidOperationException("No runtime expected."),
                (_, _) => Task.CompletedTask,
                _ => { refreshes++; return Task.CompletedTask; },
                (name, body, _) => { operations++; return Task.FromResult<object>(new { name, body }); }),
            auditLog,
            modelGroups,
            new EndpointInspectionService(http)));

        static LocalControlRequest Request(string method, string path, JsonObject? body = null, IReadOnlyDictionary<string, string>? query = null)
            => new(method, path, query ?? new Dictionary<string, string>(), body, new Dictionary<string, string>());

        foreach (var path in new[] { "/api/v1/status", "/api/v1/capabilities", "/api/v1/self", "/api/v1/models", "/api/v1/model-groups", "/api/v1/runtimes", "/api/v1/sessions", "/api/v1/settings", "/api/v1/logs", "/api/v1/metrics", "/api/v1/jobs", "/api/v1/operations" })
            Assert.Equal(200, (await api.HandleAsync(Request("GET", path))).StatusCode);
        await store.RecordTokenUsageAsync(new TokenUsageDelta(
            "metrics-model", "Metrics Model", 20, 10, 5, true, DateTimeOffset.UtcNow,
            PromptSeconds: 2, GeneratedSeconds: 1, TimingCounterObserved: true,
            RequestCount: 1, RequestCounterObserved: true));
        var usageResponse = await api.HandleAsync(Request(
            "GET",
            "/api/v1/metrics/usage",
            query: new Dictionary<string, string> { ["range"] = "30d", ["timeZone"] = "UTC" }));
        Assert.Equal(200, usageResponse.StatusCode);
        var usageJson = JsonSerializer.Serialize(usageResponse.Body);
        Assert.Contains("\"insights\"", usageJson, StringComparison.Ordinal);
        Assert.Contains("\"averagePromptTokensPerSecond\"", usageJson, StringComparison.Ordinal);
        Assert.Contains("\"requestCounterObserved\"", usageJson, StringComparison.Ordinal);
        Assert.Contains("\"trackedTokenShare\"", usageJson, StringComparison.Ordinal);
        Assert.Contains("\"gpuEnergy\"", usageJson, StringComparison.Ordinal);
        Assert.Contains("\"gpuEnergyDevices\"", usageJson, StringComparison.Ordinal);
        Assert.Contains("\"wattHours\"", usageJson, StringComparison.Ordinal);
        Assert.Equal(200, (await api.HandleAsync(Request(
            "GET",
            "/api/v1/metrics/usage",
            query: new Dictionary<string, string> { ["range"] = "month", ["timeZone"] = "UTC" }))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request(
            "GET",
            "/api/v1/metrics/usage",
            query: new Dictionary<string, string> { ["range"] = "1d", ["timeZone"] = "UTC" }))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request(
            "GET",
            "/api/v1/metrics/usage",
            query: new Dictionary<string, string> { ["dates"] = "2026-08-18,2026-08-20", ["timeZone"] = "UTC" }))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request(
            "GET",
            "/api/v1/metrics/usage",
            query: new Dictionary<string, string> { ["dates"] = "08/20/2026" }))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request(
            "GET",
            "/api/v1/metrics/usage",
            query: new Dictionary<string, string> { ["range"] = "invalid" }))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("GET", "/api/v1/gateway/inspect"))).StatusCode);

        Assert.Equal(404, (await api.HandleAsync(Request("GET", "/unknown"))).StatusCode);
        Assert.Equal(404, (await api.HandleAsync(Request("POST", "/api/v1/operations"))).StatusCode);
        Assert.Equal(404, (await api.HandleAsync(Request("GET", "/api/v1/models/missing"))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request("POST", "/api/v1/models/import", new JsonObject { ["folder"] = Path.Combine(root, "missing-model") }))).StatusCode);
        var importedModelPath = Path.Combine(root, "external", "Future-Model-MTP-Edition.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(importedModelPath)!);
        WriteMinimalGguf(importedModelPath, "qwen35");
        var importResponse = await api.HandleAsync(Request(
            "POST",
            "/api/v1/models/import",
            new JsonObject { ["file"] = importedModelPath }));
        Assert.Equal(200, importResponse.StatusCode);
        Assert.Contains("\"source\":\"file\"", JsonSerializer.Serialize(importResponse.Body), StringComparison.Ordinal);
        Assert.Equal(400, (await api.HandleAsync(Request("POST", "/api/v1/runtimes/register", new JsonObject { ["folder"] = Path.Combine(root, "missing-runtime") }))).StatusCode);
        Assert.Equal(404, (await api.HandleAsync(Request("GET", "/api/v1/sessions/missing"))).StatusCode);
        Assert.Equal(404, (await api.HandleAsync(Request("GET", "/api/v1/logs/missing.log"))).StatusCode);
        Assert.Equal(404, (await api.HandleAsync(Request("POST", "/api/v1/jobs/missing/pause"))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request("POST", "/api/v1/huggingface/download", new JsonObject()))).StatusCode);

        var runtimeBuildJob = await jobs.CreateAsync("runtime-build", "{}", TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(runtimeBuildJob, JobStatus.Running, runtimeBuildJob.PayloadJson, TestContext.Current.CancellationToken);
        Assert.Equal(409, (await api.HandleAsync(Request("POST", $"/api/v1/jobs/{runtimeBuildJob.Id}/pause"))).StatusCode);
        Assert.Equal(409, (await api.HandleAsync(Request("POST", $"/api/v1/jobs/{runtimeBuildJob.Id}/cancel"))).StatusCode);
        Assert.Equal(JobStatus.Running, (await store.ListJobsAsync()).Single(job => job.Id == runtimeBuildJob.Id).Status);

        var downloadJob = await jobs.CreateAsync("huggingface-download", "{}", TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(downloadJob, JobStatus.Running, downloadJob.PayloadJson, TestContext.Current.CancellationToken);
        Assert.Equal(200, (await api.HandleAsync(Request("POST", $"/api/v1/jobs/{downloadJob.Id}/pause"))).StatusCode);
        Assert.Equal(JobStatus.Paused, (await store.ListJobsAsync()).Single(job => job.Id == downloadJob.Id).Status);

        var patched = await api.HandleAsync(Request("PATCH", "/api/v1/settings", new JsonObject
        {
            ["contextSize"] = 65536,
            ["temperature"] = 0.7
        }));
        Assert.Equal(200, patched.StatusCode);
        Assert.Equal(65536, settings.ContextSize);
        Assert.True(settings.RequireApiKeyAuth);
        Assert.Equal(200, (await api.HandleAsync(Request("PATCH", "/api/v1/settings", new JsonObject
        {
            ["requireApiKeyAuth"] = false
        }))).StatusCode);
        Assert.False(settings.RequireApiKeyAuth);
        Assert.Equal("", settings.ModelApiKey);
        Assert.True(ApiSecurity.IsStrongBearerSecret(settings.ModelApiKeyBackup));
        Assert.Equal(400, (await api.HandleAsync(Request("PATCH", "/api/v1/settings", new JsonObject { ["port"] = 70000 }))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request("PATCH", "/api/v1/settings", new JsonObject { ["workspaceRoot"] = "blocked" }))).StatusCode);

        Assert.Equal(200, (await api.HandleAsync(Request("POST", "/api/v1/settings/model-api-key/rotate"))).StatusCode);
        Assert.True(settings.RequireApiKeyAuth);
        Assert.Equal(32, settings.ModelApiKey.Length);

        var operation = await api.HandleAsync(Request("POST", "/api/v1/operations/app.refresh", new JsonObject { ["dryRun"] = true }));
        Assert.Equal(200, operation.StatusCode);
        Assert.Equal(1, operations);
        Assert.Equal(400, (await api.HandleAsync(Request("POST", "/api/v1/operations/app.shutdown", new JsonObject()))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("POST", "/api/v1/operations/app.shutdown", new JsonObject { ["confirm"] = true }))).StatusCode);

        var modelPath = Path.Combine(settings.ModelsRoot, "api-model-q4.gguf");
        Directory.CreateDirectory(settings.ModelsRoot);
        File.WriteAllBytes(modelPath, [0x47, 0x47, 0x55, 0x46, 0x03, 0x00, 0x00, 0x00]);
        var model = new ModelRecord("api-model", "API Model", modelPath, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        var defaultProfile = await profiles.EnsureDefaultAsync(model, settings);
        var createdGroupResponse = await api.HandleAsync(Request("POST", "/api/v1/model-groups", new JsonObject
        {
            ["name"] = "API batch",
            ["retentionMode"] = "idle-timeout",
            ["idleMinutes"] = 9,
            ["evictionPriority"] = "low"
        }));
        Assert.Equal(201, createdGroupResponse.StatusCode);
        var createdGroupJson = JsonSerializer.SerializeToNode(createdGroupResponse.Body)!.AsObject();
        var createdGroupId = createdGroupJson["group"]!["id"]!.GetValue<string>();
        Assert.Equal("IdleTimeout", createdGroupJson["group"]!["retentionMode"]!.GetValue<string>());
        Assert.Equal("Low", createdGroupJson["group"]!["evictionPriority"]!.GetValue<string>());
        var defaultGroupRoute = $"/api/v1/models/api-model/profiles/{Uri.EscapeDataString(defaultProfile.Id)}/group";
        Assert.Equal(200, (await api.HandleAsync(Request("PUT", defaultGroupRoute, new JsonObject
        {
            ["group"] = createdGroupId
        }))).StatusCode);
        var assignedProfileResponse = await api.HandleAsync(Request("GET", defaultGroupRoute));
        Assert.Equal(200, assignedProfileResponse.StatusCode);
        var assignedProfileJson = JsonSerializer.SerializeToNode(assignedProfileResponse.Body)!.AsObject();
        Assert.Equal(createdGroupId, assignedProfileJson["profile"]!["group"]!["id"]!.GetValue<string>());
        Assert.Equal(9, assignedProfileJson["profile"]!["effectivePolicy"]!["idleMinutes"]!.GetValue<int>());
        Assert.Equal(200, (await api.HandleAsync(Request("PATCH", $"/api/v1/model-groups/{Uri.EscapeDataString(createdGroupId)}", new JsonObject
        {
            ["evictionPriority"] = "high"
        }))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("GET", "/api/v1/models/api-model"))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("GET", "/api/v1/models/api-model/companions"))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("GET", "/api/v1/models/api-model/profiles"))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request("POST", "/api/v1/models/api-model/profiles", new JsonObject()))).StatusCode);
        var createdProfile = await api.HandleAsync(Request("POST", "/api/v1/models/api-model/profiles", new JsonObject
        {
            ["id"] = "profile:api-model:test",
            ["name"] = "Test profile",
            ["settings"] = new JsonObject { ["contextSize"] = 32768, ["temperature"] = 0.5 }
        }));
        Assert.Equal(201, createdProfile.StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("PUT", "/api/v1/models/api-model/profiles/profile%3Aapi-model%3Atest", new JsonObject
        {
            ["name"] = "Updated profile",
            ["settings"] = new JsonObject { ["contextSize"] = 65536 }
        }))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request("POST", "/api/v1/models/api-model/load", new JsonObject()))).StatusCode);
        var inspectionRuntime = new RuntimeRecord(
            "runtime-inspection",
            "Inspection Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(root, "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        sessions.AttachExisting(
            inspectionRuntime,
            model,
            settings with { Port = 8099 },
            Path.Combine(root, "inspection.log"),
            LlamaRuntimeState.Loaded,
            "",
            "session-api-model",
            DateTimeOffset.UtcNow);
        var inspection = await api.HandleAsync(Request("GET", "/api/v1/sessions/session-api-model/inspect"));
        Assert.True(inspection.StatusCode == 200, JsonSerializer.Serialize(inspection.Body));
        var inspectionJson = JsonSerializer.SerializeToNode(inspection.Body)!.AsObject();
        Assert.Equal((int)EndpointInspectionKind.DirectModel, inspectionJson["report"]!["kind"]!.GetValue<int>());
        Assert.DoesNotContain(settings.ModelApiKey, JsonSerializer.Serialize(inspection.Body), StringComparison.Ordinal);
        Assert.Equal(200, (await api.HandleAsync(Request("POST", "/api/v1/models/api-model/unload"))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("DELETE", "/api/v1/models/api-model/profiles/profile%3Aapi-model%3Atest"))).StatusCode);
        Assert.Equal(400, (await api.HandleAsync(Request("DELETE", "/api/v1/models/api-model"))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("DELETE", "/api/v1/models/api-model", query: new Dictionary<string, string> { ["confirm"] = "true" }))).StatusCode);

        Directory.CreateDirectory(settings.RuntimeRoot);
        Assert.Equal(200, (await api.HandleAsync(Request("POST", "/api/v1/models/scan"))).StatusCode);
        Assert.Equal(200, (await api.HandleAsync(Request("POST", "/api/v1/runtimes/scan"))).StatusCode);
        Assert.True(refreshes >= 6);

        await auditLog.WriteAsync(
            Request("GET\r\nINJECT", "/api/v1/status?token=super-secret\r\nINJECT"),
            new LocalControlApiResponse(204, new { ok = true }),
            TimeSpan.FromMilliseconds(12));
        var auditText = await File.ReadAllTextAsync(auditLog.LogPath);
        Assert.Contains("GET /api/v1/status -> 200", auditText, StringComparison.Ordinal);
        Assert.Contains("POST /api/v1/models/import -> 400", auditText, StringComparison.Ordinal);
        Assert.Contains("GETINJECT /api/v1/status -> 204", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain("super-secret", auditText, StringComparison.Ordinal);
        Assert.DoesNotContain(settings.ModelApiKey, auditText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SettingsWorkflowsAndLocalHttpHostCoverReleaseSecurityPaths()
    {
        Loc.LoadLanguage("en");
        var root = CreateTempRoot();
        var current = AppSettings.CreateDefault(root) with
        {
            ModelApiKey = new string('a', 32),
            ModelApiKeyBackup = new string('a', 32),
            AutoLoadGatewayEnabled = false
        };
        var service = new AppSettingsUpdateService();
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["modelAccessMode"] = "Gateway + direct LAN",
            ["requireApiKeyAuth"] = "Enable",
            ["modelApiKey"] = "",
            ["autoLoadGatewayEnabled"] = "Yes",
            ["autoLoadGatewayPort"] = "8090",
            ["autoLoadGatewayPolicy"] = "Single active model",
            ["minimizeBehavior"] = "Tray + taskbar",
            ["startWithWindows"] = "Yes",
            ["autoUnloadIdleMinutes"] = "99999",
            ["deleteRuntimeSourceAfterSuccessfulBuild"] = "Yes",
            ["showOverviewModelStatus"] = "Hide",
            ["showOverviewHardware"] = "Hide",
            ["showOverviewSlots"] = "Hide",
            ["showOverviewTokens"] = "Hide",
            ["showOverviewMtpTokens"] = "Hide",
            ["showOverviewKvCache"] = "Hide",
            ["showOverviewLiveRuntimeLog"] = "Hide",
            ["runtimeLogOrder"] = "Latest on bottom",
            ["showOverviewAllMetrics"] = "Hide",
            ["showModelsHuggingFace"] = "Hide",
            ["trackGpuEnergyWhileIdle"] = "Yes",
            ["maxLogFileSizeMb"] = "99999"
        };
        var updated = service.Build(new AppSettingsUpdateRequest(current, root, "dark", values, new HashSet<int>()));
        Assert.True(updated.Success);
        Assert.Equal("both", updated.Settings.ModelAccessMode);
        Assert.Equal(10080, updated.Settings.AutoUnloadIdleMinutes);
        Assert.Equal(4096, updated.Settings.MaxLogFileSizeMb);
        Assert.False(updated.Settings.ShowOverviewModelStatus);
        Assert.False(updated.Settings.ShowOverviewHardware);
        Assert.False(updated.Settings.ShowOverviewSlots);
        Assert.False(updated.Settings.ShowOverviewTokens);
        Assert.False(updated.Settings.ShowOverviewMtpTokens);
        Assert.False(updated.Settings.ShowOverviewKvCache);
        Assert.False(updated.Settings.ShowOverviewLiveRuntimeLog);
        Assert.Equal("oldestFirst", updated.Settings.RuntimeLogOrder);
        Assert.False(updated.Settings.ShowOverviewAllMetrics);
        Assert.False(updated.Settings.ShowModelsHuggingFace);
        Assert.True(updated.Settings.TrackGpuEnergyWhileIdle);
        var idleEnergyTracking = new SettingsPageDefinitionService().BuildRows(updated.Settings)
            .Single(row => row.Key == "trackGpuEnergyWhileIdle");
        Assert.Equal("choice", idleEnergyTracking.Type);
        var runtimeLogOrder = new SettingsPageDefinitionService().BuildRows(updated.Settings)
            .Single(row => row.Key == "runtimeLogOrder");
        Assert.Equal("choice", runtimeLogOrder.Type);
        Assert.Contains("Latest on top", runtimeLogOrder.Options!);
        Assert.Contains("Latest on bottom", runtimeLogOrder.Options!);

        foreach (var invalid in new[]
        {
            new Dictionary<string, string> { ["modelApiKey"] = "short" },
            new Dictionary<string, string> { ["autoLoadGatewayPort"] = "bad" },
            new Dictionary<string, string> { ["autoLoadGatewayPort"] = "70000" },
            new Dictionary<string, string> { ["autoUnloadIdleMinutes"] = "bad" },
            new Dictionary<string, string> { ["maxLogFileSizeMb"] = "bad" }
        })
            Assert.False(service.Build(new AppSettingsUpdateRequest(current, root, "system", invalid, new HashSet<int>())).Success);
        Assert.False(service.Build(new AppSettingsUpdateRequest(current, root, "system", new Dictionary<string, string>
        {
            ["autoLoadGatewayEnabled"] = "Yes",
            ["autoLoadGatewayPort"] = "8082"
        }, new HashSet<int> { 8082 })).Success);

        var disabled = service.Build(new AppSettingsUpdateRequest(current, root, "system", new Dictionary<string, string>
        {
            ["requireApiKeyAuth"] = "Disable"
        }, new HashSet<int>()));
        Assert.True(disabled.Success);
        Assert.False(disabled.Settings.RequireApiKeyAuth);
        Assert.Equal("", disabled.Settings.ModelApiKey);
        Assert.Equal(current.ModelApiKey, disabled.Settings.ModelApiKeyBackup);
        var apiKeyAuth = new SettingsPageDefinitionService().BuildRows(disabled.Settings)
            .Single(row => row.Key == "requireApiKeyAuth");
        Assert.Equal("choice", apiKeyAuth.Type);
        Assert.Equal("Disable", apiKeyAuth.Value);
        Assert.Equal(new[] { "Enable", "Disable" }, apiKeyAuth.Options);

        var database = Path.Combine(root, "state", "settings.db");
        await using var store = new StateStore(database);
        await store.InitializeAsync();
        var workflow = new AppSettingsWorkflowService(store, service, root);
        var saved = await workflow.SaveEditedAsync(new AppSettingsSaveWorkflowRequest(current, "dark", values, new HashSet<int>()), TestContext.Current.CancellationToken);
        Assert.True(saved.Success);
        Assert.True(Directory.Exists(saved.Settings.ModelsRoot));
        var reloaded = await store.GetAppSettingsAsync(root);
        Assert.False(reloaded.ShowOverviewModelStatus);
        Assert.False(reloaded.ShowOverviewHardware);
        Assert.False(reloaded.ShowOverviewSlots);
        Assert.False(reloaded.ShowOverviewTokens);
        Assert.False(reloaded.ShowOverviewMtpTokens);
        Assert.False(reloaded.ShowOverviewKvCache);
        Assert.False(reloaded.ShowOverviewLiveRuntimeLog);
        Assert.Equal("oldestFirst", reloaded.RuntimeLogOrder);
        Assert.False(reloaded.ShowOverviewAllMetrics);
        Assert.False(reloaded.ShowModelsHuggingFace);
        Assert.True(reloaded.TrackGpuEnergyWhileIdle);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.EnsureModelApiKeyAsync(
            saved.Settings,
            saved.Settings with { RequireApiKeyAuth = false },
            TestContext.Current.CancellationToken));
        var localUnauthenticated = await workflow.EnsureModelApiKeyAsync(
            saved.Settings,
            saved.Settings with { RequireApiKeyAuth = false, ModelAccessMode = "local", Host = "127.0.0.1" },
            TestContext.Current.CancellationToken);
        Assert.False(localUnauthenticated.Settings.RequireApiKeyAuth);
        Assert.Equal("", localUnauthenticated.Settings.ModelApiKey);
        Assert.True(ApiSecurity.IsStrongBearerSecret(localUnauthenticated.Settings.ModelApiKeyBackup));
        var generated = await workflow.EnsureModelApiKeyAsync(
            localUnauthenticated.PersistedSettings with { RequireApiKeyAuth = true, ModelApiKey = "", ModelApiKeyBackup = "" },
            localUnauthenticated.Settings with { RequireApiKeyAuth = true, ModelApiKey = "", ModelApiKeyBackup = "" },
            TestContext.Current.CancellationToken);
        Assert.True(generated.GeneratedApiKey);
        var trimmed = await workflow.EnsureModelApiKeyAsync(generated.PersistedSettings, generated.Settings with { ModelApiKey = $"  {new string('b', 32)}  " }, TestContext.Current.CancellationToken);
        Assert.False(trimmed.GeneratedApiKey);

        string? startupCommand = null;
        var startup = new WindowsStartupRegistrationService(
            () => startupCommand,
            value => startupCommand = value,
            () => startupCommand = null,
            () => Path.Combine(root, "LlamaCppWindowsManager.exe"));
        var application = new AppSettingsApplicationService(workflow, startup);
        var applied = false;
        var refreshed = false;
        var status = "";
        var gatewayRestarts = 0;
        var actions = new AppSettingsSaveApplicationActions(
            _ => applied = true,
            _ => { },
            () => { },
            () =>
            {
                gatewayRestarts++;
                return Task.FromResult(false);
            },
            () => true,
            () => refreshed = true,
            value => status = value);
        var applicationRequest = new AppSettingsSaveApplicationRequest(current, "dark", values, []);
        var outcome = await application.SaveEditedAndApplyAsync(applicationRequest, actions, TestContext.Current.CancellationToken);
        Assert.Equal(AppSettingsSaveApplicationOutcome.Saved, outcome);
        Assert.True(applied);
        Assert.True(refreshed);
        Assert.Contains("Gateway did not start", status, StringComparison.Ordinal);
        Assert.Equal(1, gatewayRestarts);
        Assert.NotNull(startupCommand);
        var uiOnlyValues = new Dictionary<string, string>
        {
            ["showModelsHuggingFace"] = "Show"
        };
        var uiOnlyBuilt = service.Build(new AppSettingsUpdateRequest(current, root, "dark", uiOnlyValues, new HashSet<int>()));
        Assert.True(uiOnlyBuilt.Success);
        Assert.Equal(current.AutoLoadGatewayEnabled, uiOnlyBuilt.Settings.AutoLoadGatewayEnabled);
        Assert.Equal(current.AutoLoadGatewayPort, uiOnlyBuilt.Settings.AutoLoadGatewayPort);
        Assert.Equal(current.AutoLoadGatewayPolicy, uiOnlyBuilt.Settings.AutoLoadGatewayPolicy);
        Assert.Equal(current.ModelAccessMode, uiOnlyBuilt.Settings.ModelAccessMode);
        Assert.Equal(current.RequireApiKeyAuth, uiOnlyBuilt.Settings.RequireApiKeyAuth);
        Assert.Equal(current.ModelApiKey, uiOnlyBuilt.Settings.ModelApiKey);
        var uiOnlyOutcome = await application.SaveEditedAndApplyAsync(
            new AppSettingsSaveApplicationRequest(current, "dark", uiOnlyValues, []),
            actions,
            TestContext.Current.CancellationToken);
        Assert.Equal(AppSettingsSaveApplicationOutcome.Saved, uiOnlyOutcome);
        Assert.Equal(1, gatewayRestarts);
        var failedOutcome = await application.SaveEditedAndApplyAsync(
            applicationRequest with { Values = new Dictionary<string, string> { ["autoLoadGatewayPort"] = "bad" } },
            actions,
            TestContext.Current.CancellationToken);
        Assert.Equal(AppSettingsSaveApplicationOutcome.Failed, failedOutcome);

        var help = new HelpNavigationApplicationService();
        foreach (var target in new[] { "overview", "loaded-sessions", "models", "runtime-download", "runtime-jobs", "windows-tools", "wsl-tools", "model-download", "launch-settings", "overview-load", "settings", "gateway-settings", "logs", "lifetime", "updates" })
            Assert.True(help.Plan(target).ShouldNavigate);
        Assert.False(help.Plan("unknown").ShouldNavigate);
        var helpCatalog = new HelpCatalogService();
        Assert.Equal(HelpCatalogService.FirstSteps, helpCatalog.DefinitionFor("unknown").Key);
        Assert.Equal("models", helpCatalog.Select("models").Key);

        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var port = FreeCoveragePort();
        await using var host = new LocalAppService(store, jobs, port);
        await host.StartAsync();
        using var client = new HttpClient { BaseAddress = host.BaseUri };
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/health", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/jobs", TestContext.Current.CancellationToken)).StatusCode);
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", host.SessionToken);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing", TestContext.Current.CancellationToken)).StatusCode);
        using var options = new HttpRequestMessage(HttpMethod.Options, "/api/jobs");
        Assert.Equal(HttpStatusCode.NoContent, (await client.SendAsync(options, TestContext.Current.CancellationToken)).StatusCode);
        using var hostile = new HttpRequestMessage(HttpMethod.Get, "/api/jobs");
        hostile.Headers.TryAddWithoutValidation("Origin", "https://example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.SendAsync(hostile, TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public void PageViewModelsRenderDomainStateAndEdgeCases()
    {
        var root = CreateTempRoot();
        var now = DateTimeOffset.UtcNow;
        var modelPath = Path.Combine(root, "models", "qwen-q4_k_m.gguf");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        File.WriteAllBytes(modelPath, new byte[1536]);
        var model = new ModelRecord("model-1", "Qwen Test", modelPath, OwnershipKind.External, "{}", now);
        var other = new ModelRecord("model-2", "Llama Test", Path.Combine(root, "models", "llama.gguf"), OwnershipKind.External, "{}", now);
        var defaults = AppSettings.CreateDefault(root);
        var profile = new NamedModelLaunchProfile("profile-1", model.Id, "Qwen 128K", ModelLaunchSettings.FromAppSettings(defaults with { Port = 8096 }), now);
        var secondProfile = profile with { Id = "profile-2", Name = "Qwen Fast", IsDefault = true };
        var modelSizeLabels = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [model.Id] = "1.5 KB",
            [other.Id] = "Missing"
        };
        var models = new ModelsPageViewModel();

        models.ReplaceModels([model, other], candidate => candidate.Id == model.Id, [profile, secondProfile], modelSizeLabels);
        Assert.Equal(2, models.Rows.Count);
        var missingModelRow = models.Rows.Single(row => row.Model.Id == other.Id);
        Assert.Equal("Missing", missingModelRow.Size);
        Assert.True(missingModelRow.IsMissing);
        Assert.False(missingModelRow.CanLoad);
        Assert.Equal(2, models.VariantRows.Count);
        Assert.All(models.VariantRows, row => Assert.False(row.CanDelete));
        Assert.Equal(model.Id, models.ModelIdForLaunchProfile(profile.Id));
        models.ShowLaunchProfilesForModel(other.Id);
        Assert.Empty(models.VariantRows);
        Assert.Null(models.ModelIdForLaunchProfile(""));

        var overview = new OverviewPageViewModel();
        overview.ReplaceModels(
            [other, model],
            [],
            new Dictionary<string, ModelGroupAssignment>(),
            [],
            modelSizeLabels);
        overview.ReplaceLaunchProfiles([profile, secondProfile]);
        Assert.Equal(2, overview.ModelChoices.Count);
        Assert.Equal("Llama Test · Missing", overview.ModelChoices[0].DisplayName);
        Assert.True(overview.ModelChoices[0].IsMissing);
        Assert.False(overview.ModelChoices[1].IsMissing);
        Assert.Equal("Qwen Test · 1.5 KB", overview.ModelChoices[1].DisplayName);
        Assert.Equal(model.Id, overview.ModelChoices[1].Id);
        Assert.Equal(2, overview.LaunchProfileChoices.Count);
        overview.ReplaceSessions([]);
        Assert.Empty(overview.SessionRows);
        var running = new LoadedModelSessionSnapshot(
            "session-1", model.Id, model.Name, "runtime-1", "CUDA Runtime", RuntimeMode.Native, RuntimeBackend.Cuda,
            defaults with { Port = 8083 }, Path.Combine(root, "runtime.log"), now, "", 123,
            LoadedModelSessionStatus.Running, true, true, 8192, profile.Id, profile.Name);
        var gateway = new GatewayRoutingOverviewStatus(true, true, "http://127.0.0.1:8082/v1", "Listening", "singleActive", "Local", 1);
        overview.ReplaceSessions([running], gateway);
        Assert.Equal(2, overview.SessionRows.Count);
        Assert.False(overview.ReplaceSessionsIfChanged([running], gateway));
        Assert.True(overview.ReplaceSessionsIfChanged([running with { Status = LoadedModelSessionStatus.Loading }], gateway));
        Assert.Contains("Loading", overview.SessionRows[1].C4, StringComparison.Ordinal);
        overview.ReplaceSessions([running with { Status = LoadedModelSessionStatus.Failed, StatusReason = "boom" }], GatewayRoutingOverviewStatus.Hidden);
        Assert.Contains("boom", overview.SessionRows[0].C4, StringComparison.Ordinal);

        var runtime = new RuntimeRecord("runtime-1", "CUDA Runtime", RuntimeMode.Native, RuntimeBackend.Cuda, "llama-server.exe", "{}", now);
        var launch = new LaunchSettingsViewModel();
        launch.ReplaceRuntimeChoices([runtime]);
        launch.ApplyRuntimeSelectorState(new LaunchRuntimeSelectorState([runtime], "missing-runtime", "missing-runtime"));
        Assert.Equal(2, launch.RuntimeChoices.Count);
        Assert.Equal("CUDA Runtime", launch.RuntimeChoices[1].DisplayName);

        var metrics = new RuntimeMetricsViewModel();
        metrics.ReplaceSamples([
            new PrometheusSample("z_metric", "", 1.25, "", "gauge", "last"),
            new PrometheusSample("a_metric", "slot=1", 9, "raw", "counter", "first")
        ]);
        Assert.Equal("a_metric", metrics.Rows[0].C1);

        var runtimesVm = new RuntimesPageViewModel();
        runtimesVm.ReplaceRows([new RuntimeCatalogRow { Name = "Runtime", Backend = "CUDA", State = "Ready", Location = root, Details = "Test" }]);
        var buildsVm = new RuntimeBuildsPageViewModel();
        buildsVm.ReplaceRows([new RuntimeBuildPresetRow { Label = "Build" }]);
        var packagesVm = new RuntimePackagesPageViewModel();
        packagesVm.ReplaceRows([new RuntimePackagePresetRow { Label = "Package" }]);
        Assert.Single(runtimesVm.Rows);
        Assert.Single(buildsVm.Rows);
        Assert.Single(packagesVm.Rows);

        var lifetime = new LifetimeMetricsViewModel();
        lifetime.ReplaceRows([]);
        Assert.Empty(lifetime.Rows);
        lifetime.ReplaceRows([new TokenUsageRecord(model.Id, model.Name, 10, 15, now)]);
        Assert.Equal("15", lifetime.Rows[0].C5);
        Assert.Equal("25", lifetime.Rows[0].C6);

        var wsl = new WslLinuxPageViewModel();
        wsl.ReplaceDistroRows(new WslEnvironmentReport(false, false, "Missing", "", "", "Ubuntu-24.04", "Install Ubuntu", []), "");
        Assert.Equal("No Linux distro detected", wsl.Rows[0].C2);
        wsl.ReplaceDistroRows(new WslEnvironmentReport(true, true, "Ready", "", "Ubuntu-24.04", "Ubuntu-24.04", "Use it", [
            new WslDistroInfo("Debian", "Running", "2", false, false),
            new WslDistroInfo("Ubuntu-24.04", "Stopped", "2", true, true)
        ]), "Ubuntu-24.04");
        Assert.Equal("Selected", wsl.Rows[0].C6);

        var windows = new WindowsPageViewModel();
        windows.ReplaceToolRows(new WindowsToolSnapshot(true, "git", true, "cmake", true, "MSVC", true, "nvcc", true, "CUDA", false, "Vulkan missing", false, "oneAPI missing", false));
        Assert.Equal(4, windows.Rows.Count);

        var hfFile = new HuggingFaceFile("owner/repo", "model-q4.gguf", "model-q4.gguf", "Q4_K_M", 1536, 1234)
        {
            HasVisionProjector = true,
            HasConfig = true,
            HasTokenizer = true,
            CapabilityHints = "vision,reasoning,moe,fim,draft",
            License = "apache-2.0"
        };
        var hf = new HuggingFacePageViewModel();
        hf.ReplaceSearchResults([hfFile], HuggingFaceInstallStateService.BuildInventory([]), defaults.ModelsRoot);
        Assert.Contains("Vision + mmproj", hf.SearchRows[0].C6, StringComparison.Ordinal);
        var job = new JobRecord("job-1", "huggingface-download", JobStatus.Running,
            JsonSerializer.Serialize(new DownloadJobPayload(hfFile, Path.Combine(defaults.ModelsRoot, hfFile.Name), 512, 1024)),
            Path.Combine(root, "job.log"), now, now);
        hf.ReplaceDownloadHistory([job, job with { Id = "other", Kind = "runtime-build" }]);
        Assert.Single(hf.DownloadHistoryRows);
        var downloadRow = hf.DownloadHistoryRows[0];
        hf.ReplaceDownloadHistory([job with { Status = JobStatus.Paused, UpdatedAt = now.AddSeconds(1) }]);
        Assert.Same(downloadRow, hf.DownloadHistoryRows[0]);
        Assert.Equal(JobStatus.Paused.ToString(), downloadRow.C1);

        var logRoot = Path.Combine(root, "logs");
        Directory.CreateDirectory(logRoot);
        var runtimeLog = Path.Combine(logRoot, "llama-server-test.log");
        File.WriteAllText(runtimeLog, "runtime");
        var logs = new LogsViewModel();
        logs.ReplaceLogs([new FileInfo(runtimeLog)], new Dictionary<string, JobRecord>(), runtimeLog, model.Name);
        Assert.Single(logs.Rows);

        var updates = new UpdatesPageViewModel();
        var changes = new List<string?>();
        updates.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        updates.CheckInFlight = true;
        updates.SetLatestUpdate(new AppUpdateInfo(true, "v2.0", "v2.1.0", "Release v2.1.0", new string('x', 1900), "https://example/release", "app.exe", "https://example/download", 123));
        Assert.True(updates.HasAvailableUpdate);
        Assert.True(updates.LatestReleaseText.Length < 1900);
        Assert.Contains(nameof(UpdatesPageViewModel.ActionText), changes);
    }

    private static int FreeCoveragePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private sealed class StaticJsonHandler(string json) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json)
            });
    }
}
#pragma warning restore xUnit1051
