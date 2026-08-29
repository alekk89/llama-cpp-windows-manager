namespace LocalLlmConsole.Services;

public static partial class RuntimeBuildCatalogService
{
    public static bool CanDownloadPreset(
        IReadOnlyList<RuntimeRecord> installed,
        IReadOnlyList<RuntimeSourceEntry> downloaded,
        RuntimeUpdateState? checkedState)
    {
        if (downloaded.Count > 0) return false;
        return checkedState?.HasUpdate == true;
    }

    public static string DownloadButtonLabel(
        IReadOnlyList<RuntimeSourceEntry> downloaded,
        IReadOnlyList<RuntimeRecord> installed,
        RuntimeUpdateState? checkedState)
    {
        if (downloaded.Count > 0) return "Downloaded";
        if (checkedState?.HasUpdate == true) return "Download";
        if (installed.Count > 0) return "Built";
        return "Check first";
    }

    public static string LocalStatusLabel(
        IReadOnlyList<RuntimeSourceEntry> downloaded,
        IReadOnlyList<RuntimeRecord> installed,
        bool commitUnavailable)
    {
        if (commitUnavailable) return "Version unknown";
        if (downloaded.Count == 0 && installed.Count == 0) return "Not downloaded";
        if (downloaded.Count > 0 && installed.Count == 0) return "Downloaded";
        if (downloaded.Count == 0 && installed.Count > 0) return installed.Count == 1 ? "Built" : $"{installed.Count} built";
        return $"{downloaded.Count} downloaded, {installed.Count} built";
    }

    public static string LatestLocalCommitLabel(IReadOnlyList<RuntimeSourceEntry> downloaded, IReadOnlyList<RuntimeRecord> installed)
    {
        var commit = LatestLocalCommitValue(downloaded, installed);
        var source = downloaded.FirstOrDefault();
        if (source is not null)
            return string.IsNullOrWhiteSpace(commit)
                ? $"downloaded source, local commit unavailable - {source.DownloadedAt.ToLocalTime():g}"
                : $"downloaded {RuntimeMetadataService.DisplayCommit(commit)} - {source.DownloadedAt.ToLocalTime():g}";
        var latest = installed.FirstOrDefault();
        if (latest is null) return "";
        return string.IsNullOrWhiteSpace(commit)
            ? $"built runtime, local commit unavailable - {latest.UpdatedAt.ToLocalTime():g}"
            : $"built {RuntimeMetadataService.DisplayCommit(commit)} - {latest.UpdatedAt.ToLocalTime():g}";
    }

    public static string LatestLocalCommitValue(IReadOnlyList<RuntimeSourceEntry> downloaded, IReadOnlyList<RuntimeRecord> installed)
    {
        var source = downloaded.FirstOrDefault();
        if (source is not null) return SourceCommit(source);
        var latest = installed.FirstOrDefault();
        return latest is null ? "" : RuntimeMetadataService.Commit(latest);
    }

    public static string ModeLabel(RuntimeMode mode)
        => NormalizeBuildMode(mode) == RuntimeMode.Native ? "Windows" : "WSL";

    public static string BackendLabel(RuntimeBuildPreset preset)
        => $"{BackendName(BuildBackend(preset))} {ModeLabel(preset.Mode)}";

    public static string BackendLabel(RuntimeSourceEntry source)
        => $"{BackendName(BuildBackend(source))} {ModeLabel(source.Mode)}";
}
