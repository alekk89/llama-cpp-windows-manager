using System.Diagnostics;
using System.Xml.Linq;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

public sealed class CoverageGateTests : ManagerRegressionTestBase
{
    [Fact]
    public async Task CoverageMergesReportsWithoutCollapsingProjectsOrCountingGeneratedCode()
    {
        var root = CreateTempRoot();
        WriteReports(root);
        var result = await MeasureAsync(root, "-MinimumCliLineCoverage", "50");
        Assert.True(result.ExitCode == 0, result.Output);
        var rows = File.ReadAllLines(Path.Combine(root, "coverage-by-file.csv"))
            .Skip(1).Select(line => line.Split(',').Select(value => value.Trim('"')).ToArray()).ToArray();
        Assert.Equal(4, rows.Length);
        var app = Assert.Single(rows, row => row[0] == "LocalLlmConsole.App" && row[1] == "Services/Shared.cs");
        Assert.Equal(["2", "2", "0", "100"], app[2..]);
        var core = Assert.Single(rows, row => row[0] == "LocalLlmConsole.Core");
        Assert.Equal(["1", "1", "0", "100"], core[2..]);
        var cli = Assert.Single(rows, row => row[0] == "LocalLlmConsole.ControlCli");
        Assert.Equal(["1", "2", "1", "50"], cli[2..]);
    }

    [Theory]
    [InlineData("cli", "Control CLI line coverage 50")]
    [InlineData("services", "Service line coverage 66.7")]
    [InlineData("models", "Model/view-model line coverage 0")]
    [InlineData("missing-cli", "matched no source lines")]
    public async Task CoverageFailsWhenACriticalScopeIsMissingOrBelowItsFloor(string scenario, string expectedError)
    {
        var root = CreateTempRoot();
        WriteReports(root, scenario);
        var result = await MeasureAsync(root);
        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains(expectedError, result.Output, StringComparison.Ordinal);
    }

    private static void WriteReports(string root, string scenario = "cli")
    {
        var classes = new List<XElement>
        {
            Class("C:/repo/src/LocalLlmConsole.App/Services/Shared.cs", (1, 0), (2, 1)),
            Class("LocalLlmConsole.Core/Services/Shared.cs", (1, 1)),
            Class("C:\\repo\\src\\LocalLlmConsole.App\\Models\\Settings.cs", (1, scenario == "models" ? 0 : 1)),
            Class("LocalLlmConsole.ControlCli/obj/Generated.g.cs", (1, 0)),
            Class("LocalLlmConsole.Tests/Services/Test.cs", (1, 0))
        };
        if (scenario != "missing-cli")
            classes.Add(Class("LocalLlmConsole.ControlCli/Command.cs", (1, 1), (2, 0)));
        Report(classes).Save(Path.Combine(root, "coverage-1.cobertura.xml"));
        Report([Class("LocalLlmConsole.App/Services/Shared.cs", (1, scenario == "services" ? 0 : 1))])
            .Save(Path.Combine(root, "coverage-2.cobertura.xml"));
    }

    private static XElement Class(string file, params (int Number, int Hits)[] lines)
        => new("class", new XAttribute("filename", file), new XElement("lines", lines.Select(line =>
            new XElement("line", new XAttribute("number", line.Number), new XAttribute("hits", line.Hits)))));

    private static XDocument Report(IEnumerable<XElement> classes)
        => new(new XElement("coverage", new XElement("packages", new XElement("package", new XElement("classes", classes)))));

    private static async Task<(int ExitCode, string Output)> MeasureAsync(string root, params string[] extra)
    {
        var start = new ProcessStartInfo(HostExecutableResolver.WindowsPowerShellExe())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", FindRepositoryFile("scripts", "measure-test-coverage.ps1"), "-ResultsRoot", root }.Concat(extra))
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        try
        {
            await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            return (process.ExitCode, await output + await error);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
