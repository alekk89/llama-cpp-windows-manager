namespace LocalLlmConsole.Services;

internal sealed class ControlLogEndpoints : ControlEndpointHandler
{
    public ControlLogEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    internal async Task<LocalControlApiResponse> LogsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (method != "GET") return Error(404, "Not found.");
        if (segments.Length == 3)
        {
            var data = await _deps.LogWorkflow.LoadAsync(_deps.Sessions.SelectedSnapshot(), cancellationToken);
            return Ok(new
            {
                ok = true,
                logs = data.Files.Select(file => new
                {
                    file = file.Name,
                    path = file.FullPath,
                    sizeBytes = file.Length,
                    updatedAt = file.LastWriteTimeUtc,
                    active = _deps.Sessions.Snapshots().Any(session => session.IsRunning &&
                        LogFileService.NormalizePath(session.LogPath).Equals(LogFileService.NormalizePath(file.FullPath), StringComparison.OrdinalIgnoreCase))
                }).OrderByDescending(file => file.updatedAt).ToArray()
            });
        }

        if (segments.Length == 4)
        {
            var name = Path.GetFileName(segments[3]);
            if (!name.Equals(segments[3], StringComparison.Ordinal))
                throw new InvalidOperationException("Log identifiers must be file names, not paths.");
            var path = Path.Combine(_deps.LogWorkflow.LogRoot, name);
            if (!LogFileService.TryValidateWorkspaceLogFile(_deps.WorkspaceRoot, path, out var fullPath, out var error))
                throw new KeyNotFoundException(error);
            var text = LogFileService.Tail(fullPath, IntQuery(request.Query, "tail", 80000, 1000, 250000));
            return Ok(new { ok = true, file = name, path = fullPath, text = LogFileService.RedactSensitiveText(text, _deps.Actions.GetSettings().ModelApiKey) });
        }
        return Error(404, "Not found.");
    }

}
