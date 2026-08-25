namespace LocalLlmConsole.Services;

public enum GgufFileRole
{
    MainModel,
    VisionProjector,
    SpeculativeAssistant,
    Ambiguous,
    Invalid
}

public enum GgufClassificationConfidence
{
    Metadata,
    MetadataAndName,
    NameFallback,
    Unknown
}

public sealed record GgufFileClassification(
    string Path,
    GgufFileRole Role,
    GgufClassificationConfidence Confidence,
    string Reason,
    string Architecture = "",
    string GeneralType = "",
    bool EmbeddedDraftMtp = false);

public sealed record ModelScanResult(
    IReadOnlyList<ModelRecord> RegisteredModels,
    IReadOnlyList<GgufFileClassification> Files)
{
    public int DiscoveredCount => Files.Count;
    public int RegisteredCount => RegisteredModels.Count;
    public int CompanionCount => Files.Count(file => file.Role is GgufFileRole.VisionProjector or GgufFileRole.SpeculativeAssistant);
    public int AmbiguousCount => Files.Count(file => file.Role == GgufFileRole.Ambiguous);
    public int InvalidCount => Files.Count(file => file.Role == GgufFileRole.Invalid);
    public IReadOnlyList<GgufFileClassification> Skipped => Files.Where(file => file.Role != GgufFileRole.MainModel).ToArray();

    public string Summary
        => $"Registered {RegisteredCount} main model(s) from {DiscoveredCount} GGUF file(s); "
            + $"identified {CompanionCount} companion(s), {AmbiguousCount} ambiguous file(s), and {InvalidCount} invalid file(s).";
}

public sealed partial class ModelCatalogService
{
    public static GgufFileClassification ClassifyGguf(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return InvalidClassification(path ?? "", "The file path is empty.");

        string fullPath;
        try { fullPath = Path.GetFullPath(path); }
        catch { return InvalidClassification(path, "The file path is invalid."); }

        if (!fullPath.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase))
            return InvalidClassification(fullPath, "The file does not have a .gguf extension.");
        if (!File.Exists(fullPath))
            return InvalidClassification(fullPath, "The GGUF file does not exist.");

        var metadata = GgufMetadataReader.TryRead(fullPath);
        if (metadata.Count == 0)
            return InvalidClassification(fullPath, "The file does not contain a readable supported GGUF header.");

        var architecture = MetadataString(metadata, "general.architecture");
        var generalType = MetadataString(metadata, "general.type");
        var embeddedDraftMtp = HasPositiveNextNPredictLayers(metadata);
        var name = Path.GetFileName(fullPath);

        if (IsVisionArchitecture(architecture))
            return Classification(GgufFileRole.VisionProjector, GgufClassificationConfidence.Metadata,
                $"GGUF architecture '{architecture}' identifies a vision projector.");
        if (IsStandaloneSpeculativeArchitecture(architecture))
            return Classification(GgufFileRole.SpeculativeAssistant, GgufClassificationConfidence.Metadata,
                $"GGUF architecture '{architecture}' identifies a standalone speculative assistant.");
        if (!string.IsNullOrWhiteSpace(generalType)
            && !generalType.Equals("model", StringComparison.OrdinalIgnoreCase))
            return Classification(GgufFileRole.Ambiguous, GgufClassificationConfidence.Metadata,
                $"GGUF general.type '{generalType}' does not identify a main model.");

        var companionNameReason = ExplicitCompanionNameReason(name);
        var hasModelMetadata = generalType.Equals("model", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrWhiteSpace(architecture);
        if (hasModelMetadata && companionNameReason is not null)
            return Classification(GgufFileRole.Ambiguous, GgufClassificationConfidence.MetadataAndName,
                $"The GGUF metadata describes a model, but the filename {companionNameReason}");
        if (hasModelMetadata)
        {
            var typeDescription = string.IsNullOrWhiteSpace(generalType) ? "model architecture" : $"general.type '{generalType}'";
            return Classification(GgufFileRole.MainModel, GgufClassificationConfidence.Metadata,
                $"GGUF {typeDescription} identifies a loadable main model.");
        }

        if (LooksLikeVisionProjectorName(name))
            return Classification(GgufFileRole.VisionProjector, GgufClassificationConfidence.NameFallback,
                "The readable GGUF lacks role metadata and its filename looks like a vision projector.");
        if (companionNameReason is not null)
            return Classification(GgufFileRole.SpeculativeAssistant, GgufClassificationConfidence.NameFallback,
                $"The readable GGUF lacks role metadata and its filename {companionNameReason}");

        return Classification(GgufFileRole.Ambiguous, GgufClassificationConfidence.Unknown,
            "The GGUF is readable, but it does not expose enough metadata to determine its role.");

        GgufFileClassification Classification(
            GgufFileRole role,
            GgufClassificationConfidence confidence,
            string reason)
            => new(fullPath, role, confidence, reason, architecture, generalType, embeddedDraftMtp);
    }

    private static GgufFileClassification InvalidClassification(string path, string reason)
        => new(path, GgufFileRole.Invalid, GgufClassificationConfidence.Unknown, reason);

    private static GgufFileClassification ConfirmedClassification(
        GgufFileClassification classification,
        IReadOnlyDictionary<string, string> confirmedIdentities)
        => classification.Role != GgufFileRole.Invalid
            && confirmedIdentities.TryGetValue(NormalizePath(classification.Path), out var confirmedIdentity)
            && string.Equals(confirmedIdentity, ClassificationIdentity(classification), StringComparison.Ordinal)
                ? classification with
                {
                    Role = GgufFileRole.MainModel,
                    Confidence = GgufClassificationConfidence.MetadataAndName,
                    Reason = $"Previously confirmed by an explicit file import. Initial classification: {classification.Role}."
                }
                : classification;

    private static string ClassificationIdentity(GgufFileClassification classification)
    {
        var info = new FileInfo(classification.Path);
        var identity = string.Join('|',
            info.Length.ToString(CultureInfo.InvariantCulture),
            info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
            classification.Architecture.Trim().ToLowerInvariant(),
            classification.GeneralType.Trim().ToLowerInvariant(),
            classification.EmbeddedDraftMtp ? "1" : "0");
        return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    private static string? ConfirmedMainModelIdentity(ModelRecord model)
    {
        try
        {
            var metadata = JsonNode.Parse(model.MetadataJson);
            return metadata?["userConfirmedMainModel"]?.GetValue<bool>() == true
                ? metadata["confirmedMainModelIdentity"]?.GetValue<string>()
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static bool IsUserConfirmedMainModel(ModelRecord model)
    {
        try
        {
            return JsonNode.Parse(model.MetadataJson)?["userConfirmedMainModel"]?.GetValue<bool>() == true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsVisionArchitecture(string architecture)
        => architecture.Equals("clip", StringComparison.OrdinalIgnoreCase)
            || architecture.EndsWith("-projector", StringComparison.OrdinalIgnoreCase)
            || architecture.EndsWith("_projector", StringComparison.OrdinalIgnoreCase);

    private static bool IsStandaloneSpeculativeArchitecture(string architecture)
        => architecture.Equals("eagle3", StringComparison.OrdinalIgnoreCase)
            || architecture.Equals("dflash", StringComparison.OrdinalIgnoreCase)
            || architecture.EndsWith("-assistant", StringComparison.OrdinalIgnoreCase)
            || architecture.EndsWith("_assistant", StringComparison.OrdinalIgnoreCase);

    private static string? ExplicitCompanionNameReason(string name)
    {
        var normalized = (name ?? "").Replace('_', '-').Replace('.', '-');
        if (normalized.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)) return "starts with the conventional MTP companion prefix.";
        if (normalized.Contains("-mtp-head", StringComparison.OrdinalIgnoreCase)) return "contains an explicit MTP-head marker.";
        if (normalized.Contains("-mtp-only", StringComparison.OrdinalIgnoreCase)) return "contains an explicit MTP-only marker.";
        if (normalized.Contains("dspark", StringComparison.OrdinalIgnoreCase)) return "contains a DSpark companion marker.";
        if (normalized.Contains("dflash", StringComparison.OrdinalIgnoreCase)) return "contains a DFlash companion marker.";
        if (normalized.Contains("eagle3", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("eagle-3", StringComparison.OrdinalIgnoreCase)) return "contains an EAGLE3 companion marker.";
        if (normalized.Contains("-draft-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)) return "contains a draft-model marker.";
        if (normalized.Contains("-spec-", StringComparison.OrdinalIgnoreCase)
            || normalized.StartsWith("spec-", StringComparison.OrdinalIgnoreCase)) return "contains a speculative-model marker.";
        return null;
    }
}
