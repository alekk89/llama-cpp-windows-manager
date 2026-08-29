using System.Globalization;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole;

public static class BenchmarksPagePresentationService
{
    public static void ApplyRuns(BenchmarksPageState page, IReadOnlyList<BenchmarkRunSnapshot> runs, CultureInfo culture)
    {
        page.SetHistory(runs.Select(run => new BenchmarkRunRow(
            run.Job.Id,
            run.Job.CreatedAt.ToLocalTime().ToString("g", culture),
            run.Job.Status.ToString(),
            $"{run.Payload.WorkItems.Count} item(s)",
            $"{run.Payload.CompletedWorkItems + run.Payload.FailedWorkItems}/{run.Payload.WorkItems.Count}",
            run.Payload.Message)).ToArray());
        if (page.HistoryPage is not null) page.HistoryPage.Text = $"Page {(page.HistoryOffset / page.HistoryPageSize) + 1}";
        if (page.HistoryPrevious is not null) page.HistoryPrevious.IsEnabled = page.HistoryOffset > 0;
        if (page.HistoryNext is not null) page.HistoryNext.IsEnabled = runs.Count == page.HistoryPageSize;
        var active = runs.FirstOrDefault(run => run.Job.Status is JobStatus.Queued or JobStatus.Running or JobStatus.Paused);
        page.IsRunActive = active is not null;
        page.ActiveRunId = active?.Job.Id ?? "";
        if (page.RunButton is not null) page.RunButton.IsEnabled = active is null;
        if (page.StopButton is not null) page.StopButton.IsEnabled = active is not null;
        if (active is not null)
            ApplyProgress(page, active);
        else if (page.ActiveStatus is not null && page.Progress is not null)
        {
            page.ActiveStatus.Text = Loc.T("Benchmarks.NoActiveRun");
            page.Progress.Maximum = 1;
            page.Progress.Value = 0;
        }
    }

    public static void ApplyProgress(BenchmarksPageState page, BenchmarkRunSnapshot run)
    {
        if (page.ActiveStatus is null || page.Progress is null) return;
        page.IsRunActive = run.Job.Status is JobStatus.Queued or JobStatus.Running or JobStatus.Paused;
        page.ActiveRunId = page.IsRunActive ? run.Job.Id : "";
        if (page.RunButton is not null) page.RunButton.IsEnabled = !page.IsRunActive;
        if (page.StopButton is not null) page.StopButton.IsEnabled = page.IsRunActive;
        var total = Math.Max(run.Payload.WorkItems.Count, 1);
        var finished = run.Payload.CompletedWorkItems + run.Payload.FailedWorkItems;
        var index = Math.Clamp(run.Payload.CurrentWorkItemIndex, 0, total - 1);
        var item = run.Payload.WorkItems.ElementAtOrDefault(index);
        var expectedRows = run.Payload.WorkItems.Sum(workItem => workItem.ExpectedResultRows);
        var elapsed = run.Payload.StartedAt is { } started
            ? ((run.Payload.CompletedAt ?? DateTimeOffset.UtcNow) - started).ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture)
            : "00:00:00";
        var target = item is null ? "" : $" · {item.ModelName} · {item.RuntimeName} · {string.Join(", ", item.ProfileNames)}";
        page.ActiveStatus.Text = $"{run.Payload.Plan.Name} · {run.Job.Status} · item {index + 1} of {total}{target}"
            + $" · rows {run.Payload.ResultRows}/{expectedRows} · elapsed {elapsed}{Environment.NewLine}{run.Payload.Message}";
        page.Progress.Maximum = total;
        page.Progress.Value = Math.Min(finished, total);
    }
}
