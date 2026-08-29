namespace LocalLlmConsole.Services;

public sealed partial class HuggingFaceService
{
    public async Task<JobRecord> StartDownloadAsync(HuggingFaceFile file, AppSettings settings, CancellationToken cancellationToken = default)
    {
        ValidateHuggingFaceFile(file);
        var modelId = ModelCatalogService.SafeId($"{file.Repo.Split('/').Last()}-{Path.GetFileNameWithoutExtension(file.Name)}");
        var modelDir = Path.Combine(settings.ModelsRoot, modelId);
        var destination = Path.GetFullPath(Path.Combine(modelDir, file.Name));
        EnsureDestinationInsideModelsRoot(destination, settings.ModelsRoot);
        if (File.Exists(destination))
            throw new InvalidOperationException("That model file already exists. Delete or rename the existing model before downloading it again.");
        if (_activeDownloadDestinations.ContainsKey(destination))
            throw new InvalidOperationException("That model file is already downloading.");

        var payload = new DownloadJobPayload(file, destination);
        var job = await _jobs.CreateAsync("huggingface-download", JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
        try
        {
            StartDownloadWorker(job, file, settings, destination);
        }
        catch (Exception ex)
        {
            await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(payload with { Error = ex.Message }, JsonOptions), CancellationToken.None);
            throw;
        }
        return job;
    }

    public async Task ResumeDownloadAsync(JobRecord job, AppSettings settings)
    {
        var payload = ParseDownloadPayload(job.PayloadJson);
        if (payload is null) throw new InvalidOperationException("This history entry does not contain a resumable Hugging Face download.");
        ValidateHuggingFaceFile(payload.File);
        if (string.IsNullOrWhiteSpace(payload.Destination)) throw new InvalidOperationException("This history entry is missing its download destination.");
        EnsureDestinationInsideModelsRoot(payload.Destination, settings.ModelsRoot);
        if (File.Exists(payload.Destination) && job.Status != JobStatus.Completed)
            throw new InvalidOperationException("The final model file already exists. Delete or import it before resuming this download.");
        if (_activeDownloads.ContainsKey(job.Id)) throw new InvalidOperationException("That download is already active.");
        if (_activeDownloadDestinations.ContainsKey(Path.GetFullPath(payload.Destination))) throw new InvalidOperationException("That model file is already downloading.");
        await _jobs.UpdateAsync(job, JobStatus.Queued, JsonSerializer.Serialize(payload with { Error = "" }, JsonOptions));
        StartDownloadWorker(job, payload.File, settings, payload.Destination);
    }

    public async Task PauseDownloadAsync(JobRecord job)
    {
        if (_activeDownloads.TryGetValue(job.Id, out var active))
        {
            active.RequestedStopStatus = JobStatus.Paused;
            active.Cancellation.Cancel();
            return;
        }

        if (job.Status is JobStatus.Queued or JobStatus.Running)
            await _jobs.UpdateAsync(job, JobStatus.Paused, job.PayloadJson);
    }

    public async Task StopDownloadAsync(JobRecord job)
    {
        if (_activeDownloads.TryGetValue(job.Id, out var active))
        {
            active.RequestedStopStatus = JobStatus.Cancelled;
            active.Cancellation.Cancel();
            return;
        }

        if (job.Status is not JobStatus.Completed)
            await _jobs.UpdateAsync(job, JobStatus.Cancelled, job.PayloadJson);
    }

    public bool IsDownloadActive(string jobId) => _activeDownloads.ContainsKey(jobId);
    public int ActiveDownloadCount => _activeDownloads.Count;

    private void StartDownloadWorker(JobRecord job, HuggingFaceFile file, AppSettings settings, string destination)
    {
        var fullDestination = Path.GetFullPath(destination);
        var active = new ActiveDownload { Destination = fullDestination };
        if (!_activeDownloadDestinations.TryAdd(fullDestination, job.Id)) throw new InvalidOperationException("That model file is already downloading.");
        if (!_activeDownloads.TryAdd(job.Id, active))
        {
            _activeDownloadDestinations.TryRemove(fullDestination, out _);
            throw new InvalidOperationException("That download is already active.");
        }
        active.Completion = Task.Run(() => RunDownloadWorkerAsync(job, file, settings, destination, active));
        _ = active.Completion;
    }

    public async Task PauseActiveDownloadsAsync(TimeSpan timeout)
    {
        var activeDownloads = _activeDownloads.Values.ToArray();
        if (activeDownloads.Length == 0) return;

        foreach (var active in activeDownloads)
        {
            active.RequestedStopStatus = JobStatus.Paused;
            active.Cancellation.Cancel();
        }

        var completion = Task.WhenAll(activeDownloads.Select(active => active.Completion));
        await BoundedTaskDrain.ObserveWithinAsync(
            completion,
            timeout,
            "Hugging Face downloads did not stop within the requested pause interval; continuing shutdown.",
            "A Hugging Face download completed with an observed shutdown exception:");
    }

    public async Task RecoverInterruptedDownloadsAsync(AppSettings settings)
    {
        var jobs = await _store.ListJobsAsync();
        foreach (var job in jobs.Where(job => string.Equals(job.Kind, "huggingface-download", StringComparison.OrdinalIgnoreCase)))
        {
            if (job.Status == JobStatus.Completed) continue;
            var payload = ParseDownloadPayload(job.PayloadJson);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Destination)) continue;
            try
            {
                ValidateHuggingFaceFile(payload.File);
            }
            catch (Exception ex)
            {
                await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(payload with { Error = ex.Message }, JsonOptions));
                continue;
            }

            var destination = payload.Destination;
            try
            {
                EnsureDestinationInsideModelsRoot(destination, settings.ModelsRoot);
                destination = Path.GetFullPath(destination);
            }
            catch (Exception ex)
            {
                await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(payload with { Error = ex.Message }, JsonOptions));
                continue;
            }

            var partial = destination + ".partial";
            var expectedBytes = ExpectedBytes(payload.File, payload.TotalBytes);
            if (File.Exists(destination))
            {
                try
                {
                    RejectUnsafeExistingFile(destination, "downloaded model");
                    if (File.Exists(partial)) RejectUnsafeExistingFile(partial, "partial download");
                }
                catch (Exception ex)
                {
                    await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(payload with { Error = ex.Message }, JsonOptions));
                    continue;
                }

                var finalBytes = new FileInfo(destination).Length;
                var verificationError = await VerifyDownloadedFileAsync(
                    destination,
                    payload.File,
                    expectedBytes,
                    CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(verificationError))
                {
                    await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(payload with
                    {
                        DownloadedBytes = finalBytes,
                        TotalBytes = expectedBytes,
                        Error = verificationError
                    }, JsonOptions));
                }
                else
                {
                    if (File.Exists(partial)) TryDelete(partial);
                    await CompleteVerifiedPrimaryModelAsync(
                        job,
                        settings,
                        payload.File,
                        destination,
                        expectedBytes > 0 ? expectedBytes : finalBytes,
                        DateTimeOffset.UtcNow,
                        recovered: true,
                        CancellationToken.None);
                }
                continue;
            }

            var partialBytes = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            try
            {
                if (File.Exists(partial)) RejectUnsafeExistingFile(partial, "partial download");
            }
            catch (Exception ex)
            {
                await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(payload with
                {
                    DownloadedBytes = partialBytes,
                    TotalBytes = expectedBytes,
                    Error = ex.Message
                }, JsonOptions));
                continue;
            }

            if (expectedBytes > 0 && partialBytes == expectedBytes)
            {
                try
                {
                    await CompletePartialDownloadAsync(job, payload.File, settings, destination, partial, expectedBytes, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(payload with
                    {
                        DownloadedBytes = partialBytes,
                        TotalBytes = expectedBytes,
                        Error = ex.Message
                    }, JsonOptions));
                }
                continue;
            }

            var status = job.Status is JobStatus.Queued or JobStatus.Running ? JobStatus.Interrupted : job.Status;
            var totalBytes = expectedBytes;
            if (partialBytes == payload.DownloadedBytes && totalBytes == payload.TotalBytes && status == job.Status) continue;

            await _jobs.UpdateAsync(job, status, JsonSerializer.Serialize(payload with
            {
                DownloadedBytes = partialBytes,
                TotalBytes = totalBytes,
                Error = status == JobStatus.Interrupted ? "Interrupted when the app stopped." : payload.Error
            }, JsonOptions));
        }
    }

    private async Task RunDownloadWorkerAsync(JobRecord job, HuggingFaceFile file, AppSettings settings, string destination, ActiveDownload active)
    {
        try
        {
            await DownloadAsync(job, file, settings, destination, active.Cancellation.Token);
        }
        catch
        {
            // The job table is the user-visible source of truth; failures are recorded there.
        }
        finally
        {
            _activeDownloads.TryRemove(job.Id, out _);
            _activeDownloadDestinations.TryRemove(active.Destination, out _);
            active.Cancellation.Dispose();
        }
    }

    private async Task DownloadAsync(JobRecord job, HuggingFaceFile file, AppSettings settings, string destination, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(settings.ModelsRoot);
        Directory.CreateDirectory(settings.CacheRoot);
        EnsureDestinationInsideModelsRoot(destination, settings.ModelsRoot);
        var active = _activeDownloads[job.Id];
        var modelDir = Path.GetDirectoryName(destination) ?? settings.ModelsRoot;
        var partial = destination + ".partial";
        Directory.CreateDirectory(modelDir);
        var url = ResolveUrl(file.Repo, file.Path, file.Revision);
        long total = 0;

        try
        {
            var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            if (File.Exists(partial)) RejectUnsafeExistingFile(partial, "partial download");
            if (File.Exists(destination)) RejectUnsafeExistingFile(destination, "downloaded model");
            if (file.SizeBytes > 0 && existing > file.SizeBytes)
                throw new InvalidDataException($"Partial download exceeds the expected size of {file.SizeBytes:N0} bytes.");
            if (existing > 0 && file.SizeBytes > 0 && existing == file.SizeBytes)
            {
                await CompletePartialDownloadAsync(job, file, settings, destination, partial, file.SizeBytes, cancellationToken);
                return;
            }

            EnsureDiskSpace(destination, file.SizeBytes > existing ? file.SizeBytes - existing : 0);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (existing > 0) request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existing, null);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            total = RequiredDownloadBytes(file, ValidateDownloadResponse(file, response, existing));
            if (existing > 0 && (int)response.StatusCode != 206) existing = 0;
            EnsureDiskSpace(destination, total > existing ? total - existing : 0);
            await _jobs.UpdateAsync(job, JobStatus.Running, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, existing, total), JsonOptions), cancellationToken);
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = OpenSafePartialForWrite(partial, existing > 0);
            var buffer = new byte[1024 * 1024];
            long downloaded = existing;
            var lastProgressBytes = existing;
            var lastProgressUpdateAt = DateTimeOffset.UtcNow;
            int read;
            while ((read = await BoundedStreamCopyService.ReadWithIdleTimeoutAsync(
                       input,
                       buffer,
                       DownloadReadIdleTimeout,
                       cancellationToken)) > 0)
            {
                if (read > total - downloaded)
                    throw new InvalidDataException($"Download exceeded the expected size of {total:N0} bytes.");
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                downloaded += read;
                var now = DateTimeOffset.UtcNow;
                if (ShouldPersistDownloadProgress(downloaded, lastProgressBytes, lastProgressUpdateAt, now))
                {
                    await _jobs.UpdateAsync(job, JobStatus.Running, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, downloaded, total), JsonOptions), cancellationToken);
                    lastProgressBytes = downloaded;
                    lastProgressUpdateAt = now;
                }
            }
            output.Close();
            await CompletePartialDownloadAsync(job, file, settings, destination, partial, total, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            var knownTotal = total > 0 ? total : file.SizeBytes;
            await _jobs.UpdateAsync(job, active.RequestedStopStatus, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, existing, knownTotal), JsonOptions), CancellationToken.None);
            // Clean up partial file on cancellation (not pause — paused downloads keep it for resume).
            if (active.RequestedStopStatus is JobStatus.Cancelled or JobStatus.Failed)
                TryDelete(partial);
        }
        catch (Exception ex)
        {
            var existing = File.Exists(partial) ? new FileInfo(partial).Length : 0;
            var knownTotal = total > 0 ? total : file.SizeBytes;
            await _jobs.UpdateAsync(job, JobStatus.Failed, JsonSerializer.Serialize(new DownloadJobPayload(file, destination, existing, knownTotal, ex.Message), JsonOptions), CancellationToken.None);
            TryDelete(partial);
        }
    }
}
