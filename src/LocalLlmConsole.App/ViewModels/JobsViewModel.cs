using System.Collections.ObjectModel;

namespace LocalLlmConsole.ViewModels;

public sealed class JobsViewModel
{
    public ObservableCollection<UiRow> Rows { get; } = new();
    public ObservableCollection<UiRow> RuntimeRows { get; } = new();

    public void ReplaceJobs(IEnumerable<JobRowProjection> jobs)
    {
        Rows.Clear();
        RuntimeRows.Clear();
        foreach (var projection in jobs)
        {
            Rows.Add(projection.Row);
            if (projection.Job.Kind.Contains("runtime", StringComparison.OrdinalIgnoreCase))
                RuntimeRows.Add(projection.Row);
        }
    }
}
