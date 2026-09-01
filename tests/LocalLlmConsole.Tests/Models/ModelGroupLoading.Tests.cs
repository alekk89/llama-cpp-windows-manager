using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class ModelGroupLoadingTests : ManagerRegressionTestBase
{
    [Fact]
    public void OverviewGroupLoadPreflightsAggregateVramBeforeAnyModelStarts()
    {
        var root = CreateTempRoot();
        var runtime = Runtime(root, "cuda", RuntimeBackend.Cuda);
        var defaults = AppSettings.CreateDefault(root) with { ContextSize = 4096, GpuLayers = 999 };
        var first = Model(root, "model-a", 2L * 1024 * 1024);
        var second = Model(root, "model-b", 2L * 1024 * 1024);
        var firstProfile = Profile(first, runtime, 8091, defaults);
        var secondProfile = Profile(second, runtime, 8092, defaults);
        var group = Group("Workload");
        var snapshot = Snapshot(group, firstProfile, secondProfile);

        var plan = new OverviewModelGroupLoadPlanningService().Plan(
            group,
            snapshot,
            [firstProfile, secondProfile],
            [first, second],
            [runtime],
            [],
            defaults,
            new VramMemorySnapshot(1.1, 24));

        Assert.False(plan.CanLoad);
        Assert.Equal(2, plan.Targets.Count);
        Assert.True(plan.EstimatedRequiredGiB > plan.AvailableGiB);
        Assert.Contains(plan.Errors, error => error.Contains("Not enough VRAM is available to load all models", StringComparison.Ordinal));
    }

    [Fact]
    public void OverviewGroupLoadAllowsMultipleCpuProfilesForTheSameModel()
    {
        var root = CreateTempRoot();
        var runtime = Runtime(root, "cpu", RuntimeBackend.Cpu);
        var defaults = AppSettings.CreateDefault(root) with { GpuLayers = 0 };
        var model = Model(root, "model-a", 1024);
        var first = Profile(model, runtime, 8091, defaults);
        var second = Profile(model, runtime, 8092, defaults) with { Id = "profile:model-a:second", Name = "Second" };
        var group = Group("CPU tools");

        var oneProfile = new OverviewModelGroupLoadPlanningService().Plan(
            group, Snapshot(group, first), [first], [model], [runtime], [], defaults, null);
        Assert.True(oneProfile.CanLoad);
        Assert.Equal(0, oneProfile.EstimatedRequiredGiB);

        var duplicateModel = new OverviewModelGroupLoadPlanningService().Plan(
            group, Snapshot(group, first, second), [first, second], [model], [runtime], [], defaults, null);
        Assert.True(duplicateModel.CanLoad);
        Assert.Equal(2, duplicateModel.Targets.Count);
    }

    [Fact]
    public void OverviewModelChoicesIncludeLoadableGroupsAfterPhysicalModels()
    {
        var root = CreateTempRoot();
        var defaults = AppSettings.CreateDefault(root);
        var model = Model(root, "model-a", 1024);
        var runtime = Runtime(root, "cpu", RuntimeBackend.Cpu);
        var profile = Profile(model, runtime, 8091, defaults);
        var group = Group("Assistants");
        var snapshot = Snapshot(group, profile);
        var viewModel = new LocalLlmConsole.ViewModels.OverviewPageViewModel();

        viewModel.ReplaceModels(
            [model],
            [group],
            snapshot.Assignments,
            [profile],
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [model.Id] = "1 KB" });

        Assert.Equal(2, viewModel.ModelChoices.Count);
        Assert.Equal(OverviewModelChoiceKind.Model, viewModel.ModelChoices[0].Kind);
        Assert.Equal("model-a · 1 KB", viewModel.ModelChoices[0].DisplayName);
        Assert.Equal(OverviewModelChoiceKind.Group, viewModel.ModelChoices[1].Kind);
        Assert.Equal("Group · Assistants (1)", viewModel.ModelChoices[1].DisplayName);
        Assert.Equal([profile.Id], viewModel.ModelChoices[1].LaunchProfileIds);
    }

    [Fact]
    public async Task OverviewGroupLoadRollsBackOnlyProfilesStartedByTheFailedLoad()
    {
        var root = CreateTempRoot();
        var runtime = Runtime(root, "cpu", RuntimeBackend.Cpu);
        var defaults = AppSettings.CreateDefault(root) with { GpuLayers = 0 };
        var first = Model(root, "model-a", 1024);
        var second = Model(root, "model-b", 1024);
        var firstProfile = Profile(first, runtime, 8091, defaults);
        var secondProfile = Profile(second, runtime, 8092, defaults);
        var group = Group("Rollback");
        var sessions = Array.Empty<LoadedModelSessionSnapshot>();
        var plan = new OverviewModelGroupLoadPlanningService().Plan(
            group,
            Snapshot(group, firstProfile, secondProfile),
            [firstProfile, secondProfile],
            [first, second],
            [runtime],
            sessions,
            defaults,
            null);
        Assert.True(plan.CanLoad);

        var events = new List<string>();
        var newStartAttempts = 0;
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new OverviewModelGroupLoadApplicationService().ExecuteAsync(
                plan,
                sessions,
                [first, second],
                [runtime],
                new OverviewModelGroupLoadApplicationActions(
                    (sessionId, _) =>
                    {
                        events.Add($"stop:{sessionId}");
                        return Task.CompletedTask;
                    },
                    (_, model, settings, profileId, _, _) =>
                    {
                        events.Add($"start:{model.Id}:{settings.Port}:{profileId}");
                        if (profileId.StartsWith("profile:", StringComparison.OrdinalIgnoreCase))
                            return Task.FromResult(++newStartAttempts < 2);
                        return Task.FromResult(true);
                    }),
                TestContext.Current.CancellationToken));

        Assert.Contains("profiles started by this group load were stopped", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            [
                $"start:model-a:8091:{firstProfile.Id}",
                $"start:model-b:8092:{secondProfile.Id}",
                $"stop:{LoadedModelSessionManager.SessionIdFor(first.Id, firstProfile.Id)}"
            ],
            events);
    }

    private static RuntimeRecord Runtime(string root, string id, RuntimeBackend backend)
    {
        var executable = Path.Combine(root, $"{id}.exe");
        File.WriteAllBytes(executable, [1]);
        return new RuntimeRecord($"runtime:{id}", id, RuntimeMode.Native, backend, executable, "{}", DateTimeOffset.UtcNow);
    }

    private static ModelRecord Model(string root, string id, long size)
    {
        var path = Path.Combine(root, $"{id}.gguf");
        using (var stream = File.Create(path))
            stream.SetLength(size);
        return new ModelRecord(id, id, path, OwnershipKind.External, "{}", DateTimeOffset.UtcNow);
    }

    private static NamedModelLaunchProfile Profile(ModelRecord model, RuntimeRecord runtime, int port, AppSettings defaults)
        => new(
            $"profile:{model.Id}",
            model.Id,
            $"{model.Name} profile",
            ModelLaunchSettings.FromAppSettings(defaults with { Port = port }, runtime.Id),
            DateTimeOffset.UtcNow);

    private static ModelGroupRecord Group(string name)
        => new($"group:{name.ToLowerInvariant().Replace(' ', '-')}", name, ModelGroupRetentionMode.Inherit, 30, ModelGroupEvictionPriority.Normal, DateTimeOffset.UtcNow);

}
