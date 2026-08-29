namespace LocalLlmConsole;

public sealed record OverviewPageActionControllerActions(
    Func<CancellationToken, Task> SelectModelSessionAsync,
    Func<Task> SelectLaunchProfileAsync,
    Action UpdateModelActions,
    Func<Task> LoadSelectedModelAsync,
    Func<CancellationToken, Task> SelectLoadedSessionRowAsync,
    Func<Task> InspectSelectedEndpointAsync,
    Func<object, OverviewSessionRow?> EndpointRowFromLink,
    Func<OverviewSessionRow, Task> InspectEndpointRowAsync,
    Func<object, string> SessionIdFromRowButton,
    Func<string, Task> UnloadLoadedSessionAsync,
    Func<OverviewDashboardLayout, Task> PersistDashboardLayoutAsync,
    Func<Func<Task>, Task> RunEventAsync);

public sealed class OverviewPageActionController
{
    private readonly OverviewPageActionControllerActions _actions;
    private CancellationTokenSource? _modelSelection;
    private CancellationTokenSource? _loadedSessionSelection;

    public OverviewPageActionController(OverviewPageActionControllerActions actions)
    {
        _actions = actions;
    }

    public OverviewPageActions Build()
        => new(
            SelectModelSessionAsync,
            _actions.SelectLaunchProfileAsync,
            _actions.LoadSelectedModelAsync,
            SelectLoadedSessionRowAsync,
            InspectSelectedEndpointAsync,
            InspectEndpointRow_Click,
            UnloadLoadedSessionRow_Click,
            _actions.PersistDashboardLayoutAsync,
            _actions.RunEventAsync);

    public void CancelPendingSelections()
    {
        Interlocked.Exchange(ref _modelSelection, null)?.Cancel();
        Interlocked.Exchange(ref _loadedSessionSelection, null)?.Cancel();
    }

    private async Task SelectModelSessionAsync()
    {
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _modelSelection, current);
        previous?.Cancel();
        try
        {
            await _actions.RunEventAsync(() => _actions.SelectModelSessionAsync(current.Token));
            if (!current.IsCancellationRequested)
                _actions.UpdateModelActions();
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref _modelSelection, null, current);
            current.Dispose();
        }
    }

    private async Task SelectLoadedSessionRowAsync()
    {
        var current = new CancellationTokenSource();
        var previous = Interlocked.Exchange(ref _loadedSessionSelection, current);
        previous?.Cancel();
        try
        {
            await _actions.RunEventAsync(() => _actions.SelectLoadedSessionRowAsync(current.Token));
        }
        catch (OperationCanceledException) when (current.IsCancellationRequested)
        {
        }
        finally
        {
            Interlocked.CompareExchange(ref _loadedSessionSelection, null, current);
            current.Dispose();
        }
    }

    private async void UnloadLoadedSessionRow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        await _actions.RunEventAsync(async () =>
        {
            var sessionId = _actions.SessionIdFromRowButton(sender);
            if (!string.IsNullOrWhiteSpace(sessionId))
                await _actions.UnloadLoadedSessionAsync(sessionId);
        });
    }

    private Task InspectSelectedEndpointAsync()
        => _actions.RunEventAsync(_actions.InspectSelectedEndpointAsync);

    private async void InspectEndpointRow_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        e.Handled = true;
        await _actions.RunEventAsync(async () =>
        {
            if (_actions.EndpointRowFromLink(sender) is { } row)
                await _actions.InspectEndpointRowAsync(row);
        });
    }
}
