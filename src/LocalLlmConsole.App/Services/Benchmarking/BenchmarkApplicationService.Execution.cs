using LocalLlmConsole.Models;

namespace LocalLlmConsole.Services;

public sealed partial class BenchmarkApplicationService
{
    private async Task<WorkItemOutcome> ExecuteWorkItemAsync(
        string jobId,
        BenchmarkJobPayload payload,
        BenchmarkWorkItem item,
        int attempt,
        LoadedModelSessionManager.BenchmarkSessionLease computeLease,
        CancellationToken cancellationToken)
    {
        var runtime = (await _store.ListRuntimesAsync()).FirstOrDefault(runtime => runtime.Id.Equals(item.RuntimeId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Runtime '{item.RuntimeId}' is no longer registered.");
        if (item.ExecutionMode == BenchmarkExecutionMode.ProfileServing)
            return await ExecuteServingWorkItemAsync(jobId, payload, item, attempt, runtime, computeLease, cancellationToken);
        var capability = await _capabilities.ProbeAsync(runtime, item.WslDistro, cancellationToken);
        if (!capability.IsAvailable) throw new InvalidOperationException(capability.Error);
        var modelPath = BenchmarkRuntimeToolAdapter.RuntimeVisiblePath(runtime.Mode, item.ModelPath);
        var args = BenchmarkCommandBuilder.Build(payload.Plan, item, modelPath);
        var job = await _store.GetJobAsync(jobId) ?? throw new InvalidOperationException($"Benchmark job '{jobId}' disappeared.");
        var sequence = 0;
        var validRows = 0;
        await using var memorySampler = await BenchmarkGpuMemorySampler.StartAsync(() => new WindowsGpuMemoryProbe(), cancellationToken);
        var process = await _processRunner.RunAsync(
            runtime,
            item.WslDistro,
            capability.BenchmarkExecutablePath,
            args,
            async line =>
            {
                if (!BenchmarkResultService.TryParse(
                        line, item.ModelFingerprint, item.EffectiveCommandSignature, runtime.Mode, runtime.Backend,
                        out var parsed, out var error, AppUpdateService.CurrentVersionLabel(), RuntimeInformation.OSDescription)
                    || parsed is null)
                {
                    await AppendLogAsync((await _store.GetJobAsync(jobId))?.LogPath, $"Ignored output: {error}");
                    return;
                }
                sequence++;
                await _store.InsertBenchmarkResultAsync(jobId, item.Key, attempt, sequence, parsed);
                validRows++;
                PublishTransient(job, payload, $"Latest {parsed.Classification}: {parsed.AverageTokensPerSecond:0.00} tok/s", payload.ResultRows + validRows);
            },
            onDiagnostic: line => PublishTransient(job, payload, line.Length <= 300 ? line : line[..300], payload.ResultRows + Volatile.Read(ref validRows)),
            cancellationToken);
        var memoryPeaks = await memorySampler.FinishAsync();
        await _store.SetBenchmarkMemoryAsync(jobId, item.Key, attempt, memoryPeaks, BenchmarkGpuMemorySampler.IntervalMilliseconds);
        if (!string.IsNullOrWhiteSpace(process.DiagnosticTail)) await AppendLogAsync(job.LogPath, process.DiagnosticTail);
        return new WorkItemOutcome(process.ExitCode, validRows, process.CancellationRequested, process.VerifiedStopped, "");
    }

    private async Task<WorkItemOutcome> ExecuteServingWorkItemAsync(
        string jobId,
        BenchmarkJobPayload payload,
        BenchmarkWorkItem item,
        int attempt,
        RuntimeRecord runtime,
        LoadedModelSessionManager.BenchmarkSessionLease computeLease,
        CancellationToken cancellationToken)
    {
        var model = (await _store.ListModelsAsync()).FirstOrDefault(model => model.Id.Equals(item.ModelId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Model '{item.ModelId}' is no longer registered.");
        var launchProfile = item.LaunchSettings ?? throw new InvalidOperationException("The benchmark work item has no saved launch-profile snapshot.");
        var workspaceRoot = string.IsNullOrWhiteSpace(_workspaceRoot)
            ? Path.GetDirectoryName(Path.GetDirectoryName((await _store.GetJobAsync(jobId))?.LogPath ?? "")) ?? Environment.CurrentDirectory
            : _workspaceRoot;
        var settings = launchProfile.ApplyTo(await _store.GetAppSettingsAsync(workspaceRoot)) with { Host = "127.0.0.1" };
        LoadedModelSessionSnapshot? session = null;
        var sequence = 0;
        var job = await _store.GetJobAsync(jobId) ?? throw new InvalidOperationException($"Benchmark job '{jobId}' disappeared.");
        try
        {
            session = await computeLease.StartAsync(runtime, model, settings, Path.Combine(workspaceRoot, "logs"),
                item.ProfileIds.FirstOrDefault() ?? "", item.ProfileNames.FirstOrDefault() ?? "");
            var results = await _servingRunner.RunAsync(payload.Plan, item, runtime, model, settings,
                async parsed =>
                {
                    sequence++;
                    await _store.InsertBenchmarkResultAsync(jobId, item.Key, attempt, sequence, parsed);
                    PublishTransient(job, payload,
                        $"Latest {parsed.ProfileName}: {parsed.AverageTokensPerSecond:0.00} tok/s, draft {parsed.DraftAcceptancePercent:0.0}%",
                        payload.ResultRows + sequence);
                },
                message => PublishTransient(job, payload, message, payload.ResultRows + Volatile.Read(ref sequence)),
                cancellationToken);
            return new WorkItemOutcome(0, results.Count, false, true, "");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new WorkItemOutcome(-1, sequence, true, true, "Cancelled");
        }
        catch (Exception ex)
        {
            await AppendLogAsync(job.LogPath, ex.ToString());
            return new WorkItemOutcome(-1, sequence, false, true, ex.Message);
        }
        finally
        {
            if (session is not null) await computeLease.StopAsync(session.SessionId, CancellationToken.None);
        }
    }
}
