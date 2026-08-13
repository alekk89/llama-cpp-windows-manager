namespace LocalLlmConsole.Services;

public static class ControlJsonPatch
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static T Apply<T>(T source, JsonObject? patch, params string[] blockedProperties)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (patch is null || patch.Count == 0) return source;

        var target = JsonSerializer.SerializeToNode(source, JsonOptions)?.AsObject()
            ?? throw new InvalidOperationException($"Could not serialize {typeof(T).Name} for patching.");
        var names = target.Select(pair => pair.Key).ToDictionary(name => name, StringComparer.OrdinalIgnoreCase);
        var blocked = blockedProperties.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (suppliedName, value) in patch)
        {
            if (!names.TryGetValue(suppliedName, out var actualName))
                throw new InvalidOperationException($"Unknown {typeof(T).Name} setting '{suppliedName}'.");
            if (blocked.Contains(actualName))
                throw new InvalidOperationException($"Setting '{actualName}' cannot be changed through the control API.");
            target[actualName] = value?.DeepClone();
        }

        try
        {
            return target.Deserialize<T>(JsonOptions)
                ?? throw new InvalidOperationException($"The {typeof(T).Name} settings payload was empty.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid {typeof(T).Name} setting value: {ex.Message}", ex);
        }
    }

    public static JsonObject RedactedAppSettings(AppSettings settings)
    {
        var node = JsonSerializer.SerializeToNode(settings, JsonOptions)?.AsObject() ?? new JsonObject();
        node.Remove("workspaceRoot");
        node["modelApiKey"] = string.IsNullOrWhiteSpace(settings.ModelApiKey) ? "" : "[configured]";
        node["modelApiKeyBackup"] = string.IsNullOrWhiteSpace(settings.ModelApiKeyBackup) ? "" : "[configured]";
        return node;
    }

    public static object RedactSensitiveData(object value, params string[] knownSecrets)
    {
        ArgumentNullException.ThrowIfNull(value);
        var node = JsonSerializer.SerializeToNode(value, JsonOptions) ?? JsonValue.Create("[unavailable]");
        RedactNode(node, knownSecrets.Where(secret => !string.IsNullOrWhiteSpace(secret)).ToArray());
        return node;
    }

    private static void RedactNode(JsonNode? node, IReadOnlyList<string> knownSecrets)
    {
        if (node is JsonObject obj)
        {
            foreach (var property in obj.ToArray())
            {
                if (IsSensitiveName(property.Key) && IsSecretValue(property.Value))
                {
                    obj[property.Key] = "[redacted]";
                    continue;
                }
                RedactNode(property.Value, knownSecrets);
                if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                    obj[property.Key] = RedactText(text, knownSecrets);
            }
            return;
        }

        if (node is not JsonArray array) return;
        for (var index = 0; index < array.Count; index++)
        {
            RedactNode(array[index], knownSecrets);
            if (array[index] is JsonValue value && value.TryGetValue<string>(out var text))
                array[index] = RedactText(text, knownSecrets);
        }
    }

    private static bool IsSensitiveName(string name)
    {
        var normalized = Regex.Replace(name, "[-_]", "").ToLowerInvariant();
        return normalized is "token" or "accesstoken" or "refreshtoken" or "credential"
            || normalized.Contains("apikey", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("authorization", StringComparison.Ordinal)
            || normalized.Contains("protectedtoken", StringComparison.Ordinal)
            || normalized.Contains("sessiontoken", StringComparison.Ordinal);
    }

    private static bool IsSecretValue(JsonNode? value)
    {
        if (value is not JsonValue scalar) return true;
        return scalar.TryGetValue<string>(out _);
    }

    private static string RedactText(string text, IReadOnlyList<string> knownSecrets)
    {
        var redacted = text ?? "";
        foreach (var secret in knownSecrets)
            redacted = redacted.Replace(secret, "[redacted]", StringComparison.Ordinal);
        return LogFileService.RedactSensitiveText(redacted, "");
    }
}
