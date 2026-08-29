using System.Windows;
using System.Windows.Controls;

namespace LocalLlmConsole;

public sealed record BenchmarksPageActions(
    Func<Task> RefreshAsync,
    Func<Task> ValidateAsync,
    Func<Task> RunAsync,
    Func<Task> PauseAsync,
    Func<Task> ResumeAsync,
    Func<Task> CancelAsync,
    Func<Task> DetailsAsync,
    Func<Task> ExportAsync,
    Func<Task> CompareAsync,
    Func<Task> CloneAsync,
    Func<Task> ImportPlanAsync,
    Func<Task> ExportPlanAsync,
    Func<Task> OpenLogAsync,
    Func<Task> PreviousHistoryPageAsync,
    Func<Task> NextHistoryPageAsync,
    Func<Task> SelectionChangedAsync,
    Func<int> SelectedRunCount,
    Func<BenchmarkRunRow, Task> RemoveRunAsync,
    Action AddProfile,
    Action<BenchmarkScopeRow> RemoveProfile,
    Action ClearProfiles,
    Action PlanChanged,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class BenchmarksPageController
{
    private readonly BenchmarksPageActions _actions;

    public BenchmarksPageController(BenchmarksPageActions actions) => _actions = actions;

    public async void Refresh(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.RefreshAsync);
    public async void Validate(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.ValidateAsync);
    public async void Run(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.RunAsync);
    public async void Pause(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.PauseAsync);
    public async void Resume(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.ResumeAsync);
    public async void Cancel(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.CancelAsync);
    public async void Details(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.DetailsAsync);
    public async void Export(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.ExportAsync);
    public async void Compare(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.CompareAsync);
    public async void Clone(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.CloneAsync);
    public async void ImportPlan(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.ImportPlanAsync);
    public async void ExportPlan(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.ExportPlanAsync);
    public async void OpenLog(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.OpenLogAsync);
    public async void PreviousHistoryPage(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.PreviousHistoryPageAsync);
    public async void NextHistoryPage(object sender, RoutedEventArgs e) => await _actions.RunEventAsync(_actions.NextHistoryPageAsync);
    public async void SelectionChanged(object sender, SelectionChangedEventArgs e) => await _actions.RunEventAsync(_actions.SelectionChangedAsync);
    public async void ActivateHistorySelection(object sender, RoutedEventArgs e)
    {
        var action = _actions.SelectedRunCount() switch
        {
            1 => _actions.DetailsAsync,
            2 => _actions.CompareAsync,
            _ => () => Task.FromException(new InvalidOperationException("Select one run to view its report or exactly two runs to compare."))
        };
        await _actions.RunEventAsync(action);
    }
    public async void RemoveRun(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: BenchmarkRunRow row }) return;
        await _actions.RunEventAsync(() => _actions.RemoveRunAsync(row));
    }
    public void AddProfile(object sender, RoutedEventArgs e) => MutateScope(_actions.AddProfile);
    public void RemoveProfile(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { Tag: BenchmarkScopeRow row }) return;
        _actions.RemoveProfile(row);
        _actions.PlanChanged();
    }
    public void ClearProfiles(object sender, RoutedEventArgs e) => MutateScope(_actions.ClearProfiles);
    public void PlanChanged(object sender, RoutedEventArgs e) => _actions.PlanChanged();
    public void PlanTextChanged(object sender, TextChangedEventArgs e) => _actions.PlanChanged();
    public void PlanSelectionChanged(object sender, SelectionChangedEventArgs e) => _actions.PlanChanged();
    public void NotifyPlanChanged() => _actions.PlanChanged();

    private void MutateScope(Action mutation)
    {
        mutation();
        _actions.PlanChanged();
    }
}
