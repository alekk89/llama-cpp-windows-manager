using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class SettingsIsolationTests : ManagerRegressionTestBase
{
    [Fact]
    public void StartupRegistrationOnlyDisablesItsOwnInstallation()
    {
        var production = WindowsStartupRegistrationService.StartupCommandForExecutable(@"D:\production\Manager.exe");
        string? command = production;
        var writes = 0;
        var service = new WindowsStartupRegistrationService(() => command, value => { writes++; command = value; },
            () => { writes++; command = null; }, () => @"D:\development\Manager.exe");
        Assert.False(service.IsEnabled());
        Assert.False(service.Reconcile(AppSettings.CreateDefault(CreateTempRoot()) with { StartWithWindows = true }).StartWithWindows);
        Assert.True(service.Apply(false).Success);
        Assert.Equal(production, command);
        Assert.Equal(0, writes);
        Assert.True(service.Apply(true).Success);
        Assert.True(service.IsEnabled());
        Assert.True(service.Apply(false).Success);
        Assert.Null(command);
        Assert.Equal(2, writes);
    }

    [Fact]
    public async Task PresentationSavePreservesForeignStartupRegistrationAndRunningGateway()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state.db"));
        await store.InitializeAsync();
        var current = AppSettings.CreateDefault(root) with { ModelApiKey = new string('a', 64), ModelApiKeyBackup = new string('a', 64) };
        var writes = 0;
        var service = new AppSettingsApplicationService(new AppSettingsWorkflowService(store, new AppSettingsUpdateService(), root),
            new WindowsStartupRegistrationService(() => @"""D:\production\Manager.exe""", _ => writes++, () => writes++, () => @"D:\development\Manager.exe"));
        var restarts = 0;
        var outcome = await service.SaveEditedAndApplyAsync(new(current, "dark", new Dictionary<string, string>
        { ["uiScalePercent"] = "125", ["fontScalePercent"] = "110", ["showModelsHuggingFace"] = "Show" }, []),
            new(_ => { }, _ => { }, () => { }, () => { restarts++; return Task.FromResult(true); }, () => false, () => { }, _ => { }),
            TestContext.Current.CancellationToken);
        Assert.NotEqual(AppSettingsSaveApplicationOutcome.Failed, outcome);
        Assert.Equal(0, writes);
        Assert.Equal(0, restarts);
    }

    [Fact]
    public void GatewayRestartsOnlyForEffectiveHostOptions()
    {
        var current = AppSettings.CreateDefault(CreateTempRoot()) with { ModelApiKey = new string('a', 64), AutoLoadGatewayPolicy = "keepLoaded" };
        foreach (var updated in new[] { current, current with { UiScalePercent = 150, FontScalePercent = 125 },
                     current with { ShowModelsHuggingFace = !current.ShowModelsHuggingFace }, current with { ModelAccessMode = "models" } })
            Assert.False(AppSettingsApplicationService.GatewaySettingsChanged(current, updated));
        foreach (var updated in new[] { current with { AutoLoadGatewayPort = current.AutoLoadGatewayPort + 1 },
                     current with { ModelAccessMode = "gateway" }, current with { RequireApiKeyAuth = false },
                     current with { ModelApiKey = new string('b', 64) }, current with { GatewayAutoLoadModels = !current.GatewayAutoLoadModels },
                     current with { AutoLoadGatewayEnabled = !current.AutoLoadGatewayEnabled }, current with { AutoLoadGatewayPolicy = "singleActive" } })
            Assert.True(AppSettingsApplicationService.GatewaySettingsChanged(current, updated));
    }
}
