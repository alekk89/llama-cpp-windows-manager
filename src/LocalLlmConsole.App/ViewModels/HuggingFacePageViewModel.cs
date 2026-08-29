using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class HuggingFacePageViewModel
{
    public ObservableCollection<HuggingFaceSearchRow> SearchRows { get; } = new();
    public ObservableCollection<HuggingFaceDownloadRow> DownloadHistoryRows { get; } = new();

    public void ReplaceSearchResults(
        IEnumerable<HuggingFaceFile> files,
        HuggingFaceInstallInventory installed,
        string modelsRoot)
    {
        SearchRows.Clear();
        foreach (var file in files)
        {
            var isInstalled = HuggingFaceInstallStateService.IsInstalled(file, installed, modelsRoot);
            SearchRows.Add(new HuggingFaceSearchRow
            {
                File = file,
                Repo = file.Repo,
                FilePath = file.Path,
                Quant = file.Quant,
                Size = DisplayFormatService.Bytes(file.SizeBytes),
                Downloads = file.Downloads.ToString("N0"),
                Signals = SearchSignals(file),
                DownloadAction = isInstalled ? "Installed" : "Download",
                DownloadToolTip = isInstalled
                    ? "This model file is already in the models folder."
                    : "Download this GGUF model file into the models folder.",
                CardToolTip = "Open this repository's Hugging Face model card.",
                CanDownload = !isInstalled
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
                return new HuggingFaceDownloadRow
                {
                    Job = job,
                    Status = job.Status.ToString(),
                    Model = payload is null ? job.Id : $"{payload.File.Name} - {payload.File.Repo}",
                    Progress = HuggingFaceInstallStateService.FormatDownloadProgress(payload),
                    Size = payload?.TotalBytes > 0 ? DisplayFormatService.Bytes(payload.TotalBytes) : "",
                    Updated = job.UpdatedAt.ToLocalTime().ToString("g"),
                    Destination = payload?.Destination ?? "",
                    StartAction = HuggingFaceInstallStateService.DownloadStartLabel(job.Status),
                    CanStart = HuggingFaceInstallStateService.CanStartDownload(job.Status),
                    CanPause = HuggingFaceInstallStateService.CanPauseDownload(job.Status),
                    CanStop = HuggingFaceInstallStateService.CanStopDownload(job.Status)
                };
            }).ToArray();
        ReconcileDownloadHistory(rows);
    }

    private void ReconcileDownloadHistory(IReadOnlyList<HuggingFaceDownloadRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
        {
            var desired = rows[index];
            var existingIndex = index < DownloadHistoryRows.Count
                                && DownloadHistoryRows[index].JobId.Equals(desired.JobId, StringComparison.OrdinalIgnoreCase)
                ? index
                : FindDownloadHistoryIndex(desired.JobId, index + 1);
            if (existingIndex >= 0)
            {
                if (existingIndex != index)
                    DownloadHistoryRows.Move(existingIndex, index);
                DownloadHistoryRows[index].Apply(desired);
            }
            else
            {
                DownloadHistoryRows.Insert(index, desired);
            }
        }

        while (DownloadHistoryRows.Count > rows.Count)
            DownloadHistoryRows.RemoveAt(DownloadHistoryRows.Count - 1);
    }

    private int FindDownloadHistoryIndex(string jobId, int startIndex)
    {
        for (var index = Math.Max(0, startIndex); index < DownloadHistoryRows.Count; index++)
            if (DownloadHistoryRows[index].JobId.Equals(jobId, StringComparison.OrdinalIgnoreCase)) return index;
        return -1;
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
