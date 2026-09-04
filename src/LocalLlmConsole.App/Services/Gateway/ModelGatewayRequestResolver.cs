namespace LocalLlmConsole.Services;

public static class ModelGatewayRequestResolver
{
    private static readonly HashSet<string> ProxiedPostPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/v1/chat/completions",
        "/v1/completions",
        "/v1/responses",
        "/v1/embeddings",
        "/v1/audio/speech",
        "/v1/audio/transcriptions",
        "/v1/images/generations",
        "/v1/images/edits",
        "/completion",
        "/infill",
        "/rerank",
        "/reranking",
        "/v1/rerank",
        "/v1/reranking"
    };

    public static bool IsProxiedPostPath(string path)
        => ProxiedPostPaths.Contains(path);

    public static string ExtractRequestedModel(byte[] body)
    {
        if (body.Length == 0) return "";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return "";
            if (document.RootElement.TryGetProperty("model", out var model) && model.ValueKind == JsonValueKind.String)
                return model.GetString()?.Trim() ?? "";
            return "";
        }
        catch
        {
            return "";
        }
    }

    public static ModelGatewayModelRoute? ResolveModel(IReadOnlyList<ModelGatewayModelRoute> models, string requestedModel)
    {
        var requested = (requestedModel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requested)) return null;

        var exact = models.FirstOrDefault(route => string.Equals(route.Id, requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(route => string.Equals(route.LegacyId, requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(route => string.Equals(route.Name, requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(route => string.Equals(route.Profile.Id, requested, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact;

        return models.FirstOrDefault(route => route.Profile.IsDefault
            && string.Equals(route.Model.Name, requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(route => route.Profile.IsDefault
                && string.Equals(Path.GetFileName(route.Model.ModelPath), requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(route => route.Profile.IsDefault
                && string.Equals(Path.GetFileNameWithoutExtension(route.Model.ModelPath), requested, StringComparison.OrdinalIgnoreCase));
    }

    public static ModelRecord? ResolveModel(IReadOnlyList<ModelRecord> models, string requestedModel)
    {
        var requested = (requestedModel ?? "").Trim();
        if (string.IsNullOrWhiteSpace(requested)) return null;

        return models.FirstOrDefault(model => string.Equals(model.Id, requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(model => string.Equals(model.Name, requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(model => string.Equals(Path.GetFileName(model.ModelPath), requested, StringComparison.OrdinalIgnoreCase))
            ?? models.FirstOrDefault(model => string.Equals(Path.GetFileNameWithoutExtension(model.ModelPath), requested, StringComparison.OrdinalIgnoreCase));
    }

    public static byte[] BodyForRuntime(byte[] body, AppSettings launchSettings)
    {
        var aliases = RuntimeModelAliasService.ReadAliases(launchSettings.CustomParameters);
        if (aliases.Count == 0 || aliases.Contains(ExtractRequestedModel(body), StringComparer.Ordinal)) return body;

        // Gateway suffixes select profiles; llama-server only knows its configured aliases.
        // Use the active session's settings, which may differ from a subsequently edited profile.
        using var document = JsonDocument.Parse(body);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.NameEquals("model")) writer.WriteString("model", aliases[0]);
                else property.WriteTo(writer);
            }
            writer.WriteEndObject();
        }
        return stream.ToArray();
    }
}
