namespace LocalLlmConsole.Tests;

public sealed class TestWorkspaceTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task CleanupReleasesOnlyTheCompletedWorkspacesSqlitePool()
    {
        var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory();
        await using (var store = new LocalLlmConsole.Services.StateStore(Path.Combine(directory, "state.db")))
            await store.InitializeAsync();

        var otherPath = Path.Combine(CreateTempRoot(), "other.db");
        await using var other = new LocalLlmConsole.Services.StateStore(otherPath);
        await other.InitializeAsync();
        workspace.Complete(passed: true, Assert.Fail);

        Assert.False(Directory.Exists(directory));
        Assert.Empty(await other.ListModelsAsync());
        Assert.True(File.Exists(otherPath));
    }

    [Fact]
    public void SuccessfulWorkspaceCleanupRemovesOnlyItsOwnFiles()
    {
        var workspace = new TestWorkspace();
        var first = workspace.CreateDirectory();
        var second = workspace.CreateDirectory();
        var other = CreateTempRoot();
        File.WriteAllText(Path.Combine(first, "result.txt"), "test output");
        File.WriteAllText(Path.Combine(other, "sentinel.txt"), "preserve");

        workspace.Complete(passed: true, Assert.Fail);

        Assert.False(Directory.Exists(Path.GetDirectoryName(first)));
        Assert.False(Directory.Exists(second));
        Assert.Equal("preserve", File.ReadAllText(Path.Combine(other, "sentinel.txt")));
        Assert.Throws<ObjectDisposedException>(() => workspace.CreateDirectory());
        workspace.Complete(passed: true, Assert.Fail);
    }

    [Fact]
    public void FailedWorkspaceRetainsArtifactsAndReportsTheirLocation()
    {
        var workspace = new TestWorkspace();
        var directory = workspace.CreateDirectory();
        var root = Path.GetDirectoryName(directory)!;
        var messages = new List<string>();
        try
        {
            File.WriteAllText(Path.Combine(directory, "failure.log"), "diagnostic");
            workspace.Complete(passed: false, messages.Add);
            Assert.Equal("diagnostic", File.ReadAllText(Path.Combine(directory, "failure.log")));
            Assert.Contains(root, Assert.Single(messages), StringComparison.Ordinal);
        }
        finally
        {
            // This test deliberately retains a workspace, then cleans its own fixture.
            var allowed = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "LocalLlmConsole.Tests")) + Path.DirectorySeparatorChar;
            Assert.StartsWith(allowed, Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase);
            Directory.Delete(root, recursive: true);
        }
    }
}
