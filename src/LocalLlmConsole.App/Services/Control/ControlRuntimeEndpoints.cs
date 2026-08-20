namespace LocalLlmConsole.Services;

internal sealed class ControlRuntimeEndpoints : ControlEndpointHandler
{
    public ControlRuntimeEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    internal async Task<LocalControlApiResponse> RuntimesAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
            return Ok(new { ok = true, runtimes = await _deps.StateStore.ListRuntimesAsync() });
        if (segments.Length == 4 && segments[3].Equals("scan", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var count = await _deps.RuntimeRegistry.ScanAsync(_deps.Actions.GetSettings().RuntimeRoot);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, registered = count });
        }
        if (segments.Length == 4 && segments[3].Equals("register", StringComparison.OrdinalIgnoreCase) && method == "POST")
        {
            var folder = RequiredString(request.Body, "folder");
            if (!Directory.Exists(folder))
                throw new InvalidOperationException($"Runtime folder '{folder}' was not found.");
            var runtime = await _deps.RuntimeRegistry.RegisterFolderAsync(folder);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return new LocalControlApiResponse(201, new { ok = true, runtime });
        }
        return Error(404, "Not found.");
    }

}
