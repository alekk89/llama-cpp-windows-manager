using System.Text;
using System.Windows;
using LocalLlmConsole.Localization;
using LocalLlmConsole.Services;
using Forms = System.Windows.Forms;

namespace LocalLlmConsole;

public static class BenchmarksPageHistoryService
{
    public static async Task ShowDetailsAsync(
        BenchmarkApplicationService benchmarks,
        StateStore store,
        string jobId,
        Window owner)
    {
        var run = await benchmarks.InspectAsync(jobId);
        var results = await BenchmarkExportService.LoadAllAsync(store, jobId, includePartialAttempts: true);
        var summary = $"{Loc.T($"Benchmarks.Status.{run.Job.Status}")} · {run.Payload.CompletedWorkItems + run.Payload.FailedWorkItems}/{run.Payload.WorkItems.Count} · {run.PersistedResultRows}";
        BenchmarkReportWindow.Show(
            owner,
            run.Payload.Plan.Name,
            summary,
            BenchmarkSpeedReportService.Build(results));
    }

    public static async Task ExportAsync(
        BenchmarkApplicationService benchmarks,
        StateStore store,
        string jobId,
        Action<string> setStatus)
    {
        await benchmarks.InspectAsync(jobId);
        using var dialog = new Forms.SaveFileDialog
        {
            Title = Loc.T("Benchmarks.ExportTitle"),
            Filter = Loc.T("Benchmarks.ExportFilter"),
            FileName = $"benchmark-{jobId}.csv",
            AddExtension = true,
            OverwritePrompt = true
        };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        var results = await BenchmarkExportService.LoadAllAsync(store, jobId, includePartialAttempts: true);
        var content = Path.GetExtension(dialog.FileName).Equals(".json", StringComparison.OrdinalIgnoreCase)
            ? BenchmarkExportService.Json(results)
            : BenchmarkExportService.Csv(results);
        await File.WriteAllTextAsync(dialog.FileName, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        setStatus(Loc.T("Benchmarks.ResultsExported", results.Count, dialog.FileName));
    }

    public static async Task ShowComparisonAsync(
        StateStore store,
        IReadOnlyList<string> jobIds,
        Window owner,
        DialogService dialogs)
    {
        if (jobIds.Count != 2) throw new InvalidOperationException(Loc.T("Benchmarks.SelectTwoRuns"));
        var baseline = await BenchmarkExportService.LoadAllAsync(store, jobIds[0], includePartialAttempts: false);
        var candidate = await BenchmarkExportService.LoadAllAsync(store, jobIds[1], includePartialAttempts: false);
        var rows = BenchmarkComparisonService.Compare(baseline, candidate);
        var lines = rows.Take(50).Select(row =>
            Loc.T("Benchmarks.Comparison.Row", row.Classification, row.PromptTokens, row.GenerationTokens, row.ContextSize, row.BatchSize, row.Depth,
                row.BaselineTokensPerSecond.ToString("0.00", Loc.FormatCulture), row.CandidateTokensPerSecond.ToString("0.00", Loc.FormatCulture),
                row.PercentChange.ToString("+0.00;-0.00;0.00", Loc.FormatCulture))
            + (row.EnvironmentMatches ? "" : $" · {Loc.T("Benchmarks.Comparison.EnvironmentDiffers")}"));
        var message = rows.Count == 0
            ? Loc.T("Benchmarks.Comparison.None")
            : string.Join(Environment.NewLine, lines)
              + (rows.Count > 50 ? "\n\n" + Loc.T("Benchmarks.Comparison.Truncated", 50, rows.Count) : "");
        dialogs.Notify(owner, message, Loc.T("Benchmarks.Comparison.Title"), MessageBoxImage.Information);
    }
}
