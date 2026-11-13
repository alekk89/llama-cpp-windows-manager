using System.Diagnostics;
using LocalLlmConsole.Services;

namespace LocalLlmConsole.Tests;

/// <summary>
/// Runs the generated WSL stop scripts under Git Bash against a fake process table.
/// The fake table includes the stopping shell, its ancestors, and a subshell whose
/// command line contains the marker, which is exactly what <c>bash -lc &lt;script&gt;</c>
/// produces inside WSL.
/// </summary>
public sealed class WslProcessKillScriptTests : ManagerRegressionTestBase
{
    private const string Marker = "local-llm-console-llama-0123abcd";
    private const int TargetPid = 4242;
    private const int UnrelatedPid = 5000;

    [Fact]
    public async Task MarkerStopKillsOnlyTheTargetAndVerifiesItsExit()
    {
        var script = WslRuntimeStopService.WslKillByMarkerCommand(Marker);

        var run = await RunAsync(script, Target($"{Marker}\0--port\08081"), removeOnKill: true);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["4242"], run.Killed);
    }

    [Fact]
    public async Task MarkerStopReportsATargetThatSurvivesTheSignal()
    {
        var script = WslRuntimeStopService.WslKillByMarkerCommand(Marker);

        var run = await RunAsync(script, Target($"{Marker}\0--port\08081"), removeOnKill: false);

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(["4242"], run.Killed);
    }

    [Fact]
    public async Task MarkerStopSucceedsWhenOnlyTheStoppingShellMatches()
    {
        var script = WslRuntimeStopService.WslKillByMarkerCommand(Marker);

        var run = await RunAsync(script, null, removeOnKill: true);

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.Killed);
    }

    [Fact]
    public async Task ExecutableAndPortStopExcludesItsOwnShell()
    {
        var script = WslRuntimeStopService.WslKillByExecutableAndPortCommand("/opt/llama/bin/llama-server", 8081);

        var run = await RunAsync(script, Target("/opt/llama/bin/llama-server\0-m\0/m.gguf\0--port\08081"), removeOnKill: true);

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["4242"], run.Killed);
    }

    [Fact]
    public async Task BuildMarkerCleanupKillsEnvironmentMatchesButNotItself()
    {
        var script = CommandLineService.WslKillByEnvironmentMarkerCommand(Marker);
        var target = Target("cmake\0--build\0.", $"PATH=/usr/bin\0LLAMA_CPP_WINDOWS_MANAGER_BUILD_MARKER={Marker}\0");

        var run = await RunAsync(script, target, removeOnKill: true, selfEnvironment: $"LLAMA_CPP_WINDOWS_MANAGER_BUILD_MARKER={Marker}\0");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(["4242"], run.Killed);
    }

    private static FakeProcess Target(string cmdline, string environ = "")
        => new(TargetPid, cmdline, environ);

    private sealed record FakeProcess(int Pid, string CommandLine, string Environment);

    private sealed record ScriptRun(int ExitCode, string[] Killed, string Output);

    private async Task<ScriptRun> RunAsync(string script, FakeProcess? target, bool removeOnKill, string selfEnvironment = "")
    {
        Assert.Contains(WslProcessKillScript.ProcRootAssignment, script, StringComparison.Ordinal);
        var root = CreateTempRoot();
        var fakeProc = Path.Combine(root, "proc");
        Directory.CreateDirectory(fakeProc);
        WriteProcess(fakeProc, new FakeProcess(1, "/init", ""), parent: 0);
        WriteProcess(fakeProc, new FakeProcess(77, "/bin/bash\0-c\0bash -lc " + script, ""), parent: 1);
        WriteProcess(fakeProc, new FakeProcess(UnrelatedPid, "sleep\0100", "PATH=/usr/bin\0"), parent: 1);
        if (target is not null) WriteProcess(fakeProc, target, parent: 1);

        // The stopping shell only learns its own PID at run time, so its fake /proc entry and a
        // forked subshell with the same command line are created by the harness preamble.
        var selfCmdline = "bash\\0-lc\\0" + EscapeForPrintf(script);
        var preamble = string.Join("\n",
            "kill() { echo \"$1\" >> killed.txt; " + (removeOnKill ? "rm -rf \"proc/$1\"; " : "") + "}",
            "mkdir -p \"proc/$$\" proc/9001",
            $"printf '{selfCmdline}' > \"proc/$$/cmdline\"",
            $"printf '{selfCmdline}' > proc/9001/cmdline",
            $"printf '{EscapeForPrintf(selfEnvironment)}' > \"proc/$$/environ\"",
            $"printf '{EscapeForPrintf(selfEnvironment)}' > proc/9001/environ",
            "printf 'PPid:\\t77\\n' > \"proc/$$/status\"",
            "printf 'PPid:\\t%s\\n' \"$$\" > proc/9001/status",
            "");
        var testable = script.Replace(WslProcessKillScript.ProcRootAssignment, "proc_root=proc;", StringComparison.Ordinal);
        await File.WriteAllTextAsync(Path.Combine(root, "stop.sh"), preamble + testable + "\n", TestContext.Current.CancellationToken);

        var startInfo = new ProcessStartInfo(GitBash())
        {
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("stop.sh");
        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var error = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await process.WaitForExitAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(60), TestContext.Current.CancellationToken);
        var killedPath = Path.Combine(root, "killed.txt");
        var killed = File.Exists(killedPath)
            ? (await File.ReadAllLinesAsync(killedPath, TestContext.Current.CancellationToken)).Where(line => line.Length > 0).ToArray()
            : [];
        return new ScriptRun(process.ExitCode, killed, await output + await error);
    }

    private static void WriteProcess(string fakeProc, FakeProcess process, int parent)
    {
        var directory = Path.Combine(fakeProc, process.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "cmdline"), process.CommandLine);
        File.WriteAllText(Path.Combine(directory, "environ"), process.Environment);
        File.WriteAllText(Path.Combine(directory, "status"), $"Name:\tfake\nPPid:\t{parent}\n");
    }

    private static string EscapeForPrintf(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "%%", StringComparison.Ordinal)
            .Replace("'", "'\"'\"'", StringComparison.Ordinal)
            .Replace("\0", "\\0", StringComparison.Ordinal);

    private static string GitBash()
    {
        var candidates = new List<string>();
        foreach (var directory in (System.Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
        {
            if (string.IsNullOrWhiteSpace(directory)) continue;
            var git = Path.Combine(directory.Trim(), "git.exe");
            if (!File.Exists(git)) continue;
            var installRoot = Path.GetDirectoryName(Path.GetDirectoryName(git)!);
            if (installRoot is not null) candidates.Add(Path.Combine(installRoot, "bin", "bash.exe"));
        }
        foreach (var programFiles in new[] { "ProgramW6432", "ProgramFiles" })
        {
            var value = System.Environment.GetEnvironmentVariable(programFiles);
            if (!string.IsNullOrWhiteSpace(value)) candidates.Add(Path.Combine(value, "Git", "bin", "bash.exe"));
        }
        // System32\bash.exe is the WSL launcher; only Git for Windows bash runs the fake process table.
        var bash = candidates.FirstOrDefault(File.Exists);
        Assert.True(bash is not null, "Git for Windows bash.exe is required to run the WSL stop-script tests.");
        return bash;
    }
}
