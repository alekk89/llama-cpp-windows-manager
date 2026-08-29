namespace LocalLlmConsole.Services;

public static class RuntimePackageInventoryPresenter
{
    public static IReadOnlyList<RuntimeRecord> InstalledPackages(IReadOnlyList<RuntimeRecord> runtimes, RuntimePackagePreset preset)
        => runtimes
            .Where(runtime => string.Equals(RuntimeMetadataService.ManagedPackageId(runtime), preset.Id, StringComparison.OrdinalIgnoreCase)
                || RuntimeMetadataService.EquivalentPackageIds(runtime).Contains(preset.Id, StringComparer.OrdinalIgnoreCase))
            .GroupBy(runtime => RuntimeMetadataService.Folder(runtime), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(runtime => runtime.UpdatedAt).First())
            .OrderByDescending(runtime => runtime.UpdatedAt)
            .ToList();

    public static IReadOnlyList<RuntimeRecord> MatchingSourceBuilds(IReadOnlyList<RuntimeRecord> runtimes, RuntimePackagePreset preset)
        => runtimes
            .Where(runtime => RuntimeMetadataService.IsManagedSourceBuild(runtime)
                && string.Equals(RuntimeMetadataService.ManagedPresetId(runtime), preset.SourcePresetId, StringComparison.OrdinalIgnoreCase))
            .GroupBy(runtime => RuntimeMetadataService.Folder(runtime), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(runtime => runtime.UpdatedAt).First())
            .OrderByDescending(runtime => runtime.UpdatedAt)
            .ToList();

    public static string LatestInstalledTag(IReadOnlyList<RuntimeRecord> installed)
        => installed
            .Select(RuntimeMetadataService.PackageTag)
            .FirstOrDefault(tag => !string.IsNullOrWhiteSpace(tag)) ?? "";

    public static string LatestInstalledAssetSummary(IReadOnlyList<RuntimeRecord> installed)
        => installed
            .Select(RuntimeMetadataService.PackageAssetSummary)
            .FirstOrDefault(summary => !string.IsNullOrWhiteSpace(summary)) ?? "";

    public static string LatestSourceCommit(IReadOnlyList<RuntimeRecord> sourceBuilds)
        => sourceBuilds
            .Select(RuntimeMetadataService.Commit)
            .FirstOrDefault(commit => !string.IsNullOrWhiteSpace(commit)) ?? "";

    public static string LocalIdentity(IReadOnlyList<RuntimeRecord> installed, IReadOnlyList<RuntimeRecord> sourceBuilds)
    {
        var packageTag = LatestInstalledTag(installed);
        if (!string.IsNullOrWhiteSpace(packageTag)) return $"package:{packageTag}";
        var sourceCommit = LatestSourceCommit(sourceBuilds);
        if (!string.IsNullOrWhiteSpace(sourceCommit)) return $"source:{sourceCommit}";
        return "";
    }

    public static bool CanInstallPackage(IReadOnlyList<RuntimeRecord> installed, IReadOnlyList<RuntimeRecord> sourceBuilds, RuntimePackageUpdateState? updateState)
        => (updateState is null || updateState.IsAvailable)
            && (installed.Count == 0 || updateState?.HasUpdate == true);

    public static bool CanInstallPackage(IReadOnlyList<RuntimeRecord> installed, RuntimePackageUpdateState? updateState)
        => CanInstallPackage(installed, [], updateState);

    public static string InstallButtonLabel(IReadOnlyList<RuntimeRecord> installed, IReadOnlyList<RuntimeRecord> sourceBuilds, RuntimePackageUpdateState? updateState)
    {
        if (updateState?.HasUpdate == true) return "Update";
        return installed.Count > 0 ? "Installed" : "Install";
    }

    public static string InstallButtonLabel(IReadOnlyList<RuntimeRecord> installed, RuntimePackageUpdateState? updateState)
        => InstallButtonLabel(installed, [], updateState);

    public static string LocalStatusLabel(IReadOnlyList<RuntimeRecord> installed, IReadOnlyList<RuntimeRecord> sourceBuilds, RuntimePackageUpdateState? updateState = null)
    {
        if (installed.Count > 0)
        {
            if (installed.Any(runtime => RuntimeMetadataService.EquivalentPackageIds(runtime).Count > 0))
                return installed.Count == 1 ? "Exact source match" : $"{installed.Count} exact matches";
            return installed.Count == 1 ? "Installed" : $"{installed.Count} installed";
        }

        if (updateState is { IsAvailable: false })
            return "Not published";

        if (sourceBuilds.Count > 0)
            return sourceBuilds.Count == 1 ? "Built from source" : $"{sourceBuilds.Count} source builds";

        return "Not installed";
    }

    public static string LocalStatusLabel(IReadOnlyList<RuntimeRecord> installed)
        => installed.Count switch
        {
            0 => "Not installed",
            1 => "Installed",
            _ => $"{installed.Count} installed"
        };

    public static string LatestLocalLabel(IReadOnlyList<RuntimeRecord> installed, IReadOnlyList<RuntimeRecord> sourceBuilds, RuntimePackageUpdateState? updateState)
    {
        var localTag = LatestInstalledTag(installed);
        var sourceCommit = LatestSourceCommit(sourceBuilds);
        if (updateState is not null)
        {
            if (!updateState.IsAvailable)
                return $"not published in {DisplayTag(updateState.LatestTag)} - checked {updateState.CheckedAt.ToLocalTime():g}";

            if (updateState.HasUpdate)
            {
                var local = !string.IsNullOrWhiteSpace(localTag)
                    ? DisplayTag(localTag)
                    : RuntimeMetadataService.DisplayCommit(sourceCommit);
                var latest = !string.IsNullOrWhiteSpace(updateState.TargetCommit)
                    ? $"{DisplayTag(updateState.LatestTag)} {RuntimeMetadataService.DisplayCommit(updateState.TargetCommit)}"
                    : DisplayTag(updateState.LatestTag);
                return $"update available {local} -> {latest}";
            }

            return string.IsNullOrWhiteSpace(localTag)
                ? !string.IsNullOrWhiteSpace(sourceCommit)
                    ? $"source matches {DisplayTag(updateState.LatestTag)} - checked {updateState.CheckedAt.ToLocalTime():g}"
                    : $"latest {DisplayTag(updateState.LatestTag)} - checked {updateState.CheckedAt.ToLocalTime():g}"
                : $"current {DisplayTag(localTag)} - checked {updateState.CheckedAt.ToLocalTime():g}";
        }

        if (!string.IsNullOrWhiteSpace(sourceCommit))
            return $"source built {RuntimeMetadataService.DisplayCommit(sourceCommit)} - {sourceBuilds[0].UpdatedAt.ToLocalTime():g}";
        if (installed.Count == 0) return "";
        return string.IsNullOrWhiteSpace(localTag)
            ? $"installed - {installed[0].UpdatedAt.ToLocalTime():g}"
            : $"installed {DisplayTag(localTag)} - {installed[0].UpdatedAt.ToLocalTime():g}";
    }

    public static string DisplayTag(string tag)
        => string.IsNullOrWhiteSpace(tag) ? "n/a" : tag;
}
