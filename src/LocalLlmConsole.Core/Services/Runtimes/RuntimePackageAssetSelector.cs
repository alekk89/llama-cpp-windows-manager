namespace LocalLlmConsole.Services;

public static class RuntimePackageAssetSelector
{
    public static RuntimePackageSelection SelectAssets(RuntimePackagePreset preset, RuntimePackageRelease release, string cudaPackagePreference = "latest")
        => preset.Id switch
        {
            "official-prebuilt-windows-cuda" => SelectWindowsCudaAssets(preset, release, cudaPackagePreference),
            "official-prebuilt-cuda" => SelectLinuxCudaAssets(preset, release, cudaPackagePreference),
            "atomic-prebuilt-windows-cuda" => SelectSingleAsset(preset, release, @"^llama-turboquant(?:-.+)?-win-(?:cu|cuda)-?[0-9.]*-x64\.zip$"),
            "atomic-prebuilt-cuda" => SelectSingleAsset(preset, release, @"^llama-turboquant(?:-.+)?-(?:ubuntu|linux|wsl)-(?:cu|cuda)-?[0-9.]*-x64\.(?:tar\.gz|tgz|zip)$"),
            "thetom-prebuilt-windows-cuda" => SelectSingleAsset(preset, release, @"^turboquant-plus-.+-windows-x64-cuda[0-9.]+\.zip$"),
            "thetom-prebuilt-vulkan" => SelectSingleAsset(preset, release, @"^turboquant-plus-.+-linux-x64-vulkan\.tar\.gz$"),
            "thetom-prebuilt-cpu" => SelectSingleAsset(preset, release, @"^turboquant-plus-.+-linux-x64-cpu\.tar\.gz$"),
            "official-prebuilt-windows-vulkan" => SelectSingleAsset(preset, release, @"^llama-.+-bin-win-vulkan-x64\.zip$"),
            "official-prebuilt-vulkan" => SelectSingleAsset(preset, release, @"^llama-.+-bin-ubuntu-vulkan-x64\.tar\.gz$"),
            "official-prebuilt-windows-sycl" => SelectSingleAsset(preset, release, @"^llama-.+-bin-win-sycl-x64\.zip$"),
            "official-prebuilt-sycl" => SelectSingleAsset(preset, release, @"^llama-.+-bin-(?:ubuntu|linux)(?:-24)?-sycl(?:-(?:fp16|f16|fp32))?-x64\.(?:tar\.gz|zip)$"),
            "official-prebuilt-windows-cpu" => SelectSingleAsset(preset, release, @"^llama-.+-bin-win-cpu-x64\.zip$"),
            "official-prebuilt-cpu" => SelectSingleAsset(preset, release, @"^llama-.+-bin-ubuntu-x64\.tar\.gz$"),
            _ => throw new InvalidOperationException($"No prebuilt asset rule exists for {preset.Label}.")
        };

    public static bool AssetSummariesMatch(string left, string right)
    {
        static string Normalize(string text)
            => string.Join("|", (text ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase));

        var normalizedLeft = Normalize(left);
        var normalizedRight = Normalize(right);
        return !string.IsNullOrWhiteSpace(normalizedLeft)
            && string.Equals(normalizedLeft, normalizedRight, StringComparison.OrdinalIgnoreCase);
    }

    private static RuntimePackageSelection SelectSingleAsset(RuntimePackagePreset preset, RuntimePackageRelease release, string pattern)
    {
        var asset = release.Assets.FirstOrDefault(asset => Regex.IsMatch(asset.Name, pattern, RegexOptions.IgnoreCase));
        if (asset is null)
            throw new RuntimePackageAssetUnavailableException($"Release {release.TagName} does not include a matching asset for {preset.Label}.");
        return new RuntimePackageSelection(preset, release.TagName, release.HtmlUrl, release.PublishedAt, asset, [], release.TargetCommit);
    }

    private static RuntimePackageSelection SelectWindowsCudaAssets(RuntimePackagePreset preset, RuntimePackageRelease release, string cudaPackagePreference)
    {
        var binaries = release.Assets
            .Select(asset => new { Asset = asset, Version = MatchGroup(asset.Name, @"^llama-.+-bin-win-cuda-(?<version>[0-9.]+)-x64\.zip$", "version") })
            .Where(match => !string.IsNullOrWhiteSpace(match.Version))
            .ToList();
        var runtimeDlls = release.Assets
            .Select(asset => new { Asset = asset, Version = MatchGroup(asset.Name, @"^cudart-llama-bin-win-cuda-(?<version>[0-9.]+)-x64\.zip$", "version") })
            .Where(match => !string.IsNullOrWhiteSpace(match.Version))
            .ToDictionary(match => match.Version, match => match.Asset, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in binaries.OrderByDescending(match => CudaVersionPreference(match.Version, cudaPackagePreference)))
        {
            if (runtimeDlls.TryGetValue(candidate.Version, out var dllAsset))
                return new RuntimePackageSelection(preset, release.TagName, release.HtmlUrl, release.PublishedAt, candidate.Asset, [dllAsset], release.TargetCommit);
        }

        throw new InvalidOperationException($"Release {release.TagName} does not include matching CUDA binaries and runtime DLLs for Windows.");
    }

    private static RuntimePackageSelection SelectLinuxCudaAssets(RuntimePackagePreset preset, RuntimePackageRelease release, string cudaPackagePreference)
    {
        var binaries = release.Assets
            .Select(asset => new
            {
                Asset = asset,
                Version = MatchGroup(asset.Name, @"^llama-.+-bin-(?:ubuntu|linux)-cuda(?:-(?<version>[0-9.]+))?-x64\.tar\.gz$", "version")
            })
            .Where(match => Regex.IsMatch(match.Asset.Name, @"^llama-.+-bin-(?:ubuntu|linux)-cuda(?:-[0-9.]+)?-x64\.tar\.gz$", RegexOptions.IgnoreCase))
            .ToList();
        if (binaries.Count == 0)
            throw new RuntimePackageAssetUnavailableException($"Release {release.TagName} does not publish an official CUDA WSL/Linux x64 prebuilt runtime.");

        var runtimeDlls = release.Assets
            .Select(asset => new
            {
                Asset = asset,
                Version = MatchGroup(asset.Name, @"^cudart-llama-bin-(?:ubuntu|linux)-cuda(?:-(?<version>[0-9.]+))?-x64\.tar\.gz$", "version")
            })
            .Where(match => Regex.IsMatch(match.Asset.Name, @"^cudart-llama-bin-(?:ubuntu|linux)-cuda(?:-[0-9.]+)?-x64\.tar\.gz$", RegexOptions.IgnoreCase))
            .GroupBy(match => match.Version ?? "", StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First().Asset, StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in binaries.OrderByDescending(match => CudaVersionPreference(match.Version, cudaPackagePreference)))
        {
            if (!string.IsNullOrWhiteSpace(candidate.Version) && runtimeDlls.TryGetValue(candidate.Version, out var dllAsset))
                return new RuntimePackageSelection(preset, release.TagName, release.HtmlUrl, release.PublishedAt, candidate.Asset, [dllAsset], release.TargetCommit);
        }

        var primary = binaries.OrderByDescending(match => CudaVersionPreference(match.Version, cudaPackagePreference)).First().Asset;
        return new RuntimePackageSelection(preset, release.TagName, release.HtmlUrl, release.PublishedAt, primary, [], release.TargetCommit);
    }

    private static (int FamilyScore, Version Version) CudaVersionPreference(string value, string cudaPackagePreference)
    {
        var normalized = value.Trim();
        var familyScore = PackagePreferencePolicy.NormalizeCuda(cudaPackagePreference) == "compatibility"
            && normalized.StartsWith("12.", StringComparison.OrdinalIgnoreCase)
                ? 2
                : 1;
        return Version.TryParse(normalized, out var version)
            ? (familyScore, version)
            : (familyScore, new Version(0, 0));
    }

    private static string MatchGroup(string text, string pattern, string group)
    {
        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[group].Value : "";
    }
}
