namespace LocalLlmConsole.Services;

internal sealed class ControlJobEndpoints : ControlEndpointHandler
{
    public ControlJobEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    internal async Task<LocalControlApiResponse> JobsAsync(
        string method,
        string[] segments,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
            return Ok(new { ok = true, jobs = await _deps.StateStore.ListJobsAsync() });
        if (segments.Length != 5 || method != "POST") return Error(404, "Not found.");
        var job = (await _deps.StateStore.ListJobsAsync()).FirstOrDefault(item =>
            item.Id.Equals(segments[3], StringComparison.OrdinalIgnoreCase))
            ?? throw new KeyNotFoundException($"Job '{segments[3]}' was not found.");
        var action = segments[4].ToLowerInvariant();
        if (!job.Kind.Equals("huggingface-download", StringComparison.OrdinalIgnoreCase))
            return Error(409, $"Job '{job.Id}' is a {job.Kind} job. Generic job commands support Hugging Face downloads only.");
        switch (action)
        {
            case "pause":
                await _deps.HuggingFace.PauseDownloadAsync(job);
                break;
            case "resume":
                await _deps.HuggingFace.ResumeDownloadAsync(job, _deps.Actions.GetSettings());
                break;
            case "cancel":
            case "stop":
                await _deps.HuggingFace.StopDownloadAsync(job);
                break;
            default:
                return Error(404, "Not found.");
        }
        cancellationToken.ThrowIfCancellationRequested();
        return Ok(new { ok = true, job = job.Id, action });
    }

    internal async Task<LocalControlApiResponse> HuggingFaceAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 4 && segments[3].Equals("search", StringComparison.OrdinalIgnoreCase) && method == "GET")
        {
            request.Query.TryGetValue("q", out var query);
            var results = await _deps.HuggingFace.SearchAsync(query ?? "", cancellationToken);
            return Ok(new { ok = true, results });
        }
        if (segments.Length == 4 && segments[3].Equals("download", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var download = Body<LocalControlDownloadRequest>(request.Body);
            var query = download.Query;
            if (string.IsNullOrWhiteSpace(query))
            {
                if (string.IsNullOrWhiteSpace(download.Repo))
                    throw new InvalidOperationException("Provide query or repo for a Hugging Face download.");
                query = string.IsNullOrWhiteSpace(download.Path)
                    ? download.Repo
                    : $"https://huggingface.co/{download.Repo}/resolve/{(string.IsNullOrWhiteSpace(download.Revision) ? "main" : download.Revision)}/{download.Path}";
            }
            var results = await _deps.HuggingFace.SearchAsync(query, cancellationToken);
            var file = results.FirstOrDefault(candidate =>
                    string.IsNullOrWhiteSpace(download.Path)
                    || candidate.Path.Equals(download.Path, StringComparison.OrdinalIgnoreCase))
                ?? throw new KeyNotFoundException("No matching GGUF file was found on Hugging Face.");
            if (download.DryRun)
                return Ok(new { ok = true, dryRun = true, file, wouldDownload = true });
            var job = await _deps.HuggingFace.StartDownloadAsync(file, _deps.Actions.GetSettings(), cancellationToken);
            return new LocalControlApiResponse(202, new { ok = true, job, file });
        }
        return Error(404, "Not found.");
    }

}
