namespace LocalLlmConsole.Services;

internal sealed class ControlSettingsEndpoints : ControlEndpointHandler
{
    public ControlSettingsEndpoints(ControlEndpointContext context)
        : base(context)
    {
    }

    internal async Task<LocalControlApiResponse> SettingsAsync(
        string method,
        string[] segments,
        LocalControlRequest request,
        CancellationToken cancellationToken)
    {
        if (segments.Length == 5
            && segments[3].Equals("model-api-key", StringComparison.OrdinalIgnoreCase)
            && segments[4].Equals("rotate", StringComparison.OrdinalIgnoreCase)
            && method == "POST")
        {
            var rotated = _settingsMutations.RotateModelApiKey(_deps.Actions.GetSettings());
            await _deps.Actions.ApplySettingsAsync(rotated, cancellationToken);
            return Ok(new { ok = true, modelApiKey = "[rotated]", requireApiKeyAuth = true });
        }
        if (segments.Length != 3) return Error(404, "Not found.");
        if (method == "GET")
            return Ok(new { ok = true, settings = ControlJsonPatch.RedactedAppSettings(_deps.Actions.GetSettings()) });
        if (method is not ("PATCH" or "PUT")) return Error(404, "Not found.");

        var current = _deps.Actions.GetSettings();
        var updated = _settingsMutations.Patch(current, request.Body, _deps.Sessions.Snapshots());
        updated = await _deps.Actions.ApplySettingsAsync(updated, cancellationToken);
        return Ok(new { ok = true, settings = ControlJsonPatch.RedactedAppSettings(updated) });
    }

}
