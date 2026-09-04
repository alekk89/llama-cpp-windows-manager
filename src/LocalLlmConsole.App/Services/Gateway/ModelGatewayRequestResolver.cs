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
        if (models is ModelGatewayRouteSnapshot snapshot) return snapshot.Resolve(requested);

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
        if (aliases.Count == 0) return body;

        // Scan once and replace only top-level model values. Keeping all other
        // bytes avoids decoding/re-encoding large prompts and multimodal payloads.
        var reader = new Utf8JsonReader(body);
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Gateway request body must be a JSON object.");
        var ranges = new List<(int Start, int Length)>();
        var requestedModel = "";
        while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
        {
            var isModel = reader.ValueTextEquals("model"u8);
            if (!reader.Read()) throw new JsonException("Missing property value.");
            var start = checked((int)reader.TokenStartIndex);
            if (isModel)
                requestedModel = reader.TokenType == JsonTokenType.String ? reader.GetString()?.Trim() ?? "" : "";
            reader.Skip();
            if (isModel) ranges.Add((start, checked((int)reader.BytesConsumed) - start));
        }
        if (reader.TokenType != JsonTokenType.EndObject)
            throw new JsonException("Incomplete gateway request body.");
        // Validate trailing input even when the runtime already accepts the alias.
        if (reader.Read()) throw new JsonException("Unexpected trailing JSON value.");
        if (ranges.Count == 0 || aliases.Contains(requestedModel, StringComparer.Ordinal)) return body;

        // Gateway suffixes select profiles; use the active session's alias, which
        // may differ from a subsequently edited saved profile.
        var replacement = JsonSerializer.SerializeToUtf8Bytes(aliases[0]);
        var length = body.Length;
        foreach (var range in ranges)
            length = checked(length - range.Length + replacement.Length);
        var rewritten = new byte[length];
        var sourceOffset = 0;
        var destinationOffset = 0;
        foreach (var range in ranges)
        {
            var unchanged = range.Start - sourceOffset;
            body.AsSpan(sourceOffset, unchanged).CopyTo(rewritten.AsSpan(destinationOffset));
            destinationOffset += unchanged;
            replacement.CopyTo(rewritten.AsSpan(destinationOffset));
            destinationOffset += replacement.Length;
            sourceOffset = range.Start + range.Length;
        }
        body.AsSpan(sourceOffset).CopyTo(rewritten.AsSpan(destinationOffset));
        return rewritten;
    }
}
