using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class ControlRuntimeOperationsTests : ManagerRegressionTestBase
{
    private static readonly string[] RuntimeControlOperations =
    [
        "runtime.catalog",
        "runtime-repository.add",
        "runtime.delete",
        "runtime-package.install",
        "runtime-package.check",
        "runtime-package.delete",
        "runtime-source.download",
        "runtime-source.check",
        "runtime-source.delete",
        "runtime-build.start",
        "runtime-build.delete",
        "runtime-job.cancel",
        "runtime-job.retry",
        "runtime-job.clear"
    ];

    [Fact]
    public void ControlRuntimeOperationApplicationServiceAdvertisesOnlyItsOwnedOperations()
    {
        Assert.All(RuntimeControlOperations, operation =>
            Assert.True(ControlRuntimeOperationApplicationService.CanHandle(operation), operation));
        Assert.True(ControlRuntimeOperationApplicationService.CanHandle("RUNTIME.CATALOG"));
        Assert.False(ControlRuntimeOperationApplicationService.CanHandle("models.list"));
        Assert.False(ControlRuntimeOperationApplicationService.CanHandle("runtime.unknown"));
    }

    [Fact]
    public async Task ControlRuntimeOperationApplicationServiceRejectsUnknownAndIncompleteRequests()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var service = CreateControlRuntimeOperations(store, root);

        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.ExecuteAsync(
            "runtime.unknown", new JsonObject(), true, TestContext.Current.CancellationToken));
        var missing = await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExecuteAsync(
            "runtime-package.check", new JsonObject(), true, TestContext.Current.CancellationToken));

        Assert.Equal("Operation parameter 'preset' is required.", missing.Message);
    }

    [Fact]
    public async Task ControlRuntimeOperationApplicationServiceReturnsCatalogAndSafeDryRunPlans()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var runtimeFolder = Path.Combine(root, "runtimes", "registered");
        Directory.CreateDirectory(runtimeFolder);
        var runtime = new RuntimeRecord(
            "runtime-control-test",
            "Control Test Runtime",
            RuntimeMode.Native,
            RuntimeBackend.Cpu,
            Path.Combine(runtimeFolder, "llama-server.exe"),
            "{}",
            DateTimeOffset.UtcNow);
        await store.UpsertRuntimeAsync(runtime);
        var service = CreateControlRuntimeOperations(store, root);

        var catalog = JsonSerializer.SerializeToNode(await service.ExecuteAsync(
            "runtime.catalog", new JsonObject(), false, TestContext.Current.CancellationToken))!.AsObject();
        var packagePlan = JsonSerializer.SerializeToNode(await service.ExecuteAsync(
            "runtime-package.check",
            new JsonObject { ["preset"] = "official-prebuilt-windows-cpu" },
            true,
            TestContext.Current.CancellationToken))!.AsObject();
        var buildPlan = JsonSerializer.SerializeToNode(await service.ExecuteAsync(
            "runtime-build.start",
            new JsonObject { ["preset"] = "official-windows-cpu", ["update"] = true },
            true,
            TestContext.Current.CancellationToken))!.AsObject();
        var deletePlan = JsonSerializer.SerializeToNode(await service.ExecuteAsync(
            "runtime.delete",
            new JsonObject { ["runtime"] = runtime.Id },
            true,
            TestContext.Current.CancellationToken))!.AsObject();

        Assert.Contains(catalog["runtimes"]!.AsArray(), item => item!["Id"]!.GetValue<string>() == runtime.Id);
        Assert.True(packagePlan["wouldExecute"]!.GetValue<bool>());
        Assert.Equal("check", packagePlan["action"]!.GetValue<string>());
        Assert.True(buildPlan["wouldBuild"]!.GetValue<bool>());
        Assert.True(buildPlan["update"]!.GetValue<bool>());
        Assert.True(deletePlan["wouldDelete"]!.GetValue<bool>());
        Assert.Single(await store.ListRuntimesAsync());
    }

    [Fact]
    public async Task ControlRuntimeOperationApplicationServiceValidatesRepositoryDraftsDuringDryRun()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var service = CreateControlRuntimeOperations(store, root);

        var result = JsonSerializer.SerializeToNode(await service.ExecuteAsync(
            "runtime-repository.add",
            new JsonObject
            {
                ["label"] = "Audit Runtime",
                ["repo"] = "https://github.com/example/llama.cpp.git",
                ["branch"] = "main",
                ["backend"] = "CPU Windows"
            },
            true,
            TestContext.Current.CancellationToken))!.AsObject();

        Assert.True(result["wouldAdd"]!.GetValue<bool>());
        Assert.Equal("Audit Runtime", result["Preset"]!["Label"]!.GetValue<string>());
        var runtimeRoot = Path.Combine(root, "runtimes");
        Assert.False(Directory.Exists(runtimeRoot)
                     && Directory.EnumerateFiles(runtimeRoot, "custom-runtime-repositories.json", SearchOption.AllDirectories).Any());
    }

    private static ControlRuntimeOperationApplicationService CreateControlRuntimeOperations(StateStore store, string root)
    {
        var settings = AppSettings.CreateDefault(root);
        var dependencies = new ControlRuntimeOperationDependencies(
            store,
            new RuntimeCatalogDataService(),
            new RuntimeCustomRepositoryService(),
            Unused<RuntimeBuildDeletionApplicationService>(),
            Unused<RuntimePackageApplicationService>(),
            Unused<RuntimeSourceApplicationService>(),
            Unused<RuntimeBuildApplicationService>(),
            Unused<RuntimeBuildJobApplicationService>(),
            new RuntimeCatalogSessionState());
        var actions = new ControlRuntimeOperationActions(
            () => settings,
            () => 1_048_576,
            async (_, action) => await action(),
            () => Task.CompletedTask,
            _ => { },
            _ => Task.CompletedTask);
        return new ControlRuntimeOperationApplicationService(dependencies, actions);
    }

    private static T Unused<T>() where T : class
        => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
}
