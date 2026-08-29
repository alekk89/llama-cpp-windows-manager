using System.Diagnostics;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;
using LocalLlmConsole.ViewModels;
using Microsoft.Data.Sqlite;

namespace LocalLlmConsole.Tests;


public sealed class RuntimeBuildJobControlTests : ManagerRegressionTestBase
{
    [Fact]
    public void RuntimeBuildJobServiceCreatesDeterministicBuildPlan()
    {
        var root = CreateTempRoot();
        var settings = AppSettings.CreateDefault(root);
        var preset = new RuntimeBuildPreset("official-cpu", "Official CPU", "https://example.com/llama.cpp.git", "master", false);
        var source = new RuntimeSourceEntry(preset.Id, preset.Label, preset.RepoUrl, preset.Branch, preset.Cuda, Path.Combine(root, "source"), "abcdef1234567890", DateTimeOffset.UtcNow);

        var plan = RuntimeBuildJobService.CreatePlan(preset, update: false, source, settings, new DateTimeOffset(2026, 5, 26, 12, 34, 56, TimeSpan.Zero), "marker");

        Assert.Equal("build", plan.Action);
        Assert.Equal(source.SourceDir, plan.SourceDir);
        Assert.Equal(Path.Combine(settings.CacheRoot, "runtime-builds", "official-cpu-20260526-123456"), plan.BuildDir);
        Assert.Equal(Path.Combine(settings.RuntimeRoot, "official-cpu-20260526-123456"), plan.InstallDir);
        Assert.Equal("marker", plan.ProcessMarker);
        Assert.Contains("abcdef1", plan.QueuedMessage, StringComparison.Ordinal);
    }


    [Fact]
    public void RuntimeBuildJobServiceParsesPayloadAndExposesJobPolicies()
    {
        var root = CreateTempRoot();
        var preset = new RuntimeBuildPreset("official-cuda", "Official CUDA", "https://example.com/llama.cpp.git", "master", true);
        var sourceDir = Path.Combine(root, "runtime-source");
        var payloadJson = RuntimeBuildJobService.Payload(preset, "build", Path.Combine(root, "runtime"), "Building", "marker", "Ubuntu-24.04", sourceDir);
        var now = DateTimeOffset.UtcNow;
        var running = new JobRecord("job-1", "runtime-build", JobStatus.Running, payloadJson, Path.Combine(root, "logs", "job-1.log"), now, now);
        var failed = running with { Id = "job-2", Status = JobStatus.Failed };
        var completed = running with { Id = "job-3", Status = JobStatus.Completed };
        var completedDownload = running with { Id = "job-4", Kind = "runtime-source-download", Status = JobStatus.Completed };
        Directory.CreateDirectory(Path.GetDirectoryName(running.LogPath)!);
        File.WriteAllText(running.LogPath, "[2026-05-26T12:00:00Z] Running: Building\n[ 42%] Building CXX object llama.cpp\n");

        var payload = RuntimeBuildJobService.ParsePayload(payloadJson);
        Assert.NotNull(payload);
        Assert.Equal("official-cuda", payload.Preset.Id);
        Assert.Equal("Official CUDA", payload.Preset.Label);
        Assert.True(payload.Preset.Cuda);
        Assert.Equal("build", payload.Action);
        Assert.Equal("marker", payload.ProcessMarker);
        Assert.Equal("Ubuntu-24.04", payload.WslDistro);
        Assert.Equal(sourceDir, payload.SourceDir);
        Assert.Equal(RuntimeMode.Wsl, payload.Mode);
        Assert.True(RuntimeBuildJobService.CanCancel(running));
        Assert.False(RuntimeBuildJobService.CanRetry(running));
        Assert.False(RuntimeBuildJobService.CanClear(running));
        Assert.False(RuntimeBuildJobService.CanCancel(failed));
        Assert.True(RuntimeBuildJobService.CanRetry(failed));
        Assert.True(RuntimeBuildJobService.CanClear(failed));
        Assert.False(RuntimeBuildJobService.CanCancel(completed));
        Assert.False(RuntimeBuildJobService.CanRetry(completed));
        Assert.True(RuntimeBuildJobService.CanClear(completed));
        Assert.False(RuntimeBuildJobService.CanCancel(completedDownload));
        Assert.False(RuntimeBuildJobService.CanRetry(completedDownload));
        Assert.True(RuntimeBuildJobService.CanClear(completedDownload));
    }


    [Fact]
    public async Task RuntimeBuildJobControlServiceCancelsRetriesAndClearsRuntimeJobs()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""));
        var markers = new RuntimeBuildMarkerService(runner);
        var cancellations = new RuntimeBuildCancellationRegistry();
        var controls = new RuntimeBuildJobControlService(store, jobs, markers, cancellations, root);
        var preset = new RuntimeBuildPreset("official-cuda", "Official CUDA", "https://example.com/llama.cpp.git", "master", true);
        var sourceDir = Path.Combine(root, "runtime-source");
        Directory.CreateDirectory(sourceDir);
        var payloadJson = RuntimeBuildJobService.Payload(preset, "build", Path.Combine(root, "runtime"), "Building", "marker", "Ubuntu-24.04", sourceDir);
        var job = await jobs.CreateAsync("runtime-build", payloadJson, TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Running, payloadJson, TestContext.Current.CancellationToken);
        var cancellation = controls.RegisterCancellation(job.Id);

        var cancel = await controls.CancelAsync(
            job with { Status = JobStatus.Running },
            "Ubuntu-default",
            BoundedLogFile.MegabytesToBytes(1),
            TestContext.Current.CancellationToken);
        var cancellationRequested = cancellation.IsCancellationRequested;
        var cancelledJob = Assert.Single(await store.ListJobsAsync());
        var retry = controls.PlanRetry(cancelledJob);
        var clear = await controls.ClearAsync(cancelledJob, TestContext.Current.CancellationToken);
        controls.UnregisterCancellation(job.Id, cancellation);
        var logExistsAfterClear = File.Exists(cancelledJob.LogPath);

        Assert.True(cancel.Success);
        Assert.True(cancellationRequested);
        Assert.Contains("Cancel requested", cancel.StatusMessage, StringComparison.Ordinal);
        Assert.Equal(JobStatus.Cancelled, cancelledJob.Status);
        Assert.Contains("Cancel requested by user", RuntimeBuildJobService.ParsePayload(cancelledJob.PayloadJson)?.Message, StringComparison.Ordinal);
        Assert.Contains(runner.Commands, command => command.Contains("Ubuntu-24.04"));
        Assert.True(retry.CanRetry);
        Assert.Equal(preset.Id, retry.Preset?.Id);
        Assert.False(retry.Update);
        Assert.Equal(sourceDir, retry.Source?.SourceDir);
        Assert.True(clear.Success);
        Assert.Contains("Cleared runtime job", clear.StatusMessage, StringComparison.Ordinal);
        Assert.Empty(await store.ListJobsAsync());
        Assert.False(logExistsAfterClear);
        Assert.Equal(0, cancellations.ActiveCount);
    }


    [Fact]
    public async Task RuntimeBuildJobApplicationServiceCoordinatesCancelRetryAndClear()
    {
        var root = CreateTempRoot();
        await using var store = new StateStore(Path.Combine(root, "state", "local-llm-console.db"));
        await store.InitializeAsync();
        var settings = AppSettings.CreateDefault(root);
        var jobs = new JobEngine(store, Path.Combine(root, "logs"));
        var runner = new ScriptedProcessRunner(_ => new ProcessRunResult(0, "", ""));
        var markers = new RuntimeBuildMarkerService(runner);
        var cancellations = new RuntimeBuildCancellationRegistry();
        var controls = new RuntimeBuildJobControlService(store, jobs, markers, cancellations, root);
        var application = new RuntimeBuildJobApplicationService(controls);
        var preset = new RuntimeBuildPreset("app-job-cuda", "App Job CUDA", "https://example.com/llama.cpp.git", "master", true);
        var sourceDir = Path.Combine(root, "runtime-source");
        Directory.CreateDirectory(sourceDir);
        var payloadJson = RuntimeBuildJobService.Payload(preset, "build", Path.Combine(root, "runtime"), "Building", "marker", "Ubuntu-24.04", sourceDir);
        var job = await jobs.CreateAsync("runtime-build", payloadJson, TestContext.Current.CancellationToken);
        await jobs.UpdateAsync(job, JobStatus.Running, payloadJson, TestContext.Current.CancellationToken);
        var cancellation = controls.RegisterCancellation(job.Id);
        var confirmations = new List<RuntimeBuildJobClearConfirmation>();
        var retries = new List<RuntimeBuildJobRetryPlan>();
        var busyMessages = new List<string>();
        var statuses = new List<string>();
        var allowClear = false;
        RuntimeBuildJobApplicationActions Actions() => new(
            confirmation =>
            {
                confirmations.Add(confirmation);
                return allowClear;
            },
            async (message, action) =>
            {
                busyMessages.Add(message);
                await action();
            },
            retry =>
            {
                retries.Add(retry);
                return Task.CompletedTask;
            },
            statuses.Add);

        var invalidClear = await application.ClearAsync(job with { Status = JobStatus.Running }, Actions());
        var cancelled = await application.CancelAsync(job with { Status = JobStatus.Running }, settings, BoundedLogFile.MegabytesToBytes(1), Actions());
        var cancellationRequested = cancellation.IsCancellationRequested;
        var cancelledJob = Assert.Single(await store.ListJobsAsync());
        var retried = await application.RetryAsync(cancelledJob, Actions());
        var clearCancelled = await application.ClearAsync(cancelledJob, Actions());
        allowClear = true;
        var cleared = await application.ClearAsync(cancelledJob, Actions());
        controls.UnregisterCancellation(job.Id, cancellation);

        Assert.Equal(RuntimeBuildJobApplicationOutcome.Blocked, invalidClear);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Applied, cancelled);
        Assert.True(cancellationRequested);
        Assert.Contains("Cancel requested", statuses[1], StringComparison.Ordinal);
        Assert.Equal(JobStatus.Cancelled, cancelledJob.Status);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Applied, retried);
        var retry = Assert.Single(retries);
        Assert.True(retry.CanRetry);
        Assert.Equal(preset.Id, retry.Preset?.Id);
        Assert.Equal(sourceDir, retry.Source?.SourceDir);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Cancelled, clearCancelled);
        Assert.Equal(2, confirmations.Count);
        Assert.Equal("Clear runtime job", confirmations[0].Title);
        Assert.Equal(RuntimeBuildJobApplicationOutcome.Applied, cleared);
        Assert.Equal(["Only completed, failed, cancelled, or interrupted runtime jobs can be cleared.", "Cancel requested for App Job CUDA.", $"Cleared runtime job {job.Id}."], statuses);
        Assert.Equal(["Clearing runtime job..."], busyMessages);
        Assert.Empty(await store.ListJobsAsync());
        Assert.Equal(0, cancellations.ActiveCount);
    }


}
