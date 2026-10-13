namespace LocalLlmConsole.Services;

public sealed partial class ProfileTransferWorkflowService
{
    private static ModelLaunchSettings ResolveSettings(ModelRecord model, ProfileModelHint sourceModel,
        PortableLaunchProfile source, ProfileImportChoice choice, ModelLaunchSettings settings,
        IReadOnlyList<RuntimeRecord> runtimes, IReadOnlyList<string> gpuNames, List<string> warnings, List<string> errors)
    {
        var available = runtimes.Where(RuntimeAvailabilityService.IsAvailable).ToArray();
        var runtime = !string.IsNullOrWhiteSpace(choice.RuntimeId)
            ? available.FirstOrDefault(r => r.Id == choice.RuntimeId)
            : available.FirstOrDefault(r => r.Name == source.Runtime?.Name && Matches(r, source.Runtime))
                ?? available.FirstOrDefault(r => Matches(r, source.Runtime));
        if (runtime is null) errors.Add("Select an installed, available runtime.");
        else
        {
            settings = settings with { RuntimeId = runtime.Id };
            if (runtime.Name != source.Runtime?.Name) warnings.Add($"Runtime mapped to {runtime.Name} ({runtime.Backend}, {runtime.Mode}). Review version-dependent flags.");
        }
        if (!sourceModel.FileName.Equals(Path.GetFileName(model.ModelPath), StringComparison.OrdinalIgnoreCase))
            warnings.Add("Destination GGUF differs from the source. Review context, memory, chat template and advanced flags before loading.");
        var architecture = ModelHint(model).Architecture;
        if (!string.IsNullOrEmpty(sourceModel.Architecture) && !string.IsNullOrEmpty(architecture)
            && sourceModel.Architecture != architecture) warnings.Add($"Architecture differs: {sourceModel.Architecture} → {architecture}.");

        settings = settings with
        {
            VisionProjectorPath = ResolveCompanion(settings.VisionProjectorPath, model.ModelPath,
                ModelCatalogService.FindVisionProjectors(model.ModelPath), "vision projector", warnings, errors),
            SpecDraftModelPath = ResolveCompanion(settings.SpecDraftModelPath, model.ModelPath,
                ModelCatalogService.FindDraftModels(model.ModelPath, settings.SpeculativeType), "draft model", warnings, errors),
            MtpHeadPath = ResolveCompanion(settings.MtpHeadPath, model.ModelPath,
                ModelCatalogService.FindDraftModels(model.ModelPath, LaunchSettingMetadataService.AtomicMtpSpeculativeType), "MTP head", warnings, errors)
        };
        if (!string.IsNullOrWhiteSpace(settings.GpuDevices) || !string.IsNullOrWhiteSpace(settings.GpuSplit)
            || !string.IsNullOrWhiteSpace(settings.TensorBufferOverrides))
        {
            if (source.GpuNames is null || source.GpuNames.Count == 0 || !source.GpuNames.SequenceEqual(gpuNames))
            {
                warnings.Add("GPU layout differs or could not be verified. GPU device, split and tensor buffer selections reset to automatic; review before loading.");
                settings = settings with { GpuDevices = "", GpuSplit = "", TensorBufferOverrides = "", GpuMode = "auto" };
            }
        }
        if (choice.Settings is not null)
        {
            if (choice.Settings.ContainsKey("runtimeId")) throw new InvalidOperationException("Select runtimeId through the local runtime mapping, not settings overrides.");
            settings = ProfileBundleCodec.ApplySettings(settings, choice.Settings);
        }
        // Explicit local overrides are validated too; exported relative references may never escape the model folder.
        foreach (var path in new[] { settings.VisionProjectorPath, settings.SpecDraftModelPath, settings.MtpHeadPath })
            if (!string.IsNullOrWhiteSpace(path) && path != "embedded" && (!Path.IsPathFullyQualified(path) || !File.Exists(path)))
                errors.Add($"Companion file is missing: {path}. Browse to a local file or clear the setting.");
        if (settings.SpecDraftModelPath == "embedded" || settings.MtpHeadPath == "embedded")
            errors.Add("For embedded draft MTP, leave the external draft/head path empty. Only the vision projector accepts 'embedded'.");
        settings = ResolveRequiredCompanions(model, settings, warnings, errors);
        errors.AddRange(LaunchSettingMetadataService.ValidateGpuSettings(settings.GpuMode, settings.GpuDevices, settings.GpuSplit));
        foreach (var (path, type, vision) in new[]
        {
            (settings.VisionProjectorPath, "", true),
            (settings.SpecDraftModelPath, settings.SpeculativeType, false),
            (settings.MtpHeadPath, LaunchSettingMetadataService.AtomicMtpSpeculativeType, false)
        })
            if (!string.IsNullOrWhiteSpace(path) && path != "embedded" && File.Exists(path)
                && !ModelCatalogService.IsCompatibleCompanion(model.ModelPath, path, type, vision))
                errors.Add($"The selected companion conflicts with the destination model or helper type: {path}.");
        if (!string.IsNullOrWhiteSpace(settings.CustomParameters))
            warnings.Add("Review Custom parameters for runtime support and machine-specific file paths.");
        if (!string.IsNullOrWhiteSpace(settings.Host))
            warnings.Add($"Saved host override: {settings.Host}. The application's direct-model access policy still applies when loading.");
        return settings;
    }

    private static ModelLaunchSettings ResolveRequiredCompanions(ModelRecord model, ModelLaunchSettings settings,
        List<string> warnings, List<string> errors)
    {
        if (settings.VisionMode == "on" && string.IsNullOrWhiteSpace(settings.VisionProjectorPath))
        {
            var candidates = ModelCatalogService.FindVisionProjectors(model.ModelPath);
            if (candidates.Count == 1) settings = settings with { VisionProjectorPath = candidates[0] };
            else errors.Add("Vision is enabled. Select a compatible local projector or disable vision.");
        }
        if (settings.SpeculativeType.StartsWith("draft-", StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrWhiteSpace(settings.SpecDraftModelPath))
        {
            if (settings.SpeculativeType == "draft-mtp" && ModelCatalogService.HasEmbeddedDraftMtp(model.ModelPath))
                warnings.Add("Using the destination GGUF's embedded draft MTP tensors.");
            else
            {
                var candidates = ModelCatalogService.FindDraftModels(model.ModelPath, settings.SpeculativeType);
                if (candidates.Count == 1) settings = settings with { SpecDraftModelPath = candidates[0] };
                else errors.Add("Speculative decoding requires a compatible local draft model. Select one or disable speculative decoding.");
            }
        }
        if (LaunchSettingMetadataService.IsAtomicMtpSpeculativeType(settings.SpeculativeType) && string.IsNullOrWhiteSpace(settings.MtpHeadPath))
        {
            var candidates = ModelCatalogService.FindDraftModels(model.ModelPath, LaunchSettingMetadataService.AtomicMtpSpeculativeType);
            if (candidates.Count == 1) settings = settings with { MtpHeadPath = candidates[0] };
            else errors.Add("Atomic MTP requires a compatible local head. Select one or disable speculative decoding.");
        }
        if (settings.VisionProjectorPath == "embedded")
            warnings.Add("Embedded vision is explicit. Verify that the selected GGUF package and runtime support it before loading.");
        return settings;
    }

    private static bool Matches(RuntimeRecord runtime, ProfileRuntimeHint? hint)
        => hint is not null && runtime.Backend.ToString().Equals(hint.Backend, StringComparison.OrdinalIgnoreCase)
            && runtime.Mode.ToString().Equals(hint.Mode, StringComparison.OrdinalIgnoreCase);

    private static string ResolveCompanion(string reference, string modelPath, IReadOnlyList<string> candidates,
        string label, List<string> warnings, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(reference) || reference.Equals("embedded", StringComparison.OrdinalIgnoreCase)) return reference;
        // Bundles contain names, never source-machine paths. Explicit overrides are applied separately.
        if (reference != Path.GetFileName(reference.Replace('\\', '/')))
        {
            errors.Add($"The {label} reference must be a filename. Browse to a local companion in preview.");
            return "";
        }
        var exact = candidates.FirstOrDefault(path => Path.GetFileName(path).Equals(reference, StringComparison.OrdinalIgnoreCase));
        var resolved = exact ?? (candidates.Count == 1 ? candidates[0] : null);
        if (resolved is not null)
        {
            warnings.Add($"{label}: {reference} → {resolved}");
            return resolved;
        }
        warnings.Add($"No unambiguous compatible {label} for {reference}. Browse to a local file or explicitly clear the setting.");
        return reference;
    }
}
