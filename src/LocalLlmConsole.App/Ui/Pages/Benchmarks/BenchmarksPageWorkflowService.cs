using System.Windows;
using LocalLlmConsole.Models;
using LocalLlmConsole.Services;

namespace LocalLlmConsole;

public static class BenchmarksPageWorkflowService
{
    public static async Task<BenchmarkPlanPreview> ValidateAsync(
        BenchmarkApplicationService benchmarks,
        BenchmarkPlan plan,
        BenchmarksPageState? page)
    {
        var preview = await benchmarks.ValidateAsync(plan);
        if (page?.Summary is null) return preview;
        if (page.RunButton is not null) page.RunButton.IsEnabled = !page.IsRunActive;
        var details = preview.IsValid
            ? Loc.T("Benchmarks.Validation.Counts", preview.WorkItems.Count, preview.ExpectedResultRows, preview.TimedRepetitions) + " "
              + (preview.DeduplicatedWorkItems > 0 ? Loc.T("Benchmarks.Validation.Collapsed", preview.DeduplicatedWorkItems) : Loc.T("Benchmarks.Validation.Ready"))
              + (preview.Warnings.Count > 0 ? $"{Environment.NewLine}{string.Join(Environment.NewLine, preview.Warnings)}" : "")
              + CommandPreview(plan, preview)
            : string.Join(Environment.NewLine, preview.Errors);
        page.Workspace?.SetValidationDetails(details);
        page.Workspace?.ShowPreview(preview);
        page.Summary.Text = preview.IsValid ? (preview.Warnings.Count > 0 ? Loc.T("Benchmarks.Validation.ReadyWithNotes") : Loc.T("Benchmarks.Validation.Ready"))
            : string.Join(Environment.NewLine, preview.Errors.Take(2));
        page.Summary.ToolTip = details;
        if (page.RunButton is not null) page.RunButton.IsEnabled = preview.IsValid && !page.IsRunActive;
        return preview;
    }

    private static string CommandPreview(BenchmarkPlan plan, BenchmarkPlanPreview preview)
    {
        var item = preview.WorkItems.FirstOrDefault();
        if (item is null) return "";
        if (plan.ExecutionMode == BenchmarkExecutionMode.ProfileServing)
            return Environment.NewLine + Loc.T("Benchmarks.Validation.FirstVariant", item.ProfileNames.FirstOrDefault() ?? "", item.RuntimeName,
                item.LaunchSettings?.ContextSize ?? 0, item.LaunchSettings?.BatchSize ?? 0, item.LaunchSettings?.MicroBatchSize ?? 0,
                string.Join(',', plan.Serving.Concurrencies));
        var modelPath = BenchmarkRuntimeToolAdapter.RuntimeVisiblePath(item.RuntimeMode, item.ModelPath);
        var arguments = BenchmarkCommandBuilder.Build(plan, item, modelPath);
        return Environment.NewLine + Loc.T("Benchmarks.Validation.FirstArgv", JsonSerializer.Serialize(arguments));
    }

    public static async Task<BenchmarkRunSnapshot?> StartAsync(
        BenchmarkApplicationService benchmarks,
        BenchmarkPlan plan,
        IReadOnlyList<LoadedModelSessionSnapshot> sessions,
        BenchmarksPageState? page,
        Window owner,
        DialogService dialogs)
    {
        var preview = await ValidateAsync(benchmarks, plan, page);
        if (!preview.IsValid) return null;
        var active = sessions.Where(session => session.IsRunning).ToArray();
        var sessionNotice = active.Length == 0
            ? Loc.T("Benchmarks.Start.NoSessions")
            : plan.StopActiveSessions
                ? Loc.T("Benchmarks.Start.SessionsStopped", active.Length, string.Join(Environment.NewLine, active.Select(session => $"• {session.ModelName}")))
                : Loc.T("Benchmarks.Start.SessionsBlock", active.Length);
        if (active.Length > 0 && !plan.StopActiveSessions)
        {
            dialogs.Notify(owner, sessionNotice, Loc.T("Benchmarks.Start.BlockedTitle"), MessageBoxImage.Warning);
            return null;
        }
        if (!dialogs.Confirm(
                owner,
                Loc.T("Benchmarks.Start.Confirm", preview.WorkItems.Count, sessionNotice),
                Loc.T("Benchmarks.Start.Title"),
                MessageBoxImage.Warning))
            return null;
        return await benchmarks.StartAsync(plan, confirmed: true);
    }

    public static async Task CloneAsync(
        BenchmarkApplicationService benchmarks,
        string jobId,
        BenchmarksPageState page,
        IReadOnlyList<NamedModelLaunchProfile> profiles)
    {
        var source = await benchmarks.InspectAsync(jobId);
        var clone = source.Payload.Plan with { Name = Loc.T("Benchmarks.CloneName", source.Payload.Plan.Name), StopActiveSessions = false };
        BenchmarksPagePlanService.Apply(page, clone, profiles);
    }

    public static async Task CloneAndValidateAsync(
        BenchmarkApplicationService benchmarks,
        string jobId,
        BenchmarksPageState page,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        string wslDistro)
    {
        await CloneAsync(benchmarks, jobId, page, profiles);
        await ValidateAsync(benchmarks, BenchmarksPagePlanService.Build(page, wslDistro), page);
    }

    public static async Task<bool> ImportAsync(
        BenchmarksPageState page,
        IReadOnlyList<NamedModelLaunchProfile> profiles)
    {
        var plan = await BenchmarksPagePlanFileService.ImportAsync();
        if (plan is null) return false;
        BenchmarksPagePlanService.Apply(page, plan, profiles);
        return true;
    }

    public static async Task ImportAndValidateAsync(
        BenchmarkApplicationService benchmarks,
        BenchmarksPageState page,
        IReadOnlyList<NamedModelLaunchProfile> profiles,
        string wslDistro)
    {
        if (await ImportAsync(page, profiles))
            await ValidateAsync(benchmarks, BenchmarksPagePlanService.Build(page, wslDistro), page);
    }

    public static async Task ExportPlanAsync(BenchmarkPlan plan, Action<string> setStatus)
    {
        var path = await BenchmarksPagePlanFileService.ExportAsync(plan);
        if (!string.IsNullOrWhiteSpace(path)) setStatus(Loc.T("Benchmarks.PlanExported", path));
    }

    public static async Task OpenLogAsync(BenchmarkApplicationService benchmarks, string jobId, Action<string> openLog)
    {
        var run = await benchmarks.InspectAsync(jobId);
        openLog(run.Job.LogPath);
    }

    public static async Task<bool> DeleteAsync(
        BenchmarkApplicationService benchmarks,
        string jobId,
        Window owner,
        DialogService dialogs)
    {
        var run = await benchmarks.InspectAsync(jobId);
        if (run.Job.Status is JobStatus.Queued or JobStatus.Running or JobStatus.Paused)
            throw new InvalidOperationException(Loc.T("Benchmarks.Delete.Active"));
        if (!dialogs.Confirm(owner, Loc.T("Benchmarks.Delete.Confirm", run.Payload.Plan.Name), Loc.T("Benchmarks.Delete.Title"), MessageBoxImage.Warning))
            return false;
        await benchmarks.DeleteAsync(run.Job.Id, confirmed: true);
        return true;
    }

    public static async Task DeleteAndRefreshAsync(
        BenchmarkApplicationService benchmarks,
        string jobId,
        Window owner,
        DialogService dialogs,
        Func<Task> refresh)
    {
        if (await DeleteAsync(benchmarks, jobId, owner, dialogs)) await refresh();
    }

    public static Task PreviousPageAsync(BenchmarksPageState? page, Func<Task> refresh)
    {
        if (page is null) return Task.CompletedTask;
        page.HistoryOffset = Math.Max(0, page.HistoryOffset - page.HistoryPageSize);
        return refresh();
    }

    public static Task NextPageAsync(BenchmarksPageState? page, Func<Task> refresh)
    {
        if (page is null || page.History?.Items.Count < page.HistoryPageSize) return Task.CompletedTask;
        page.HistoryOffset += page.HistoryPageSize;
        return refresh();
    }
}
