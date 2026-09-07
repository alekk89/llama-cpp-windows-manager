using System.Windows;
using LocalLlmConsole.Models;

namespace LocalLlmConsole.UiTests;

public sealed class WpfBenchmarkWorkspaceTests : WpfUiTestBase
{
    [Fact]
    public async Task QuickSetupUsesSelectedProfileAndKeepsStartVisible()
    {
        await RunStaAsync(() =>
        {
            var (page, controls, profile) = Create();
            var quick = BenchmarksPagePlanService.Build(page, "");
            Assert.Single(quick.ScopeSelections);
            Assert.Equal(profile.Id, quick.ScopeSelections[0].ProfileId);
            Assert.Empty(quick.Serving.ContextSizes);
            Assert.Equal(new BenchmarkPromptGenerationPair(512, 128), Assert.Single(quick.PromptGenerationPairs));
            Assert.Equal(3, quick.Repetitions);
            controls.Workspace!.Root.Measure(new Size(624, 504));
            controls.Workspace.Root.Arrange(new Rect(0, 0, 624, 504));
            controls.Workspace.Root.UpdateLayout();
            var position = controls.RunButton.TranslatePoint(new Point(), controls.Workspace.Root);
            Assert.InRange(position.Y, 0, 504 - controls.RunButton.ActualHeight);
            Assert.True(controls.Root.ViewportHeight > 300);
        });
    }

    [Fact]
    public async Task ImportRetainsIndependentCachesSeedPoliciesAndTemporaryOverrides()
    {
        await RunStaAsync(() =>
        {
            var (page, _, profile) = Create();
            var plan = BenchmarksPagePlanService.Build(page, "") with
            {
                StopActiveSessions = false,
                PreventSystemSleep = false,
                Serving = new()
                {
                    Seed = 7,
                    Temperature = .75,
                    ProfileOverrides = new Dictionary<string, string> { ["TopP"] = "0.8", ["CustomParameters"] = "--threads-batch 12" },
                    SpeculativeCompanionModes = ["auto"]
                },
                Options = new() { CacheTypesK = ["q8_0", "f16"], CacheTypesV = ["f16"], LazyModes = ["off"], TensorSplits = ["3,2"] }
            };
            BenchmarksPagePlanService.Apply(page, plan, [profile]);
            var rebuilt = BenchmarksPagePlanService.Build(page, "");
            Assert.Equal(plan.Options.CacheTypesK, rebuilt.Options.CacheTypesK);
            Assert.Equal(plan.Options.CacheTypesV, rebuilt.Options.CacheTypesV);
            Assert.Empty(rebuilt.Options.CacheTypesKv);
            Assert.Equal(7, rebuilt.Serving.Seed);
            Assert.Equal(.75, rebuilt.Serving.Temperature);
            Assert.Equal(plan.Serving.ProfileOverrides, rebuilt.Serving.ProfileOverrides);
            Assert.Equal(plan.Options.LazyModes, rebuilt.Options.LazyModes);
            Assert.Equal(plan.Options.TensorSplits, rebuilt.Options.TensorSplits);
            Assert.Equal(plan.Serving.SpeculativeCompanionModes, rebuilt.Serving.SpeculativeCompanionModes);
            Assert.False(rebuilt.StopActiveSessions);
            Assert.False(rebuilt.PreventSystemSleep);
        });
    }

    private static (BenchmarksPageState, BenchmarksPageControls, NamedModelLaunchProfile) Create()
    {
        Func<Task> no = () => Task.CompletedTask;
        var actions = new BenchmarksPageActions(no, no, no, no, no, no, no, no, no, no, no, no, no, no, no, no,
            () => 0, _ => Task.CompletedTask, () => { }, _ => { }, () => { }, () => { }, action => action());
        var controls = BenchmarksPageFactory.Create(new(actions));
        var page = new BenchmarksPageState(() => (true, true));
        page.Apply(controls);
        var now = DateTimeOffset.UtcNow;
        var model = new ModelRecord("m", "Model", "m.gguf", OwnershipKind.External, "{}", now);
        var runtime = new RuntimeRecord("r", "Runtime", RuntimeMode.Native, RuntimeBackend.Cpu, "llama-server.exe", "{}", now);
        var profile = new NamedModelLaunchProfile("p", "m", "Profile", ModelLaunchSettings.FromAppSettings(AppSettings.CreateDefault(""), "r"), now, true);
        page.SetCatalog([model], [profile], [runtime]);
        page.UpdateWorkspacePreview();
        return (page, controls, profile);
    }
}
