namespace LocalLlmConsole.Services;

public sealed class UiAsyncRefreshTimerController
{
    private readonly IUiTimerFactory _timerFactory;
    private IUiTimer? _timer;
    private int _refreshing;

    public UiAsyncRefreshTimerController(IUiTimerFactory timerFactory)
    {
        _timerFactory = timerFactory ?? throw new ArgumentNullException(nameof(timerFactory));
    }

    public bool IsRunning => _timer is not null;

    public void Start(
        TimeSpan interval,
        Func<Task> refreshAsync,
        Action<Exception> onError,
        bool runImmediately = false)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);
        ArgumentNullException.ThrowIfNull(onError);

        Stop();
        _timer = _timerFactory.Create(interval);
        _timer.Tick += async (_, _) => await RunTickAsync(refreshAsync, onError);
        _timer.Start();
        if (runImmediately)
            _ = RunTickAsync(refreshAsync, onError);
    }

    public void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private async Task RunTickAsync(Func<Task> refreshAsync, Action<Exception> onError)
    {
        if (Interlocked.CompareExchange(ref _refreshing, 1, 0) != 0) return;
        try
        {
            await RunOnceAsync(refreshAsync, onError);
        }
        finally
        {
            Volatile.Write(ref _refreshing, 0);
        }
    }

    public static async Task RunOnceAsync(
        Func<Task> refreshAsync,
        Action<Exception> onError)
    {
        ArgumentNullException.ThrowIfNull(refreshAsync);
        ArgumentNullException.ThrowIfNull(onError);

        try
        {
            await refreshAsync();
        }
        catch (Exception ex)
        {
            onError(ex);
        }
    }
}
