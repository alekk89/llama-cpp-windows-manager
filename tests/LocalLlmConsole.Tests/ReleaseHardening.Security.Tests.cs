using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed partial class ReleaseHardeningTests
{
    [Fact]
    public void LogFileServiceDescribesValidatesAndPreviewsLogs()
    {
        var root = CreateTempRoot();
        var logRoot = Path.Combine(root, "logs");
        Directory.CreateDirectory(logRoot);
        var runtimeLog = Path.Combine(logRoot, "llama-server-test.log");
        File.WriteAllText(runtimeLog, "loading model 'D:/models/Qwen3.gguf'\n0123456789");
        var outsideLog = Path.Combine(root, "outside.log");
        File.WriteAllText(outsideLog, "outside");
        var now = DateTimeOffset.UtcNow;
        var job = new JobRecord(
            "job-1",
            "runtime-build",
            JobStatus.Completed,
            """{"presetLabel":"CUDA","action":"build","installDir":"D:\\test-runtime"}""",
            runtimeLog,
            now,
            now);

        var runtime = LogFileService.Describe(runtimeLog, null, activeRuntime: false, activeModel: "");
        var runtimeJob = LogFileService.Describe(runtimeLog, job, activeRuntime: false, activeModel: "");

        Assert.Equal(("Model runtime", "Model: Qwen3.gguf"), runtime);
        Assert.Equal("Runtime build", runtimeJob.Type);
        Assert.Contains("build: CUDA", runtimeJob.Related, StringComparison.Ordinal);
        Assert.Equal("loadi", LogFileService.Head(runtimeLog, 5));
        Assert.Equal("56789", LogFileService.Tail(runtimeLog, 5));
        var bomLog = Path.Combine(logRoot, "bom.log");
        File.WriteAllText(bomLog, "\u03A9mega", new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        Assert.Equal("\u03A9me", LogFileService.Head(bomLog, 3));
        Assert.True(LogFileService.TryValidateWorkspaceLogFile(root, runtimeLog, out var fullPath, out var error));
        Assert.Equal(Path.GetFullPath(runtimeLog), fullPath);
        Assert.Equal("", error);
        Assert.False(LogFileService.TryValidateWorkspaceLogFile(root, outsideLog, out _, out error));
        Assert.Contains("workspace logs folder", error, StringComparison.OrdinalIgnoreCase);

        var appLog = Path.Combine(logRoot, "app-test.log");
        File.WriteAllText(appLog, "app");
        var deletionPlan = LogFileService.BuildDeletionPlan(root, [runtimeLog, appLog, outsideLog, appLog], runtimeLog);
        Assert.Single(deletionPlan.DeletablePaths);
        Assert.Equal(Path.GetFullPath(appLog), deletionPlan.DeletablePaths[0]);
        Assert.Equal(2, deletionPlan.SkippedCount);
        Assert.Equal(1, LogFileService.DeleteLogs(deletionPlan.DeletablePaths));
        Assert.False(File.Exists(appLog));
        Assert.True(File.Exists(runtimeLog));
        Assert.Equal("Deleted 1 selected log file; skipped 2 active or unsafe files.", LogFileService.FormatDeletionStatus(1, 2, "selected log file"));

        Assert.Equal(
            "token [redacted] --api-key [redacted]\nAuthorization: Bearer [redacted]",
            LogFileService.RedactSensitiveText("token secret-key --api-key secret-key\nAuthorization: Bearer secret-key", "secret-key"));
        Assert.Equal(
            $"start{Environment.NewLine}... omitted 2 repeated 'all slots are idle' lines{Environment.NewLine}done",
            LogFileService.CollapseIdleSlotNoise("start\nall slots are idle\nALL SLOTS ARE IDLE\ndone"));
    }


    [Fact]
    public void ApiSecurityRejectsNullOriginAndNonLoopbackHostHeaders()
    {
        var security = new ApiSecurity();

        Assert.False(security.IsLocalOriginAllowed("null"));
        Assert.True(security.IsLocalOriginAllowed("http://127.0.0.1:8090"));
        Assert.True(security.IsLocalOriginAllowed("http://127.0.0.2:8090"));
        Assert.False(security.IsLocalOriginAllowed("https://example.com"));
        Assert.True(security.IsLocalHostHeaderAllowed("127.0.0.1:8090", 8090));
        Assert.True(security.IsLocalHostHeaderAllowed("127.0.0.2:8090", 8090));
        Assert.False(security.IsLocalHostHeaderAllowed("0.0.0.0:8090", 8090));
        Assert.True(security.IsLocalHostHeaderAllowed("localhost:8090", 8090));
        Assert.False(security.IsLocalHostHeaderAllowed("example.com:8090", 8090));
        Assert.False(security.IsLocalHostHeaderAllowed("127.0.0.1:8091", 8090));
    }

}
