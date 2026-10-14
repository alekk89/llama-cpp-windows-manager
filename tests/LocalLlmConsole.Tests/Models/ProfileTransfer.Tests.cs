using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LocalLlmConsole.Tests;

#pragma warning disable xUnit1051

public sealed class ProfileTransferTests : ManagerRegressionTestBase
{
    [Fact]
    public void BundleRoundTripPreservesEveryPortableSettingAndAnnotations()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(CreateTempRoot()), "machine-runtime") with
        { CustomParameters = "--chat-template jinja --dry-multiplier 0.8", Host = "127.0.0.1", ReasoningBudgetMessage = "reasoning test" };
        var annotations = new ProfileAnnotations("For long documents", new Dictionary<string, string> { ["contextSize"] = "VRAM limited" });
        var bundle = Bundle(settings, annotations);
        var read = ProfileBundleCodec.Read("// Portable settings\n" + ProfileBundleCodec.Write(bundle).Replace("\"schemaVersion\": 1,", "\"schemaVersion\": 1, // v1\n"));
        var restored = ProfileBundleCodec.ApplySettings(ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault("other")), read.Profiles[0].Settings, portable: true);
        Assert.Equal(settings with { RuntimeId = "" }, restored);
        Assert.Equal(annotations.Notes, read.Profiles[0].Annotations!.Notes);
        Assert.Equal("VRAM limited", read.Profiles[0].Annotations!.SettingNotes!["contextSize"]);
        Assert.False(read.Profiles[0].Settings.ContainsKey("runtimeId"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{\"schemaVersion\":999,\"model\":{},\"profiles\":[]}")]
    [InlineData("{\"schemaVersion\":1,\"schemaVersion\":1}")]
    [InlineData("{broken}")]
    [InlineData("{\"schemaVersion\":1,\"model\":{},\"profiles\":[null]}")]
    public void MalformedAndUnsupportedBundlesAreRejected(string text)
        => Assert.Throws<InvalidOperationException>(() => ProfileBundleCodec.Read(text));

    [Fact]
    public void CredentialsUnknownSettingsAndNullValuesAreRejected()
    {
        var defaults = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(CreateTempRoot()));
        Assert.Throws<InvalidOperationException>(() => ProfileBundleCodec.PortableSettings(defaults with { CustomParameters = "--api-key secret" }));
        foreach (var patch in new[] { new JsonObject { ["modelApiKey"] = "secret" }, new JsonObject { ["contextSize"] = null }, new JsonObject { ["port"] = 65536 }, new JsonObject { ["topP"] = 2.0 }, new JsonObject { ["batchSize"] = "bad" } })
            Assert.Throws<InvalidOperationException>(() => ProfileBundleCodec.ApplySettings(defaults, patch));
        Assert.Throws<InvalidOperationException>(() => ProfileBundleCodec.Read(new string('x', ProfileBundleCodec.MaximumBytes + 1)));
    }

    [Fact]
    public void ExportRemovesMachinePathsAndRetainsEmbeddedReferences()
    {
        var settings = ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(CreateTempRoot())) with
        { VisionProjectorPath = "C:\\source\\mmproj-model.gguf", SpecDraftModelPath = "/source/draft.gguf", MtpHeadPath = "embedded" };
        var node = ProfileBundleCodec.PortableSettings(settings);
        Assert.Equal("mmproj-model.gguf", node["visionProjectorPath"]!.GetValue<string>());
        Assert.Equal("draft.gguf", node["specDraftModelPath"]!.GetValue<string>());
        Assert.Equal("embedded", node["mtpHeadPath"]!.GetValue<string>());
    }

    [Fact]
    public async Task KeepBothAllocatesFreshIdsNamesAndBatchPortsWithoutChangingSource()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db")); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, settings) = await SeedAsync(store, root);
        var workflow = new ProfileTransferWorkflowService(store, sessions);
        var source = (await store.ListNamedModelLaunchProfilesAsync(model.Id))[0];
        var bundle = await workflow.ExportAsync(model);
        Assert.Equal("qwen3", bundle.Model.Architecture);
        bundle = bundle with { Profiles = [bundle.Profiles[0], bundle.Profiles[0]] };
        var preview = await workflow.PreviewAsync(model, bundle, settings);
        Assert.True(preview.CanApply);
        Assert.Equal(new[] { "Default (2)", "Default (3)" }, preview.Rows.Select(row => row.Name));
        Assert.Equal(2, preview.Rows.Select(row => row.Settings.Port).Distinct().Count());
        var imported = await workflow.ApplyAsync(model, bundle, settings, [], preview);
        Assert.All(imported, profile => { Assert.NotEqual(source.Id, profile.Id); Assert.False(profile.IsDefault); });
        Assert.Equal(JsonSerializer.Serialize(source), JsonSerializer.Serialize(await store.GetNamedModelLaunchProfileAsync(source.Id)));
    }

    [Fact]
    public async Task ReplacePreservesDefaultIdStartupAndGroupReferencesAndNotes()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db")); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, defaults) = await SeedAsync(store, root);
        var original = (await store.ListNamedModelLaunchProfilesAsync(model.Id))[0];
        await store.SetStartupLaunchProfileAsync(original.Id, true);
        var groups = new ModelGroupService(store);
        var group = await groups.CreateAsync("Interactive");
        await groups.AssignAsync(original.Id, group.Id);
        var workflow = new ProfileTransferWorkflowService(store, sessions);
        var bundle = await workflow.ExportAsync(model);
        var annotations = new ProfileAnnotations("Imported notes", new Dictionary<string, string> { ["topK"] = "Experiment" });
        var portable = bundle.Profiles[0] with { Annotations = annotations };
        portable.Settings["topK"] = 31;
        bundle = bundle with { Profiles = [portable] };
        var choices = new[] { new ProfileImportChoice(0, ProfileConflictAction.Replace) };
        var preview = await workflow.PreviewAsync(model, bundle, defaults, choices);
        Assert.Equal(original.Settings.Port, preview.Rows[0].Settings.Port);
        var imported = Assert.Single(await workflow.ApplyAsync(model, bundle, defaults, choices, preview));
        Assert.Equal(original.Id, imported.Id); Assert.True(imported.IsDefault); Assert.Equal(31, imported.Settings.TopK);
        Assert.Contains(original.Id, await store.ListStartupLaunchProfileIdsAsync());
        Assert.Equal(group.Id, (await groups.SnapshotAsync()).GroupForProfile(imported.Id)!.Id);
        // An ordinary settings save must retain imported annotations.
        await store.SaveNamedModelLaunchProfileAsync(imported with { Annotations = null });
        Assert.Equal("Imported notes", (await workflow.ExportAsync(model)).Profiles[0].Annotations!.Notes);
    }

    [Fact]
    public async Task StalePreviewIsRejectedAndDatabaseBatchRollsBack()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db")); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, defaults) = await SeedAsync(store, root);
        var workflow = new ProfileTransferWorkflowService(store, sessions);
        var bundle = await workflow.ExportAsync(model);
        var preview = await workflow.PreviewAsync(model, bundle, defaults);
        var original = Assert.Single(await store.ListNamedModelLaunchProfilesAsync(model.Id));
        await store.SaveNamedModelLaunchProfileAsync(original with { UpdatedAt = DateTimeOffset.UtcNow });
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(model, bundle, defaults, [], preview));
        var fresh = original with { Id = "new-one", Name = "New one", IsDefault = false };
        await Assert.ThrowsAnyAsync<Exception>(() => store.ApplyProfileImportAsync(model.Id, store.CatalogRevision,
            [fresh, fresh with { Id = "new-two" }])); // UNIQUE name failure after the first insert.
        Assert.Null(await store.GetNamedModelLaunchProfileAsync("new-one"));
        Assert.Single(await store.ListNamedModelLaunchProfilesAsync(model.Id));
    }

    [Fact]
    public async Task MissingCompanionsRequireResolutionAndGpuSelectionsAreRemapped()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db")); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, defaults) = await SeedAsync(store, root);
        var workflow = new ProfileTransferWorkflowService(store, sessions, () => Task.FromResult<IReadOnlyList<string>>(["GPU B"]));
        var bundle = Bundle(ModelLaunchSettings.FromAppSettings(defaults) with { VisionProjectorPath = "missing.gguf", GpuDevices = "0,1", GpuSplit = "1,1" });
        bundle = bundle with { Profiles = [bundle.Profiles[0] with { GpuNames = ["GPU A"] }] };
        var preview = await workflow.PreviewAsync(model, bundle, defaults);
        Assert.False(preview.CanApply);
        Assert.Empty(preview.Rows[0].Settings.GpuDevices);
        Assert.Contains(preview.Rows[0].Warnings, warning => warning.Contains("GPU layout"));
        var choices = new[] { new ProfileImportChoice(0, Settings: new JsonObject { ["visionProjectorPath"] = "" }) };
        var resolved = await workflow.PreviewAsync(model, bundle, defaults, choices);
        Assert.True(resolved.CanApply);
        var invalidGpu = await workflow.PreviewAsync(model, bundle, defaults, [new(0, Settings: new JsonObject
        { ["visionProjectorPath"] = "", ["gpuDevices"] = "CUDA0,CUDA1", ["gpuSplit"] = "0,0" })]);
        Assert.False(invalidGpu.CanApply);
        var skipped = await workflow.PreviewAsync(model, bundle, defaults, [new(0, ProfileConflictAction.Skip)]);
        Assert.Empty(skipped.Rows); Assert.False(skipped.CanApply);
    }

    [Fact]
    public async Task RuntimeAndEditedPreviewMustRemainValidAtApply()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db")); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, defaults) = await SeedAsync(store, root);
        var workflow = new ProfileTransferWorkflowService(store, sessions);
        var bundle = await workflow.ExportAsync(model);
        var preview = await workflow.PreviewAsync(model, bundle, defaults);
        Assert.Equal("local-cpu", preview.Rows[0].Settings.RuntimeId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ApplyAsync(model, bundle, defaults,
            [new(0, Name: "Different")], preview));
        var invalid = await workflow.PreviewAsync(model, bundle, defaults, [new(0, RuntimeId: "missing-runtime")]);
        Assert.False(invalid.CanApply);
        await store.DeleteRuntimeAsync("local-cpu");
        var unavailable = await workflow.PreviewAsync(model, bundle, defaults);
        Assert.False(unavailable.CanApply);
    }

    [Fact]
    public async Task ActiveVisionOrSpeculationCannotImportWithoutRequiredCompanions()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db")); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (model, defaults) = await SeedAsync(store, root);
        var workflow = new ProfileTransferWorkflowService(store, sessions);
        foreach (var settings in new[]
        {
            ModelLaunchSettings.FromAppSettings(defaults) with { VisionMode = "on" },
            ModelLaunchSettings.FromAppSettings(defaults) with { SpeculativeType = "draft-mtp" },
            ModelLaunchSettings.FromAppSettings(defaults) with { SpeculativeType = "atomic-mtp" }
        })
            Assert.False((await workflow.PreviewAsync(model, Bundle(settings), defaults)).CanApply);
        Assert.Throws<InvalidOperationException>(() => ProfileBundleCodec.ApplySettings(ModelLaunchSettings.FromAppSettings(defaults), new JsonObject { ["cacheTypeK"] = "unknown-cache" }));
        var incompatible = Path.Combine(root, "mmproj-llama-3.gguf"); WriteMinimalGguf(incompatible);
        Assert.False(ModelCatalogService.IsCompatibleCompanion("qwen-3-8b.gguf", incompatible, "", vision: true));
        Assert.False(ModelCatalogService.IsCompatibleCompanion(model.ModelPath, incompatible, "draft-mtp"));
    }

    private static ProfileBundle Bundle(ModelLaunchSettings settings, ProfileAnnotations? annotations = null)
        => new(1, new("Source", "source.gguf", "qwen3"), [new("Default", ProfileBundleCodec.PortableSettings(settings), new("CPU", "Cpu", "Native"), annotations)]);

    [Fact]
    public async Task CopyIntoAnotherModelCreatesItsOwnDefaultAndKeepsSourceUntouched()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db")); await store.InitializeAsync();
        using var sessions = CreateLoadedModelSessionManager();
        var (source, defaults) = await SeedAsync(store, root);
        var targetPath = Path.Combine(root, "other-quant.gguf"); WriteMinimalGguf(targetPath);
        var target = source with { Id = "other-model", Name = "Other quantization", ModelPath = targetPath };
        await store.UpsertModelAsync(target);
        var workflow = new ProfileTransferWorkflowService(store, sessions);
        var sourceProfile = Assert.Single(await store.ListNamedModelLaunchProfilesAsync(source.Id));
        var bundle = await workflow.ExportAsync(source, sourceProfile.Id);
        var preview = await workflow.PreviewAsync(target, bundle, defaults);
        Assert.True(preview.CanApply);
        Assert.Contains(preview.Rows[0].Warnings, warning => warning.Contains("Destination GGUF differs"));
        var imported = Assert.Single(await workflow.ApplyAsync(target, bundle, defaults, [], preview));
        Assert.Equal(target.Id, imported.ModelId); Assert.True(imported.IsDefault);
        Assert.NotEqual(sourceProfile.Id, imported.Id);
        Assert.Equal(sourceProfile.Settings with { Port = imported.Settings.Port }, imported.Settings);
        Assert.Equal(sourceProfile, Assert.Single(await store.ListNamedModelLaunchProfilesAsync(source.Id)));
    }

    private static async Task<(ModelRecord, AppSettings)> SeedAsync(StateStore store, string root)
    {
        var defaults = AppSettings.CreateDefault(root);
        var modelPath = Path.Combine(root, "target.gguf"); WriteMinimalGguf(modelPath);
        var model = new ModelRecord("model", "Target", modelPath, OwnershipKind.External, "{\"ggufArchitecture\":\"qwen3\"}", DateTimeOffset.UtcNow);
        await store.UpsertModelAsync(model);
        var executable = Path.Combine(root, "llama-server.exe"); await File.WriteAllTextAsync(executable, "stub");
        await store.UpsertRuntimeAsync(new RuntimeRecord("local-cpu", "CPU", RuntimeMode.Native, RuntimeBackend.Cpu, executable, "{}", DateTimeOffset.UtcNow));
        await store.SaveNamedModelLaunchProfileAsync(new("existing", model.Id, "Default",
            ModelLaunchSettings.FromAppSettings(defaults, "local-cpu"), DateTimeOffset.UtcNow, true));
        return (model, defaults);
    }
}
