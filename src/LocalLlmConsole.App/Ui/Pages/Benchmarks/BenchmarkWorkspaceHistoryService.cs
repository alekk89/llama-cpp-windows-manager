using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole;

public static class BenchmarkWorkspaceHistoryService
{
    public static async Task ShowAsync(BenchmarkApplicationService benchmarks, StateStore store, string runId, BenchmarksPageState? page, Func<BenchmarksPageState?> currentPage)
    {
        var run = await benchmarks.InspectAsync(runId);
        var results = await BenchmarkExportService.LoadAllAsync(store, runId, includePartialAttempts: true);
        if (page != currentPage()) return;
        page?.Workspace?.ShowResults(run.Payload.Plan.Name + " · " + run.Job.Status, EnrichBenchmarkRows(run, results));
    }

    private static IReadOnlyList<StoredBenchmarkResult> EnrichBenchmarkRows(BenchmarkRunSnapshot run, IReadOnlyList<StoredBenchmarkResult> rows)
        => rows.Select(row =>
        {
            var item = run.Payload.WorkItems.FirstOrDefault(item => item.Key == row.WorkItemKey);
            return item is null ? row : row with
            {
                Result = row.Result with
                {
                    ManagerModelName = item.ModelName,
                    ManagerRuntimeName = item.RuntimeName,
                    ProfileName = string.Join(", ", item.ProfileNames)
                }
            };
        }).ToArray();

    public static async Task CompareAsync(BenchmarkApplicationService benchmarks, StateStore store, BenchmarksPageState? page, Func<BenchmarksPageState?> currentPage)
    {
        var ids = page?.SelectedRunIds ?? [];
        if (ids.Count != 2) throw new InvalidOperationException("Select exactly two runs to compare.");
        var rows = new List<StoredBenchmarkResult>();
        var names = new List<string>();
        foreach (var id in ids)
        {
            var run = await benchmarks.InspectAsync(id);
            names.Add(run.Payload.Plan.Name);
            rows.AddRange(EnrichBenchmarkRows(run, await BenchmarkExportService.LoadAllAsync(store, id, false)));
        }
        if (page == currentPage()) page?.Workspace?.ShowResults(string.Join(" / ", names), rows);
    }

}
