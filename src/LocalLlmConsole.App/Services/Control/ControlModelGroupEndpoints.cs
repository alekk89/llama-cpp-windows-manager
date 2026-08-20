namespace LocalLlmConsole.Services;

internal sealed class ControlModelGroupEndpoints : ControlEndpointHandler
{
    public ControlModelGroupEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    internal async Task<LocalControlApiResponse> HandleAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        var snapshot = await _modelGroups.SnapshotAsync();
        if (segments.Length == 3 && method == "GET")
            return Ok(new
            {
                ok = true,
                groups = snapshot.Groups.Select(group => ModelGroupView(group, snapshot)).ToArray()
            });

        if (segments.Length == 3 && method == "POST")
        {
            var body = Body<LocalControlModelGroupWriteRequest>(request.Body);
            var group = await _modelGroups.CreateAsync(
                body.Name,
                EnumRequest<ModelGroupRetentionMode>(body.RetentionMode, "retentionMode"),
                body.IdleMinutes,
                EnumRequest<ModelGroupEvictionPriority>(body.EvictionPriority, "evictionPriority"));
            await _deps.Actions.RefreshAsync(cancellationToken);
            return new LocalControlApiResponse(201, new { ok = true, group = ModelGroupView(group, await _modelGroups.SnapshotAsync()) });
        }

        if (segments.Length != 4) return Error(404, "Not found.");
        var existing = ModelGroupService.Resolve(snapshot, segments[3]);
        if (method == "GET")
            return Ok(new { ok = true, group = ModelGroupView(existing, snapshot) });
        if (method is "PATCH" or "PUT")
        {
            var body = request.Body ?? new JsonObject();
            var name = body["name"]?.ToString() ?? existing.Name;
            var retentionMode = body["retentionMode"] is null
                ? existing.RetentionMode
                : EnumRequest<ModelGroupRetentionMode>(body["retentionMode"]!.ToString(), "retentionMode");
            var idleMinutes = body["idleMinutes"]?.GetValue<int>() ?? existing.IdleMinutes;
            var priority = body["evictionPriority"] is null
                ? existing.EvictionPriority
                : EnumRequest<ModelGroupEvictionPriority>(body["evictionPriority"]!.ToString(), "evictionPriority");
            var updated = await _modelGroups.UpdateAsync(existing.Id, name, retentionMode, idleMinutes, priority);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, group = ModelGroupView(updated, await _modelGroups.SnapshotAsync()) });
        }
        if (method == "DELETE")
        {
            await _modelGroups.DeleteAsync(existing.Id);
            await _deps.Actions.RefreshAsync(cancellationToken);
            return Ok(new { ok = true, deleted = existing.Id });
        }
        return Error(404, "Not found.");
    }

}
