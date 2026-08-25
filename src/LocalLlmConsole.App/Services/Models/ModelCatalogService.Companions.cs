namespace LocalLlmConsole.Services;

public sealed partial class ModelCatalogService
{
    private enum SpeculativeCompanionKind
    {
        Unknown,
        Mtp,
        DSpark,
        DFlash,
        Eagle3,
        DraftModel
    }

    public static string? ResolveVisionProjectorPath(string modelPath, string configuredProjectorPath)
    {
        if (VisionProjectorSelection.IsEmbeddedOrMainModel(modelPath, configuredProjectorPath))
            return null;

        if (!string.IsNullOrWhiteSpace(configuredProjectorPath))
        {
            var fullPath = Path.GetFullPath(configuredProjectorPath.Trim());
            return File.Exists(fullPath) ? fullPath : null;
        }

        return FindVisionProjector(modelPath);
    }

    public static string? FindVisionProjector(string modelPath)
        => FindVisionProjectors(modelPath).FirstOrDefault();

    public static IReadOnlyList<string> FindVisionProjectors(string modelPath)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        return CandidateCompanions(folder)
            .Where(file =>
            {
                var name = Path.GetFileName(file);
                return !string.Equals(Path.GetFullPath(file), Path.GetFullPath(modelPath), StringComparison.OrdinalIgnoreCase)
                    && LooksLikeVisionProjectorName(name)
                    && LooksCompatibleWithMainModel(Path.GetFileName(modelPath), name);
            })
            .OrderBy(file => Path.GetFileName(file).Contains("f16", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string? FindDraftModel(string modelPath)
        => FindDraftModels(modelPath).FirstOrDefault();

    public static string? FindDraftModel(string modelPath, string speculativeType)
        => FindDraftModels(modelPath, speculativeType).FirstOrDefault();

    public static string? FindMtpHead(string modelPath)
        => FindDraftModels(modelPath, LaunchSettingMetadataService.AtomicMtpSpeculativeType).FirstOrDefault();

    public static string? ResolveDraftModelPath(
        string modelPath,
        string configuredDraftPath,
        string speculativeType)
    {
        var normalizedType = LaunchSettingMetadataService.NormalizeSpeculativeType(speculativeType);
        if (!normalizedType.StartsWith("draft-", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!string.IsNullOrWhiteSpace(configuredDraftPath))
            return configuredDraftPath.Trim();
        if (normalizedType.Equals("draft-mtp", StringComparison.OrdinalIgnoreCase)
            && HasEmbeddedDraftMtp(modelPath))
            return null;
        return FindDraftModel(modelPath, normalizedType);
    }

    public static bool HasEmbeddedDraftMtp(string modelPath)
    {
        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath)) return false;

        return HasPositiveNextNPredictLayers(GgufMetadataReader.TryRead(modelPath));
    }

    public static IReadOnlyList<string> FindDraftModels(string modelPath)
        => FindDraftModels(modelPath, "");

    public static IReadOnlyList<string> FindDraftModels(string modelPath, string speculativeType)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(modelPath));
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return [];

        var mainPath = Path.GetFullPath(modelPath);
        var normalizedType = LaunchSettingMetadataService.NormalizeSpeculativeType(speculativeType);
        return CandidateCompanions(folder)
            .Select(file => (File: file, Kind: ClassifySpeculativeCompanion(file)))
            .Where(candidate =>
            {
                var file = candidate.File;
                var name = Path.GetFileName(file);
                return !string.Equals(Path.GetFullPath(file), mainPath, StringComparison.OrdinalIgnoreCase)
                    && name.EndsWith(".gguf", StringComparison.OrdinalIgnoreCase)
                    && !LooksLikeVisionProjectorName(name)
                    && candidate.Kind != SpeculativeCompanionKind.Unknown
                    && LooksCompatibleWithMainModel(
                        Path.GetFileName(modelPath),
                        name,
                        requireSameParameterSize: candidate.Kind != SpeculativeCompanionKind.DraftModel)
                    && MatchesSpeculativeType(candidate.Kind, normalizedType);
            })
            .OrderBy(candidate => SpeculativeCompanionPriority(candidate.Kind))
            .ThenBy(candidate => candidate.File, StringComparer.OrdinalIgnoreCase)
            .Select(candidate => candidate.File)
            .ToArray();
    }

    public static string? ResolveMtpHeadPath(
        string modelPath,
        string configuredHeadPath,
        string speculativeType)
    {
        if (!LaunchSettingMetadataService.IsAtomicMtpSpeculativeType(speculativeType))
            return null;
        if (!string.IsNullOrWhiteSpace(configuredHeadPath))
            return configuredHeadPath.Trim();
        return FindMtpHead(modelPath);
    }

    private static SpeculativeCompanionKind ClassifySpeculativeCompanion(string path)
    {
        var name = Path.GetFileName(path).Replace('_', '-').Replace('.', '-');
        if (name.Contains("dspark", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DSpark;
        if (name.Contains("dflash", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DFlash;
        if (name.Contains("eagle3", StringComparison.OrdinalIgnoreCase)
            || name.Contains("eagle-3", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.Eagle3;

        var metadata = GgufMetadataReader.TryRead(path);
        var architecture = MetadataString(metadata, "general.architecture");
        if (architecture.Equals("eagle3", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.Eagle3;
        if (architecture.Equals("dflash", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DFlash;
        if (HasPositiveNextNPredictLayers(metadata)
            || name.StartsWith("mtp-", StringComparison.OrdinalIgnoreCase)
            || name.Contains("-mtp-head", StringComparison.OrdinalIgnoreCase)
            || name.Contains("mtp-head", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.Mtp;
        if (name.Contains("-draft-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)
            || name.Contains("-spec-", StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("spec-", StringComparison.OrdinalIgnoreCase))
            return SpeculativeCompanionKind.DraftModel;
        return SpeculativeCompanionKind.Unknown;
    }

    private static bool MatchesSpeculativeType(SpeculativeCompanionKind kind, string speculativeType)
        => speculativeType switch
        {
            "draft-mtp" or LaunchSettingMetadataService.AtomicMtpSpeculativeType => kind == SpeculativeCompanionKind.Mtp,
            "draft-dspark" => kind == SpeculativeCompanionKind.DSpark,
            "draft-dflash" => kind == SpeculativeCompanionKind.DFlash,
            "draft-eagle3" => kind == SpeculativeCompanionKind.Eagle3,
            "draft-simple" => kind == SpeculativeCompanionKind.DraftModel,
            "" or "none" => kind != SpeculativeCompanionKind.Unknown,
            _ => false
        };

    private static int SpeculativeCompanionPriority(SpeculativeCompanionKind kind) => kind switch
    {
        SpeculativeCompanionKind.Mtp => 0,
        SpeculativeCompanionKind.DSpark => 1,
        SpeculativeCompanionKind.DFlash => 2,
        SpeculativeCompanionKind.Eagle3 => 3,
        SpeculativeCompanionKind.DraftModel => 4,
        _ => 5
    };

    private static string MetadataString(IReadOnlyDictionary<string, object?> metadata, string key)
        => metadata.TryGetValue(key, out var value) ? Convert.ToString(value, CultureInfo.InvariantCulture) ?? "" : "";

    private static bool LooksCompatibleWithMainModel(
        string mainName,
        string companionName,
        bool requireSameParameterSize = true)
    {
        var mainFamily = FamilyVersion(mainName);
        var companionFamily = FamilyVersion(companionName);
        if (mainFamily is not null && companionFamily is not null
            && !mainFamily.Equals(companionFamily, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!requireSameParameterSize) return true;

        var mainSize = ParameterSize(mainName);
        var companionSize = ParameterSize(companionName);
        return mainSize is null || companionSize is null
            || mainSize.Equals(companionSize, StringComparison.OrdinalIgnoreCase);
    }

    private static string? FamilyVersion(string name)
    {
        var match = Regex.Match(
            name ?? "",
            @"(?ix)(?:^|[^a-z0-9])
              (?<family>qwen|gemma|llama|mistral|ministral|mixtral|pixtral|deepseek|glm|phi|command-r|internvl|minicpm)
              (?:[\s._-]+(?:small|large|nemo))?
              [\s._-]*(?:v|r)?(?<version>\d+(?:[._-]\d+)?)
              (?:[^0-9]|$)");
        if (!match.Success) return null;
        var version = match.Groups["version"].Value.Replace('_', '.').Replace('-', '.');
        return $"{match.Groups["family"].Value.ToLowerInvariant()}:{version}";
    }

    private static string? ParameterSize(string name)
    {
        var match = Regex.Match(name ?? "", @"(?i)(?:^|[^a-z0-9])(?<size>\d+(?:\.\d+)?)\s*b(?:[^a-z0-9]|$)");
        return match.Success ? match.Groups["size"].Value : null;
    }

    private static bool IsPositiveNumber(object? value) => value switch
    {
        byte number => number > 0,
        sbyte number => number > 0,
        ushort number => number > 0,
        short number => number > 0,
        uint number => number > 0,
        int number => number > 0,
        ulong number => number > 0,
        long number => number > 0,
        float number => number > 0,
        double number => number > 0,
        _ => false
    };

    private static bool HasPositiveNextNPredictLayers(IReadOnlyDictionary<string, object?> metadata)
        => metadata.Any(pair => pair.Key.EndsWith(".nextn_predict_layers", StringComparison.OrdinalIgnoreCase)
            && IsPositiveNumber(pair.Value));

    private static IEnumerable<string> CandidateCompanions(string folder)
    {
        // Automatic pairing is deliberately confined to the selected model's directory.
        // Parent/child scans can silently attach a sidecar belonging to a different model.
        return Directory.EnumerateFiles(folder, "*.gguf", SearchOption.TopDirectoryOnly).Take(500);
    }

    private static bool LooksLikeVisionProjectorName(string name)
    {
        var normalized = (name ?? "").Replace('_', '-').Replace('.', '-');
        return normalized.Contains("mmproj", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("projector", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("clip", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vision-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("visual-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("image-head", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-vision", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-visual", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("head-image", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("mtp-vision", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("vision-mtp", StringComparison.OrdinalIgnoreCase);
    }

}
