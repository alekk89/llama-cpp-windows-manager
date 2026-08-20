namespace LocalLlmConsole.Services;

internal sealed class ControlOperationEndpoints : ControlEndpointHandler
{
    public ControlOperationEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    internal async Task<LocalControlApiResponse> HandleAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 3 && method == "GET")
            return Ok(new { ok = true, operations = ControlOperationCatalog.All });
        if (segments.Length != 4 || method != "POST") return Error(404, "Not found.");

        var operation = ControlOperationCatalog.Resolve(segments[3]);
        if (_deps.Actions.ExecuteOperationAsync is null)
            return Error(501, "The application operation bridge is not available in this host.");
        var body = request.Body ?? new JsonObject();
        var dryRun = body["dryRun"]?.GetValue<bool>() ?? false;
        var confirmed = body["confirm"]?.GetValue<bool>() ?? false;
        if (operation.RequiresConfirmation && !dryRun && !confirmed)
            throw new InvalidOperationException($"Operation '{operation.Name}' requires confirm=true. Run it with dryRun=true first when consequences are unclear.");

        var result = await _deps.Actions.ExecuteOperationAsync(operation.Name, body, cancellationToken);
        return Ok(new { ok = true, operation = operation.Name, dryRun, result });
    }
}
