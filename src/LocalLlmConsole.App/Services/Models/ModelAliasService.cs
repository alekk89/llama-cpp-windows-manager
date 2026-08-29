namespace LocalLlmConsole.Services;

public static class ModelAliasService
{
    public const string LaunchAliasKind = "launchAlias";

    public static bool IsLaunchAlias(ModelRecord model)
        => model.Ownership == OwnershipKind.RegistryOnly
           && string.Equals(MetadataValue(model, "recordKind"), LaunchAliasKind, StringComparison.OrdinalIgnoreCase);

    public static string SourceModelId(ModelRecord model)
        => MetadataValue(model, "sourceModelId");

    public static string SourceModelName(ModelRecord model)
        => MetadataValue(model, "sourceModelName");

    public static string CreateMetadata(ModelRecord source, IReadOnlyList<ModelRecord> allModels)
    {
        var sourceModel = IsLaunchAlias(source)
            ? allModels.FirstOrDefault(model => string.Equals(model.Id, SourceModelId(source), StringComparison.OrdinalIgnoreCase))
            : source;
        sourceModel ??= source;

        var metadata = ReadMetadata(source.MetadataJson);
        metadata["recordKind"] = LaunchAliasKind;
        metadata["sourceModelId"] = sourceModel.Id;
        metadata["sourceModelName"] = sourceModel.Name;
        metadata["createdFromModelId"] = source.Id;
        metadata["createdFromModelName"] = source.Name;
        metadata["createdAt"] = DateTimeOffset.UtcNow.ToString("O");
        metadata["description"] = "Saved launch variant. Uses the same GGUF file with a separate model id, name, port, and launch profile.";
        return metadata.ToJsonString();
    }

    private static string MetadataValue(ModelRecord model, string key)
    {
        try
        {
            return JsonNode.Parse(model.MetadataJson)?[key]?.ToString() ?? "";
        }
        catch
        {
            return "";
        }
    }

    private static JsonObject ReadMetadata(string metadataJson)
    {
        try
        {
            return JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
        }
        catch
        {
            return new JsonObject();
        }
    }
}
