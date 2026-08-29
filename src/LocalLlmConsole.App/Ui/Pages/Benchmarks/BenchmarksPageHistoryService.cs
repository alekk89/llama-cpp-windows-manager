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
        var summary = $"{run.Job.Status} · {run.Payload.CompletedWorkItems + run.Payload.FailedWorkItems}/{run.Payload.WorkItems.Count} · {run.PersistedResultRows}";
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
        setStatus($"Exported {results.Count} benchmark result row(s) to {dialog.FileName}.");
    }

    public static async Task ShowComparisonAsync(
        StateStore store,
        IReadOnlyList<string> jobIds,
        Window owner,
        DialogService dialogs)
    {
        if (jobIds.Count != 2) throw new InvalidOperationException("Select exactly two benchmark runs to compare.");
        var baseline = await BenchmarkExportService.LoadAllAsync(store, jobIds[0], includePartialAttempts: false);
        var candidate = await BenchmarkExportService.LoadAllAsync(store, jobIds[1], includePartialAttempts: false);
        var rows = BenchmarkComparisonService.Compare(baseline, candidate);
        var lines = rows.Take(50).Select(row =>
            $"{row.Classification} {row.PromptTokens}/{row.GenerationTokens} ctx {row.ContextSize} batch {row.BatchSize} depth {row.Depth}: "
            + $"{row.BaselineTokensPerSecond:0.00} → {row.CandidateTokensPerSecond:0.00} tok/s ({row.PercentChange:+0.00;-0.00;0.00}%)"
            + (row.EnvironmentMatches ? "" : " · environment differs"));
        var message = rows.Count == 0
            ? "The selected runs have no completed rows with matching workload signatures."
            : string.Join(Environment.NewLine, lines)
              + (rows.Count > 50 ? $"\n\nShowing 50 of {rows.Count} comparable workloads." : "");
        dialogs.Notify(owner, message, "Benchmark comparison", MessageBoxImage.Information);
    }
}
