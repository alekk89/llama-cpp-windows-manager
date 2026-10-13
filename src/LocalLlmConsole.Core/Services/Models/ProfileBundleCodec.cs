using System.Text.Json.Serialization;

namespace LocalLlmConsole.Services;

/// <summary>The versioned, portable contract shared by file, clipboard and model-to-model transfer.</summary>
public static class ProfileBundleCodec
{
    public const int SchemaVersion = 1;
    public const int MaximumBytes = 4 * 1024 * 1024;
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static ProfileBundle Read(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || Encoding.UTF8.GetByteCount(text) > MaximumBytes)
            throw new InvalidOperationException("Profile bundles must contain JSON and be at most 4 MiB.");
        try
        {
            // Reject duplicate properties rather than silently selecting the last setting.
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, MaxDepth = 32 });
            CheckDuplicates(document.RootElement);
            var bundle = JsonSerializer.Deserialize<ProfileBundle>(text, JsonOptions)
                ?? throw new InvalidOperationException("The profile bundle is empty.");
            Validate(bundle);
            return bundle;
        }
        catch (JsonException ex) { throw new InvalidOperationException($"Invalid profile bundle: {ex.Message}", ex); }
    }

    public static string Write(ProfileBundle bundle)
    {
        Validate(bundle);
        var text = JsonSerializer.Serialize(bundle, JsonOptions);
        if (Encoding.UTF8.GetByteCount(text) > MaximumBytes) throw new InvalidOperationException("Profile bundle exceeds 4 MiB.");
        return text;
    }

    public static JsonObject PortableSettings(ModelLaunchSettings settings)
    {
        var node = SettingsNode(settings);
        node.Remove("runtimeId");
        foreach (var key in new[] { "visionProjectorPath", "specDraftModelPath", "mtpHeadPath" })
        {
            var path = node[key]?.GetValue<string>() ?? "";
            if (!string.IsNullOrWhiteSpace(path) && !path.Equals("embedded", StringComparison.OrdinalIgnoreCase))
                node[key] = Path.GetFileName(path.Replace('\\', '/'));
        }
        RejectCredentials(settings.CustomParameters);
        return node;
    }

    public static JsonObject SettingsNode(ModelLaunchSettings settings)
        => JsonSerializer.SerializeToNode(settings, JsonOptions)!.AsObject();

    public static ModelLaunchSettings ApplySettings(ModelLaunchSettings defaults, JsonObject patch, bool portable = false)
    {
        var target = SettingsNode(defaults);
        foreach (var pair in patch)
        {
            if (!target.ContainsKey(pair.Key) || (portable && pair.Key == "runtimeId"))
                throw new InvalidOperationException($"Unknown or non-portable launch setting '{pair.Key}'.");
            if (pair.Value is null) throw new InvalidOperationException($"Launch setting '{pair.Key}' cannot be null.");
            target[pair.Key] = pair.Value.DeepClone();
        }
        try
        {
            var settings = target.Deserialize<ModelLaunchSettings>(JsonOptions)!;
            ValidateSettings(settings);
            return settings;
        }
        catch (JsonException ex) { throw new InvalidOperationException($"Invalid launch setting: {ex.Message}", ex); }
    }

    public static void ValidateSettings(ModelLaunchSettings s)
    {
        if (s.ContextSize < 0 || s.GpuLayers < -1 || s.Port < 0 || s.Port > 65535
            || s.ParallelSlots < 1 || s.BatchSize < 1 || s.MicroBatchSize < 1 || s.Threads < 0
            || !double.IsFinite(s.Temperature) || s.Temperature < 0 || !double.IsFinite(s.TopP) || s.TopP is < 0 or > 1
            || !double.IsFinite(s.MinP) || s.MinP is < 0 or > 1 || s.TopK < 0
            || s.VulkanAllocationBlockSizeMiB < 0)
            throw new InvalidOperationException("Profile contains invalid context, GPU, port, batching or sampling values.");
        if (SettingsNode(s).Any(pair => pair.Value is null)) throw new InvalidOperationException("Profile settings cannot contain null values.");
        RejectCredentials(s.CustomParameters);
        foreach (var value in new[] { s.ReasoningMode, s.VisionMode, s.FlashAttention, s.KvOffload, s.KvUnified,
            s.ContinuousBatching, s.JinjaMode, s.MmapMode, s.PromptCacheMode, s.ContextCheckpointsMode, s.ReasoningPreserve })
            Choice(value, ["auto", "on", "off"]);
        Choice(s.MlockMode, ["auto", "on", "off"]);
        Choice(s.GpuMode, ["auto", "single", "layer", "row", "tensor"]);
        Choice(s.ReasoningFormat, ["auto", "none", "deepseek", "deepseek-legacy"]);
        Choice(s.ReasoningEffort, ["default", "minimal", "low", "medium", "high", "xhigh", "max"]);
        Choice(s.RopeScaling, ["auto", "none", "linear", "yarn"]);
        Choice(s.SpeculativeType, SpeculativeTypePolicy.SupportedTypes);
        foreach (var cache in new[] { s.CacheTypeK, s.CacheTypeV, s.SpecDraftCacheTypeK, s.SpecDraftCacheTypeV })
            Choice(cache, ["f16", "q8_0", "q4_0", "q4_1", "iq4_nl", "q5_0", "q5_1", "f32", "bf16"]);
    }

    private static void Choice(string value, IReadOnlyList<string> accepted)
    {
        if (!accepted.Contains(value, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Unsupported launch setting value '{value}'. Expected: {string.Join(", ", accepted)}.");
    }

    private static void Validate(ProfileBundle bundle)
    {
        if (bundle.SchemaVersion != SchemaVersion) throw new InvalidOperationException($"Unsupported profile schema version {bundle.SchemaVersion}.");
        if (bundle.Model is null || bundle.Profiles is null || bundle.Profiles.Count is < 1 or > 100)
            throw new InvalidOperationException("A bundle must describe one model and between 1 and 100 profiles.");
        foreach (var profile in bundle.Profiles)
        {
            if (profile is null || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 200 || profile.Settings is null)
                throw new InvalidOperationException("Each profile needs a name (at most 200 characters) and a settings object.");
            if (profile.Annotations?.SettingNotes?.Any(note => string.IsNullOrWhiteSpace(note.Key) || note.Value is null) == true)
                throw new InvalidOperationException("Setting notes require a setting name and text.");
        }
    }

    private static void RejectCredentials(string customParameters)
    {
        if (Regex.IsMatch(customParameters ?? "", @"(?:^|\s)--(?:api-key(?:-file)?|hf-token)(?:\s|=|$)", RegexOptions.IgnoreCase))
            throw new InvalidOperationException("Remove credential arguments from Custom parameters before transferring this profile.");
    }

    private static void CheckDuplicates(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in node.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new InvalidOperationException($"Duplicate JSON property '{property.Name}'.");
                CheckDuplicates(property.Value);
            }
        }
        else if (node.ValueKind == JsonValueKind.Array)
            foreach (var item in node.EnumerateArray()) CheckDuplicates(item);
    }
}
