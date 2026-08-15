using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class HuggingFacePageViewModel
{
    public ObservableCollection<UiRow> SearchRows { get; } = new();
    public ObservableCollection<UiRow> DownloadHistoryRows { get; } = new();

    public void ReplaceSearchResults(
        IEnumerable<HuggingFaceFile> files,
        HuggingFaceInstallInventory installed,
        string modelsRoot)
    {
        SearchRows.Clear();
        foreach (var file in files)
        {
            var isInstalled = HuggingFaceInstallStateService.IsInstalled(file, installed, modelsRoot);
            SearchRows.Add(new UiRow
            {
                C1 = file.Repo,
                C2 = file.Path,
                C3 = file.Quant,
                C4 = DisplayFormatService.Bytes(file.SizeBytes),
                C5 = file.Downloads.ToString("N0"),
                C6 = SearchSignals(file),
                C7 = isInstalled ? "Installed" : "Download",
                C8 = "Card",
                T1 = isInstalled
                    ? "This model file is already in the models folder."
                    : "Download this GGUF model file into the models folder.",
                T2 = "Open this repository's Hugging Face model card.",
                B1 = !isInstalled,
                B2 = true,
                Data = JsonSerializer.SerializeToNode(file) as JsonObject ?? new JsonObject()
            });
        }
    }

    public void ReplaceDownloadHistory(IEnumerable<JobRecord> jobs)
    {
        var rows = jobs
            .Where(job => string.Equals(job.Kind, "huggingface-download", StringComparison.OrdinalIgnoreCase))
            .Select(job =>
            {
                var payload = HuggingFaceService.ParseDownloadPayload(job.PayloadJson);
                return new UiRow
                {
                    C1 = job.Status.ToString(),
                    C2 = payload is null ? job.Id : $"{payload.File.Name} - {payload.File.Repo}",
                    C3 = HuggingFaceInstallStateService.FormatDownloadProgress(payload),
                    C4 = payload?.TotalBytes > 0 ? DisplayFormatService.Bytes(payload.TotalBytes) : "",
                    C5 = job.UpdatedAt.ToLocalTime().ToString("g"),
                    C6 = payload?.Destination ?? "",
                    C7 = HuggingFaceInstallStateService.DownloadStartLabel(job.Status),
                    C8 = "Pause",
                    C9 = "Stop",
                    C10 = "Delete",
                    T1 = "Resume or restart this model download.",
                    T2 = "Pause this active model download.",
                    T3 = "Stop this model download and keep resumable partial data.",
                    T4 = "Delete this download history entry and any incomplete partial file.",
                    B1 = HuggingFaceInstallStateService.CanStartDownload(job.Status),
                    B2 = HuggingFaceInstallStateService.CanPauseDownload(job.Status),
                    B3 = HuggingFaceInstallStateService.CanStopDownload(job.Status),
                    B4 = true,
                    Data = JsonSerializer.SerializeToNode(job) as JsonObject ?? new JsonObject()
                };
            });
        UiRowCollectionUpdater.Reconcile(
            DownloadHistoryRows,
            rows,
            row => row.Data["Id"]?.ToString() ?? "");
    }

    private static string SearchSignals(HuggingFaceFile file)
    {
        var hints = (file.CapabilityHints ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var chips = new List<string>();

        if (file.HasVisionProjector || hints.Contains("vision"))
            chips.Add(file.HasVisionProjector ? "Vision + mmproj" : "Vision, mmproj unknown");
        foreach (var hint in hints.Where(hint => !hint.Equals("vision", StringComparison.OrdinalIgnoreCase)).Order(StringComparer.OrdinalIgnoreCase))
            chips.Add(HintLabel(hint));
        if (file.HasConfig) chips.Add("Config");
        if (file.HasTokenizer) chips.Add("Tokenizer");
        if (!string.IsNullOrWhiteSpace(file.License)) chips.Add($"License: {file.License}");

        return chips.Count == 0 ? "GGUF" : string.Join(" | ", chips.Distinct(StringComparer.OrdinalIgnoreCase).Take(6));
    }

    private static string HintLabel(string hint) => hint.ToLowerInvariant() switch
    {
        "fim" => "FIM",
        "moe" => "MoE",
        "draft" => "Draft/MTP",
        _ => CultureInfo.CurrentCulture.TextInfo.ToTitleCase(hint)
    };
}
