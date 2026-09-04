using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class SameModelLoadChoiceTests : ManagerRegressionTestBase
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacementAdmissionIsDecidedBeforeStoppingAnySession(bool cancel)
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(path, [1]);
        var model = new ModelRecord("model", "Model", path, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var runtime = new RuntimeRecord("vulkan", "Vulkan", RuntimeMode.Native, RuntimeBackend.Vulkan, CreateRuntimeExecutable(root), "{}", DateTimeOffset.UtcNow);
        var settings = AppSettings.CreateDefault(root) with { Port = 8123, SameModelLoadPolicy = "replace" };
        using var sessions = CreateLoadedModelSessionManager();
        sessions.AttachExisting(runtime, model, settings with { Port = 8121 }, "old.log", LlamaRuntimeState.Loaded, "", "old", DateTimeOffset.UtcNow, launchProfileId: "first");
        sessions.AttachExisting(runtime, model with { Id = "other" }, settings with { Port = 8122 }, "other.log", LlamaRuntimeState.Loaded, "", "other", DateTimeOffset.UtcNow);
        var prerequisites = new RuntimeLaunchPrerequisiteService(
            _ => Task.FromResult(ReadyWslReport()), () => WindowsBuildTools(),
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", "")), (_, _) => Task.FromResult(false), () => "wsl.exe");
        var service = new ModelRuntimeLaunchPreparationService(new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs")),
            prerequisites, new RuntimeLaunchAdmissionService(new VramAdmissionService()),
            new GpuStatusProbeService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var prompted = false;
        var request = new ModelRuntimeLaunchPreparationRequest(runtime, model, settings, true, false, 8082,
            (value, _) => Task.FromResult(value), (_, _) => Task.FromResult(false), (plan, _) =>
            {
                prompted = true;
                Assert.Equal(2, sessions.Snapshots().Count(session => session.IsRunning));
                Assert.Contains("Replace 1 loaded profile", plan.InteractiveMessage, StringComparison.Ordinal);
                if (cancel) cancellation.Cancel();
                return Task.FromResult(cancel);
            }, LaunchProfileId: "second");
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.PrepareAsync(request, cancellation.Token));
        else
            Assert.False((await service.PrepareAsync(request, cancellation.Token)).CanLaunch);
        Assert.True(prompted);
        Assert.Equal(2, sessions.Snapshots().Count(session => session.IsRunning));
    }

    [Theory]
    [InlineData("ask", true, SameModelProfileLoadChoice.Cancel, false, true, 1)]
    [InlineData("ask", true, SameModelProfileLoadChoice.Alongside, true, true, 1)]
    [InlineData("ask", true, SameModelProfileLoadChoice.Replace, true, false, 1)]
    [InlineData("alongside", true, SameModelProfileLoadChoice.Cancel, true, true, 0)]
    [InlineData("replace", true, SameModelProfileLoadChoice.Cancel, true, false, 0)]
    [InlineData("replace", false, SameModelProfileLoadChoice.Cancel, true, true, 0)]
    [InlineData("ask", false, SameModelProfileLoadChoice.Cancel, true, true, 0)]
    public async Task IndividualLoadsRespectChoiceWithoutAffectingOtherModelsOrAutomation(
        string policy, bool interactive, SameModelProfileLoadChoice choice, bool canLaunch, bool keepsExisting, int expectedPrompts)
    {
        var root = CreateTempRoot();
        var path = Path.Combine(root, "model.gguf");
        File.WriteAllBytes(path, [1]);
        var runtime = new RuntimeRecord("cpu", "CPU", RuntimeMode.Native, RuntimeBackend.Cpu, CreateRuntimeExecutable(root), "{}", DateTimeOffset.UtcNow);
        var model = new ModelRecord("model", "Model", path, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
        var settings = AppSettings.CreateDefault(root) with { Port = 8103, GpuLayers = 0, SameModelLoadPolicy = policy };
        using var sessions = CreateLoadedModelSessionManager();
        foreach (var index in new[] { 1, 2 })
            sessions.AttachExisting(runtime, model, settings with { Port = 8100 + index }, "test.log", LlamaRuntimeState.Loaded, "",
                LoadedModelSessionManager.SessionIdFor(model.Id, $"profile-{index}"), DateTimeOffset.UtcNow,
                launchProfileId: $"profile-{index}", launchProfileName: $"GPU {index}");
        sessions.AttachExisting(runtime, model with { Id = "other" }, settings with { Port = 8104 }, "other.log", LlamaRuntimeState.Loaded, "",
            "other-session", DateTimeOffset.UtcNow, launchProfileId: "other-profile");
        var prompts = 0;
        var prerequisites = new RuntimeLaunchPrerequisiteService(
            _ => Task.FromResult(ReadyWslReport()), () => WindowsBuildTools(),
            new ScriptedProcessRunner(_ => new ProcessRunResult(0, "ok", "")), (_, _) => Task.FromResult(false), () => "wsl.exe");
        var service = new ModelRuntimeLaunchPreparationService(new RuntimeSessionCoordinator(sessions, Path.Combine(root, "logs")),
            prerequisites, new RuntimeLaunchAdmissionService(new VramAdmissionService()),
            new GpuStatusProbeService(new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""))));
        var result = await service.PrepareAsync(new ModelRuntimeLaunchPreparationRequest(
            runtime, model, settings, interactive, false, 8082,
            (value, _) => Task.FromResult(value), (_, _) => Task.FromResult(false),
            LaunchProfileId: "profile-3", ChooseSameModelLoadAsync: (_, existing, _) =>
            {
                prompts++;
                Assert.Equal(2, existing.Count);
                Assert.All(existing, session => Assert.Equal(model.Id, session.ModelId));
                return Task.FromResult(choice);
            }), TestContext.Current.CancellationToken);
        Assert.Equal(canLaunch, result.CanLaunch);
        Assert.Equal(expectedPrompts, prompts);
        Assert.Equal(keepsExisting ? 2 : 0, sessions.SessionsForModel(model.Id).Count);
        Assert.True(sessions.SessionForModel("other")?.IsRunning);
    }
}
