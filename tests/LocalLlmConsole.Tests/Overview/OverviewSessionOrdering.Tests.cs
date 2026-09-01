using LocalLlmConsole.Models;
using LocalLlmConsole.ViewModels;

namespace LocalLlmConsole.Tests;

public sealed class OverviewSessionOrderingTests : ManagerRegressionTestBase
{
    [Fact]
    public void SelectingLoadedSessionDoesNotMoveItsRow()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var now = DateTimeOffset.UtcNow;
        LoadedModelSessionSnapshot Session(string id, string modelId, string name, bool selected) => new(
            id, modelId, name, "runtime", "CUDA", RuntimeMode.Native, RuntimeBackend.Cuda,
            settings, Path.Combine(root, $"{id}.log"), now, "", 1,
            LoadedModelSessionStatus.Running, true, selected, 1024, $"profile:{id}", "Default");
        var alpha = Session("session-alpha", "alpha", "Alpha", selected: false);
        var zulu = Session("session-zulu", "zulu", "Zulu", selected: true);
        var viewModel = new OverviewPageViewModel();

        viewModel.ReplaceSessions([zulu, alpha]);
        Assert.Equal(["session-alpha", "session-zulu"], viewModel.SessionRows.Select(row => row.SessionId));

        viewModel.ReplaceSessions([zulu with { IsSelected = false }, alpha with { IsSelected = true }]);
        Assert.Equal(["session-alpha", "session-zulu"], viewModel.SessionRows.Select(row => row.SessionId));
        Assert.Contains("selected", viewModel.SessionRows[0].ModelName, StringComparison.OrdinalIgnoreCase);
    }
}
