using System.Collections.Concurrent;

namespace LocalLlmConsole.Services;

public sealed record ModelGatewayMetadata(
    long? TrainingContext,
    long? ParameterCount,
    long? SizeBytes);

public static class ModelGatewayMetadataService
{
    private sealed record CachedMetadata(string Fingerprint, ModelGatewayMetadata Value);

    private static readonly ConcurrentDictionary<string, CachedMetadata> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static ModelGatewayMetadata Inspect(ModelRecord model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var file = ExistingFile(model.ModelPath);
        var fingerprint = $"{model.ModelPath}|{model.UpdatedAt:O}|{file?.Length ?? -1}|{file?.LastWriteTimeUtc.Ticks ?? 0}";
        if (Cache.TryGetValue(model.Id, out var cached)
            && string.Equals(cached.Fingerprint, fingerprint, StringComparison.Ordinal))
            return cached.Value;

        var stored = StoredMetadata(model.MetadataJson);
        var trainingContext = stored.TrainingContext;
        var parameterCount = stored.ParameterCount;
        if (file is not null && (!stored.HasTrainingContext || !stored.HasParameterCount))
        {
            var gguf = GgufMetadataReader.TryRead(file.FullName);
            var architecture = Text(gguf, "general.architecture");
            if (!stored.HasTrainingContext)
                trainingContext = Positive(ModelCapabilityService.ContextLength(gguf, architecture));
            if (!stored.HasParameterCount)
                parameterCount = Positive(Integer(gguf, "general.parameter_count"))
                    ?? GgufMetadataReader.TryReadParameterCount(file.FullName);
        }

        var result = new ModelGatewayMetadata(trainingContext, parameterCount, file?.Length);
        Cache[model.Id] = new CachedMetadata(fingerprint, result);
        return result;
    }

    private static FileInfo? ExistingFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file : null;
        }
        catch
        {
            return null;
        }
    }

    private static StoredGatewayMetadata StoredMetadata(string json)
    {
        try
        {
            var metadata = JsonNode.Parse(json) as JsonObject;
            if (metadata is null) return default;
            return new StoredGatewayMetadata(
                metadata.ContainsKey("ggufContextLength"),
                Positive(Integer(metadata, "ggufContextLength")),
                metadata.ContainsKey("ggufParameterCount"),
                Positive(Integer(metadata, "ggufParameterCount")));
        }
        catch
        {
            return default;
        }
    }

    private static string Text(IReadOnlyDictionary<string, object?> values, string key)
        => values.TryGetValue(key, out var value) ? value?.ToString() ?? "" : "";

    private static long? Integer(IReadOnlyDictionary<string, object?> values, string key)
    {
        if (!values.TryGetValue(key, out var value) || value is null) return null;
        try { return Convert.ToInt64(value, CultureInfo.InvariantCulture); }
        catch { return null; }
    }

    private static long? Integer(JsonObject values, string key)
    {
        try { return values[key]?.GetValue<long>(); }
        catch { return long.TryParse(values[key]?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null; }
    }

    private static long? Positive(long? value) => value > 0 ? value : null;

    private readonly record struct StoredGatewayMetadata(
        bool HasTrainingContext,
        long? TrainingContext,
        bool HasParameterCount,
        long? ParameterCount);
}
